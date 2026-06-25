using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 知识库抽象接口。提供词条的增删查改能力。
    /// 框架内部唯一可写实现为 BuiltInKnowledgeBase，外部只读源通过 IExternalKnowledgeSource 接入。
    /// </summary>
    public interface IKnowledgeBase
    {
        /// <summary>
        /// 查询单条知识。返回是否命中。
        /// </summary>
        /// <param name="term">词条名（大小写不敏感）。</param>
        /// <param name="entry">命中时输出 KnowledgeEntry，未命中为 null。</param>
        /// <returns>是否命中。</returns>
        bool TryLookup(string term, out KnowledgeEntry entry);

        /// <summary>
        /// 存储/覆盖知识条目。若 Term 已存在则合并（取最高 Confidence，更新 Definition）。
        /// </summary>
        /// <param name="entry">知识条目。</param>
        void Store(KnowledgeEntry entry);

        /// <summary>
        /// 删除指定词条。不存在时静默返回。
        /// </summary>
        /// <param name="term">词条名。</param>
        void Delete(string term);

        /// <summary>
        /// 按前缀列举词条。用于 Agent 探索已知知识范围。
        /// </summary>
        /// <param name="prefix">前缀字符串，留空表示全部。</param>
        /// <returns>匹配的词条列表。</returns>
        IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix);

        /// <summary>
        /// 按语义标签筛选词条。命中任一标签即匹配。
        /// 用于按领域（Combat / Lore / Social 等）过滤知识库内容。
        /// </summary>
        /// <param name="tags">标签列表。null 或空列表时返回全部。</param>
        /// <returns>匹配的词条列表。</returns>
        IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags);

        /// <summary>
        /// 列出全部词条。
        /// </summary>
        IReadOnlyList<KnowledgeEntry> ListAll();

    }
}
