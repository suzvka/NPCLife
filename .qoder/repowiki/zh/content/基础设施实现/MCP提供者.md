# MCP提供者

<cite>
**本文引用的文件**
- [KnowledgeMcpProvider.cs](file://src/NPCLife/Infrastructure/Mcp/KnowledgeMcpProvider.cs)
- [SystemMcpProvider.cs](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs)
- [IMcpHookProvider.cs](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs)
- [McpTool.cs](file://src/NPCLife/Framework/Mcp/McpTool.cs)
- [McpToolGenerator.cs](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs)
- [McpToolDefinition.cs](file://src/NPCLife/Framework/Mcp/McpToolDefinition.cs)
- [McpToolInvoker.cs](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs)
- [McpSkillRegistry.cs](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs)
- [McpParamAttribute.cs](file://src/NPCLife/Framework/Mcp/McpParamAttribute.cs)
- [McpToolAttribute.cs](file://src/NPCLife/Framework/Mcp/McpToolAttribute.cs)
- [McpTypeMapper.cs](file://src/NPCLife/Framework/Mcp/McpTypeMapper.cs)
- [WritingMcpTools.cs](file://src/NPCLife/Workspace/WritingMcpTools.cs)
- [FreelancerMcpTools.cs](file://src/NPCLife/Workspace/FreelancerMcpTools.cs)
- [IWorkspaceManager.cs](file://src/NPCLife/Core/IWorkspaceManager.cs)
- [LifecycleManager.cs](file://src/NPCLife/Framework/LifecycleManager.cs)
- [McpSkillRegistryTests.cs](file://tests/NPCLife.Tests/Framework/McpSkillRegistryTests.cs)
</cite>

## 目录
1. [简介](#简介)
2. [项目结构](#项目结构)
3. [核心组件](#核心组件)
4. [架构总览](#架构总览)
5. [详细组件分析](#详细组件分析)
6. [依赖关系分析](#依赖关系分析)
7. [性能考量](#性能考量)
8. [故障排查指南](#故障排查指南)
9. [结论](#结论)
10. [附录](#附录)

## 简介
本文件面向MCP（Model Context Protocol）提供者，系统性阐述知识与系统两类MCP提供者的实现差异、接口设计与扩展机制，以及工具注册、发现与调用的完整流程。文档还解释了MCP提供者与核心框架的集成方式、生命周期管理，并给出自定义MCP提供者的开发指南与最佳实践，最后提供协议实现细节与性能优化建议。

## 项目结构
MCP相关代码主要分布在以下模块：
- Framework/Mcp：MCP协议基础设施（工具定义、调用器、注册表、特性与类型映射）
- Infrastructure/Mcp：内置提供者（知识库与系统元工具）
- Workspace：工作空间相关提供者（编剧与临时任务代理）
- Core：工作空间管理器接口
- Framework：生命周期管理器

```mermaid
graph TB
subgraph "框架层"
A["IMcpHookProvider 接口"]
B["McpTool 工具载体"]
C["McpToolGenerator 工具定义生成器"]
D["McpToolInvoker 工具调用器"]
E["McpSkillRegistry 技能注册表"]
F["McpTypeMapper 类型映射"]
G["McpToolDefinition/Schema"]
end
subgraph "基础设施层"
H["KnowledgeMcpProvider 知识提供者"]
I["SystemMcpProvider 系统提供者"]
end
subgraph "工作空间层"
J["WritingMcpTools 编剧提供者"]
K["FreelancerMcpTools 临时任务代理提供者"]
end
subgraph "核心接口"
L["IWorkspaceManager 工作空间管理器"]
end
A --> H
A --> I
A --> J
A --> K
H --> E
I --> E
J --> E
K --> E
E --> C
E --> D
C --> G
D --> F
J --> L
I --> L
```

图表来源
- [IMcpHookProvider.cs:1-38](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L1-L38)
- [McpTool.cs:1-40](file://src/NPCLife/Framework/Mcp/McpTool.cs#L1-L40)
- [McpToolGenerator.cs:1-214](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs#L1-L214)
- [McpToolInvoker.cs:1-238](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L1-L238)
- [McpSkillRegistry.cs:1-470](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L1-L470)
- [McpTypeMapper.cs:1-85](file://src/NPCLife/Framework/Mcp/McpTypeMapper.cs#L1-L85)
- [KnowledgeMcpProvider.cs:1-355](file://src/NPCLife/Infrastructure/Mcp/KnowledgeMcpProvider.cs#L1-L355)
- [SystemMcpProvider.cs:1-149](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs#L1-L149)
- [WritingMcpTools.cs:1-313](file://src/NPCLife/Workspace/WritingMcpTools.cs#L1-L313)
- [FreelancerMcpTools.cs:1-274](file://src/NPCLife/Workspace/FreelancerMcpTools.cs#L1-L274)
- [IWorkspaceManager.cs:1-58](file://src/NPCLife/Core/IWorkspaceManager.cs#L1-L58)

章节来源
- [IMcpHookProvider.cs:1-38](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L1-L38)
- [McpSkillRegistry.cs:1-470](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L1-L470)

## 核心组件
- IMcpHookProvider：MCP提供者接口，定义HookId/HookName/HookDescription与GetTools方法，每个提供者对应一个Skill。
- McpTool：统一的工具载体，包含Definition（工具定义）与Invoker（调用委托）。
- McpToolGenerator：基于反射生成工具定义（含参数Schema），并序列化为标准JSON。
- McpToolInvoker：将JSON参数反序列化、反射调用目标方法、序列化返回值。
- McpSkillRegistry：技能注册表，负责技能元数据、工具注册、激活工具查询、工具调用与事件发布。
- McpTypeMapper：C#类型到JSON Schema类型的映射。
- McpToolDefinition/McpInputSchema/McpParamSchema：工具定义的数据结构。
- 提供者实现：KnowledgeMcpProvider（知识库）、SystemMcpProvider（系统元工具）、WritingMcpTools/FreelancerMcpTools（工作空间）。

章节来源
- [McpTool.cs:1-40](file://src/NPCLife/Framework/Mcp/McpTool.cs#L1-L40)
- [McpToolGenerator.cs:1-214](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs#L1-L214)
- [McpToolInvoker.cs:1-238](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L1-L238)
- [McpSkillRegistry.cs:1-470](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L1-L470)
- [McpTypeMapper.cs:1-85](file://src/NPCLife/Framework/Mcp/McpTypeMapper.cs#L1-L85)
- [McpToolDefinition.cs:1-50](file://src/NPCLife/Framework/Mcp/McpToolDefinition.cs#L1-L50)
- [IMcpHookProvider.cs:1-38](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L1-L38)

## 架构总览
MCP提供者通过IMcpHookProvider注入依赖，向McpSkillRegistry注册工具。激活的技能集合决定哪些工具对LLM可见；调用时由McpSkillRegistry在激活技能范围内查找工具并委派给McpToolInvoker执行。

```mermaid
sequenceDiagram
participant Host as "宿主/适配器"
participant Provider as "IMcpHookProvider 提供者"
participant Registry as "McpSkillRegistry"
participant Tool as "McpTool"
participant Invoker as "McpToolInvoker"
Host->>Provider : "构造提供者注入依赖"
Host->>Registry : "RegisterFromProvider(provider)"
Provider-->>Registry : "GetTools() 返回工具列表"
Registry-->>Host : "注册完成技能元数据+工具映射"
Host->>Registry : "GetActiveToolsJson(activeSkillIds)"
Registry-->>Host : "返回tools JSON含system技能"
Host->>Registry : "InvokeTool(activeSkillIds, toolName, jsonArgs)"
Registry->>Tool : "查找匹配工具"
Tool->>Invoker : "Invoker(jsonArgs)"
Invoker-->>Tool : "返回JSON结果"
Tool-->>Registry : "返回JSON结果"
Registry-->>Host : "返回工具调用结果"
```

图表来源
- [IMcpHookProvider.cs:14-21](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L14-L21)
- [McpSkillRegistry.cs:154-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L154-L175)
- [McpSkillRegistry.cs:249-287](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L249-L287)
- [McpSkillRegistry.cs:361-437](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L361-L437)
- [McpToolInvoker.cs:24-72](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L24-L72)

## 详细组件分析

### 知识MCP提供者（KnowledgeMcpProvider）
- 职责：封装IKnowledgeService，提供词条查询、学习、列举、删除、统计等工具。
- 工具清单：
  - lookup_term：并行查询内部与外部知识源，返回命中列表（含来源标注）
  - learn_term：主动学习词条并覆盖存储，回读确认后返回命中
  - list_known_terms：支持前缀/标签过滤与限制数量
  - forget_term：删除词条（不存在时静默）
  - get_term_stats：返回词条元数据（信心度、来源、标签）
- 错误处理：捕获异常并记录日志，返回标准化错误JSON
- 输出格式：单命中返回扁平JSON，多命中返回数组；统计与摘要采用不同字段集

```mermaid
classDiagram
class IMcpHookProvider {
+string HookId
+string HookName
+string HookDescription
+GetTools() IReadOnlyList~McpTool~
}
class KnowledgeMcpProvider {
-Func~IKnowledgeService~ _getKnowledgeService
-ILogger _logger
+HookId
+HookName
+HookDescription
+GetTools() IReadOnlyList~McpTool~
+LookupTerm(term) string
+LearnTerm(term, definition, confidence, source, tags) string
+ListKnownTerms(prefix, tags, limit) string
+ForgetTerm(term) string
+GetTermStats(term) string
}
class McpTool {
+Definition : McpToolDefinition
+Invoker : Func~string,string~
+FromMethod(method, target) McpTool
}
IMcpHookProvider <|.. KnowledgeMcpProvider
KnowledgeMcpProvider --> McpTool : "创建工具"
```

图表来源
- [IMcpHookProvider.cs:23-36](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L23-L36)
- [KnowledgeMcpProvider.cs:15-40](file://src/NPCLife/Infrastructure/Mcp/KnowledgeMcpProvider.cs#L15-L40)
- [McpTool.cs:14-38](file://src/NPCLife/Framework/Mcp/McpTool.cs#L14-L38)

章节来源
- [KnowledgeMcpProvider.cs:15-355](file://src/NPCLife/Infrastructure/Mcp/KnowledgeMcpProvider.cs#L15-L355)

### 系统MCP提供者（SystemMcpProvider）
- 职责：系统元工具集，提供技能列表查询、激活/反激活、当前时间获取、中止。
- 工具清单：
  - list_skills：列出指定工作空间的技能分组及激活状态（system隐式可用）
  - activate_skill：激活技能（可叠加）
  - deactivate_skill：反激活技能（system不可反激活）
  - get_current_time：获取格式化时间字符串
  - abort：紧急中止当前对话
- 依赖注入：IWorkspaceManager、时间提供器、ILogger
- 错误处理：捕获异常并返回标准化错误JSON

```mermaid
classDiagram
class SystemMcpProvider {
-Func~IWorkspaceManager~ _getWorkspaceManager
-Func~string~ _getTime
-ILogger _logger
+HookId
+HookName
+HookDescription
+GetTools() IReadOnlyList~McpTool~
+ListSkills(workspaceId) string
+ActivateSkill(workspaceId, skillId) string
+DeactivateSkill(workspaceId, skillId) string
+GetCurrentTime() string
}
class IWorkspaceManager {
+Get(id) IWorkspace
+RouteEvents(workspaceId, events) bool
+Branch(parentId, newLabel, branchRecap, callerRole) IWorkspace
+Merge(sourceId, targetId, mergeRecap, callerRole) bool
+UpdateStatus(id, newStatus, outcome) bool
+Persist()
}
SystemMcpProvider --> IWorkspaceManager : "依赖"
```

图表来源
- [SystemMcpProvider.cs:15-147](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs#L15-L147)
- [IWorkspaceManager.cs:14-57](file://src/NPCLife/Core/IWorkspaceManager.cs#L14-L57)

章节来源
- [SystemMcpProvider.cs:15-149](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs#L15-L149)

### 工作空间MCP提供者（编剧与临时任务代理）
- 编剧提供者（WritingMcpProvider）：提供逐句台词推送、结束本轮并归档、事件路由（可附加关键词）。工作空间上下文由 prompt 自动注入。
- 临时任务代理（FreelancerMcpProvider）：提供逐句台词推送、结束本轮（无剧情结果）、事件路由。工作空间上下文由 prompt 自动注入。
- 两者均通过IMcpHookProvider注入IWorkspaceManager与ILogger，零静态耦合。

章节来源
- [WritingMcpTools.cs:16-313](file://src/NPCLife/Workspace/WritingMcpTools.cs#L16-L313)
- [FreelancerMcpTools.cs:21-274](file://src/NPCLife/Workspace/FreelancerMcpTools.cs#L21-L274)

### 接口设计与扩展机制
- IMcpHookProvider：提供者契约，统一HookId/HookName/HookDescription与GetTools。
- 特性驱动：
  - [McpTool]：标记方法为工具，可覆盖名称与描述
  - [McpParam]：覆盖参数名、描述与必填状态（Auto/True/False）
- 工具注册：
  - 从类型扫描：RegisterFromType(Type)
  - 从提供者注册：RegisterFromProvider(IMcpHookProvider)
  - 手工注册：RegisterTool(skillId, McpTool)

```mermaid
flowchart TD
Start(["开始"]) --> Attr["读取 [McpTool]/[McpParam] 特性"]
Attr --> GenDef["生成 McpToolDefinition"]
GenDef --> BuildInvoker["绑定 McpToolInvoker"]
BuildInvoker --> Reg["注册到技能映射"]
Reg --> End(["结束"])
```

图表来源
- [McpToolGenerator.cs:19-78](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs#L19-L78)
- [McpTool.cs:28-37](file://src/NPCLife/Framework/Mcp/McpTool.cs#L28-L37)
- [McpSkillRegistry.cs:154-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L154-L175)

章节来源
- [IMcpHookProvider.cs:14-36](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L14-L36)
- [McpToolAttribute.cs:1-18](file://src/NPCLife/Framework/Mcp/McpToolAttribute.cs#L1-L18)
- [McpParamAttribute.cs:1-34](file://src/NPCLife/Framework/Mcp/McpParamAttribute.cs#L1-L34)
- [McpSkillRegistry.cs:124-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L124-L175)

### 工具注册、发现与调用流程
- 注册阶段
  - 初始化技能元数据：InitializeDefaults()
  - 从类型扫描注册：RegisterFromType(Type)
  - 从提供者注册：RegisterFromProvider(IMcpHookProvider)
- 发现阶段
  - 获取技能列表：GetSkillListJson(activeSkillIds)
  - 获取激活工具定义：GetActiveToolsJson(activeSkillIds)
- 调用阶段
  - InvokeTool(activeSkillIds, toolName, jsonArgs)
  - 内部搜索顺序：业务技能 → system技能（回退）

```mermaid
sequenceDiagram
participant Dev as "开发者"
participant Reg as "McpSkillRegistry"
participant Prov as "IMcpHookProvider"
participant LLM as "LLM/客户端"
Dev->>Reg : "InitializeDefaults()"
Dev->>Reg : "RegisterFromType(Type)"
Dev->>Reg : "RegisterFromProvider(Prov)"
LLM->>Reg : "GetActiveToolsJson(activeSkillIds)"
Reg-->>LLM : "tools JSON"
LLM->>Reg : "InvokeTool(activeSkillIds, toolName, jsonArgs)"
Reg-->>LLM : "工具返回JSON"
```

图表来源
- [McpSkillRegistry.cs:52-76](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L52-L76)
- [McpSkillRegistry.cs:124-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L124-L175)
- [McpSkillRegistry.cs:249-287](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L249-L287)
- [McpSkillRegistry.cs:361-437](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L361-L437)

章节来源
- [McpSkillRegistry.cs:52-437](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L52-L437)

### 与核心框架的集成与生命周期管理
- 集成点
  - 提供者通过构造函数注入IWorkspaceManager、ILogger等依赖
  - 通过McpSkillRegistry.RegisterFromProvider完成注册
  - 工具调用经由McpSkillRegistry.InvokeTool进行
- 生命周期
  - LifecycleManager负责组件注册、初始化、配置就绪、销毁与重置
  - 提供者作为组件可被LifecycleManager统一管理

章节来源
- [SystemMcpProvider.cs:21-26](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs#L21-L26)
- [WritingMcpTools.cs:21-25](file://src/NPCLife/Workspace/WritingMcpTools.cs#L21-L25)
- [FreelancerMcpTools.cs:26-30](file://src/NPCLife/Workspace/FreelancerMcpTools.cs#L26-L30)
- [LifecycleManager.cs:159-240](file://src/NPCLife/Framework/LifecycleManager.cs#L159-L240)

## 依赖关系分析
- 耦合与内聚
  - 提供者与核心服务通过接口注入，低耦合高内聚
  - McpSkillRegistry集中管理技能与工具映射，避免分散注册
- 外部依赖
  - 依赖JsonHelper/JsonWriter进行JSON序列化
  - 依赖反射与特性进行工具定义生成
- 潜在循环依赖
  - 提供者不直接依赖注册表，注册表也不依赖提供者，无循环

```mermaid
graph LR
Prov["IMcpHookProvider 实现"] --> Reg["McpSkillRegistry"]
Reg --> Gen["McpToolGenerator"]
Reg --> Inv["McpToolInvoker"]
Gen --> Def["McpToolDefinition"]
Inv --> Map["McpTypeMapper"]
Prov --> Core["核心接口如 IWorkspaceManager"]
```

图表来源
- [McpSkillRegistry.cs:154-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L154-L175)
- [McpToolGenerator.cs:19-78](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs#L19-L78)
- [McpToolInvoker.cs:24-72](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L24-L72)
- [McpTypeMapper.cs:16-43](file://src/NPCLife/Framework/Mcp/McpTypeMapper.cs#L16-L43)

章节来源
- [McpSkillRegistry.cs:154-437](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L154-L437)

## 性能考量
- 反射与序列化
  - 工具定义生成与调用均为纯静态方法，避免实例化开销
  - 使用StringBuilder与预分配容量减少GC压力
- 参数解析与类型转换
  - 严格类型映射与宽松布尔解析，降低异常开销
  - 数组/集合转换采用批量解析，避免逐项装箱
- 工具调用
  - 激活工具集合由调用方提供，减少遍历范围
  - system技能工具始终可用，减少遗漏查找

章节来源
- [McpToolInvoker.cs:87-132](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L87-L132)
- [McpToolInvoker.cs:159-171](file://src/NPCLife/Framework/Mcp/McpToolInvoker.cs#L159-L171)
- [McpToolGenerator.cs:172-211](file://src/NPCLife/Framework/Mcp/McpToolGenerator.cs#L172-L211)

## 故障排查指南
- 常见问题
  - 工具未出现在tools列表：检查是否正确注册（RegisterFromType/RegisterFromProvider）
  - 工具调用返回错误：确认激活技能集合是否包含目标工具
  - 参数类型不匹配：检查[McpParam]必填与类型映射
- 日志与错误
  - 提供者内部捕获异常并记录警告
  - 注册表在工具调用失败时发布事件并返回标准化错误JSON
- 单元测试参考
  - McpSkillRegistryTests覆盖初始化、注册、查询与调用路径

章节来源
- [KnowledgeMcpProvider.cs:70-75](file://src/NPCLife/Infrastructure/Mcp/KnowledgeMcpProvider.cs#L70-L75)
- [SystemMcpProvider.cs:63-67](file://src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs#L63-L67)
- [McpSkillRegistry.cs:367-383](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L367-L383)
- [McpSkillRegistryTests.cs:102-244](file://tests/NPCLife.Tests/Framework/McpSkillRegistryTests.cs#L102-L244)

## 结论
MCP提供者体系以IMcpHookProvider为核心契约，结合McpSkillRegistry实现技能与工具的集中管理，配合McpToolGenerator与McpToolInvoker完成从方法签名到工具定义与调用的完整闭环。知识与系统提供者分别覆盖知识检索与系统元能力，工作空间提供者支撑编剧与临时任务代理的叙事流程。通过特性驱动与接口注入，系统具备良好的扩展性与低耦合性。

## 附录

### 自定义MCP提供者开发指南与最佳实践
- 设计步骤
  - 实现IMcpHookProvider，定义HookId/HookName/HookDescription与GetTools
  - 使用[McpTool]/[McpParam]标注工具方法与参数，明确名称、描述与必填
  - 通过构造函数注入所需依赖（如IWorkspaceManager、ILogger）
  - 在合适时机调用McpSkillRegistry.RegisterFromProvider注册
- 最佳实践
  - 工具方法保持幂等与无副作用
  - 参数尽量使用强类型，必要时提供默认值
  - 对异常进行捕获并返回标准化JSON
  - 使用system技能承载跨域元工具，业务工具使用独立技能
  - 通过LifecycleManager注册组件，确保生命周期一致

章节来源
- [IMcpHookProvider.cs:14-36](file://src/NPCLife/Framework/Mcp/IMcpHookProvider.cs#L14-L36)
- [McpToolAttribute.cs:1-18](file://src/NPCLife/Framework/Mcp/McpToolAttribute.cs#L1-L18)
- [McpParamAttribute.cs:1-34](file://src/NPCLife/Framework/Mcp/McpParamAttribute.cs#L1-L34)
- [McpSkillRegistry.cs:154-175](file://src/NPCLife/Framework/Mcp/McpSkillRegistry.cs#L154-L175)
- [LifecycleManager.cs:77-97](file://src/NPCLife/Framework/LifecycleManager.cs#L77-L97)