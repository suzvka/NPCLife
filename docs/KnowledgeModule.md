# 知识库模块接口文档

## 设计原则

框架组件（`AgentLoop`、`KnowledgeMcpProvider`）只依赖一个公共接口 **`IKnowledgeService`**，它声明了"我需要查词条 / 存词条"的契约。框架完全不关心背后的知识源组织方式——几个库、本地还是远程、精确还是模糊——均由实现方决定。

框架提供一份**默认实现** `KnowledgeService`，内部将内置缓存（可写）与外部知识源（只读）统一聚合。若默认实现无法满足需求，第三方直接实现 `IKnowledgeService` 即可，无需触碰框架代码。

---

## 1. 公共接口（框架消费端唯一依赖）

```csharp
// Core/IKnowledgeService.cs
public interface IKnowledgeService
{
    IReadOnlyList<KnowledgeEntry> Lookup(string term);
    void Store(KnowledgeEntry entry);
    void Delete(string term);
    IReadOnlyList<KnowledgeEntry> ListAll();
    IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags);
    IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix);
}
```

| 方法 | 约定 |
|------|------|
| `Lookup(term)` | 返回所有来源的匹配结果。调用方根据 `KnowledgeEntry.Source` 区分出处。无命中返回空列表。 |
| `Store(entry)` | 存储/覆盖。Term 为空时静默忽略。 |
| `Delete(term)` | 不存在时静默返回，不抛异常。 |
| `ListAll` / `ListByTags` / `ListByPrefix` | 用于 Agent 探索已知知识范围。 |

---

## 2. 数据模型

```csharp
// Core/KnowledgeEntry.cs
public class KnowledgeEntry
{
    public string Term;            // 词条名（大小写不敏感）
    public string Definition;      // 释义文本
    public string Source;          // 来源名，如 "BuiltIn"、"GameDef"、"Wiki"、"AgentDeduction"、"RAG"
    public float Confidence;       // 信心度 0.0~1.0
    public List<string> ContextTags; // 语义标签
}
```

`Source` 为自由字符串，调用方据此判断可信度。

---

## 3. 框架默认实现：KnowledgeService

```csharp
// Core/KnowledgeService.cs
public class KnowledgeService : IKnowledgeService
{
    // 构造：传入可写缓存 + 任意数量的只读外部源
    public KnowledgeService(
        IKnowledgeBase writableCache,
        IReadOnlyList<IExternalKnowledgeSource> readOnlySources = null
    );
}
```

**查询逻辑** (`Lookup`)：
- 并行查询 `writableCache` + 所有 `readOnlySources`
- 不短路——同一个 Term 在不同来源的释义全部返回
- 每条结果的 `Source` 字段标注出处

**写入逻辑** (`Store` / `Delete`)：
- 代理到 `writableCache`
- 外部源为只读，不受写入影响

**列举逻辑** (`ListAll` / `ListByTags` / `ListByPrefix`)：
- 代理到 `writableCache`

### 3.1 内部组件接口

框架默认实现使用以下两个内部接口组装，**第三方实现 `IKnowledgeService` 时无需了解它们**：

#### IKnowledgeBase — 可写缓存契约

```csharp
// Core/IKnowledgeBase.cs
public interface IKnowledgeBase
{
    bool TryLookup(string term, out KnowledgeEntry entry);
    void Store(KnowledgeEntry entry);
    void Delete(string term);
    IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix);
    IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags);
    IReadOnlyList<KnowledgeEntry> ListAll();
}
```

框架提供唯一内置实现 `BuiltInKnowledgeBase`：内存字典 + `ICacheStore` 持久化，直接覆盖，无容量限制。

#### IExternalKnowledgeSource — 只读外部源契约

```csharp
// Core/IExternalKnowledgeSource.cs
public interface IExternalKnowledgeSource
{
    string SourceName { get; }
    IReadOnlyList<KnowledgeEntry> QueryExact(string term);
}
```

实现时注意：返回的 `KnowledgeEntry.Source` 必须与 `SourceName` 一致。

---

## 4. 典型接入方式

### 4.1 使用框架默认实现

```csharp
var cache = new BuiltInKnowledgeBase(cacheStore, logger);

var externals = new List<IExternalKnowledgeSource>
{
    new GameDefKnowledgeSource(gameDefDb),
    new RagKnowledgeSource(vectorStore),
};

var knowledge = new KnowledgeService(cache, externals);

var agent = new AgentLoop(
    pool, llm, credentials, prompt, skills, maxRounds, logger,
    knowledgeService: knowledge   // IKnowledgeService
);
```

### 4.2 第三方自定义实现

```csharp
// 完全自定义：读写同一个外部数据库，不需要框架的 BuiltInKnowledgeBase
public class MyDbKnowledgeService : IKnowledgeService
{
    private readonly IDbConnection _db;

    public IReadOnlyList<KnowledgeEntry> Lookup(string term)
    {
        return _db.Query("SELECT * FROM knowledge WHERE term = @term", new { term })
                  .Select(row => new KnowledgeEntry { ... })
                  .ToList();
    }

    public void Store(KnowledgeEntry entry)
    {
        _db.Execute("INSERT OR REPLACE INTO knowledge ...", entry);
    }

    public void Delete(string term)
    {
        _db.Execute("DELETE FROM knowledge WHERE term = @term", new { term });
    }

    public IReadOnlyList<KnowledgeEntry> ListAll()
    {
        return _db.Query("SELECT * FROM knowledge ORDER BY term").ToList();
    }

    // ... ListByTags, ListByPrefix 同理
}

// 直接注入，框架无感知
var knowledge = new MyDbKnowledgeService(dbConnection);
var agent = new AgentLoop(..., knowledgeService: knowledge);
```

---

## 5. 外部源实现示例

```csharp
// 基于内存字典的 GameDef 源
public class GameDefKnowledgeSource : IExternalKnowledgeSource
{
    private readonly Dictionary<string, string> _defs;

    public string SourceName => "GameDef";

    public IReadOnlyList<KnowledgeEntry> QueryExact(string term)
    {
        if (_defs.TryGetValue(term, out var def))
            return new[] { new KnowledgeEntry
            {
                Term = term, Definition = def,
                Source = "GameDef", Confidence = 1.0f,
                ContextTags = new List<string> { "GameDef" }
            }};
        return Array.Empty<KnowledgeEntry>();
    }
}

// RAG 源：语义检索
public class RagKnowledgeSource : IExternalKnowledgeSource
{
    private readonly IVectorStore _store;

    public string SourceName => "RAG";

    public IReadOnlyList<KnowledgeEntry> QueryExact(string term)
    {
        // "精确"阈值由实现方自行定义
        var results = _store.Search(term, topK: 3, minScore: 0.95f);
        return results.Select(r => new KnowledgeEntry
        {
            Term = term, Definition = r.Content,
            Source = "RAG", Confidence = r.Score,
            ContextTags = r.Tags
        }).ToList();
    }
}
```

---

## 6. 架构关系图

```
           AgentLoop / KnowledgeMcpProvider
                      │
                      ▼
              IKnowledgeService   ◄── 公共接口，第三方可替换
                      │
          ┌───────────┴───────────┐
          │                       │
    默认实现                 第三方实现
   KnowledgeService        MyDbKnowledgeService
          │                 (直接读写 DB, 无需关心
   ┌──────┼──────┐           IKnowledgeBase / IExternalKnowledgeSource)
   │      │      │
  IKB   IExt   IExt
  (写)  (只读)  (只读)
```

- **上排**：框架组件，只依赖 `IKnowledgeService`
- **左下**：框架默认实现，内部用 `IKnowledgeBase` + `IExternalKnowledgeSource` 组装
- **右下**：第三方自行实现，可以完全不用这两个内部接口
