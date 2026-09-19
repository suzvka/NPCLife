using NPCLife.Cards;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// Skill 模块 —— Agent 能力扩展的唯一入口。
    /// 
    /// 每一个 Skill 模块定义一组文本（注入 system prompt）和工具（注入工具列表）。
    /// 框架内部 system、direction、writing 等也通过此接口构建。
    /// 
    /// 生命周期：在 McpSkillRegistry.InitializeDefaults() 之前通过 RegisterModule() 注册。
    /// 运行时每次 Agent 激活时，框架从所有激活 skill 中：
    ///   1. 拼接 PromptInstruction（静态） → SystemPrompt
    ///   2. 调用 GetDynamicContext()（动态）  → 追加到 SystemPrompt
    ///   3. 收集 GetTools()                 → ToolsJson（system skill 工具优先，去重）
    /// 
    /// 与 [SkillDefinition] 属性的关系：
    ///   [SkillDefinition] → 元数据层（供 UI 展示技能列表、InitializeDefaults() 填充）
    ///   ISkillModule       → 运行时层（RegisterModule() 时提供文本、工具、动态上下文）
    ///   内置模块需同时实现两者，但 SkillCatalog 不实例化 ISkillModule。
    /// </summary>
    public interface ISkillModule
    {
        /// <summary>模块唯一 ID（如 "system"、"storyline_direction"、"colony_overview"）。</summary>
        string Id { get; }

        /// <summary>模块显示名（如 "系统"、"剧情分支管理"）。</summary>
        string Name { get; }

        /// <summary>模块功能描述。</summary>
        string Description { get; }

        /// <summary>
        /// 默认授权给哪些 Agent 角色。新 workspace 创建时自动激活。
        /// 返回空数组表示不自动激活。
        /// </summary>
        WorkspaceRole[] DefaultRoles { get; }

        /// <summary>
        /// 注入到 system prompt 的静态使用说明。
        /// 当该 Skill 被激活时追加到 prompt 末尾。返回 null 表示无额外说明。
        /// </summary>
        string PromptInstruction { get; }

        /// <summary>
        /// 动态上下文。每次 Agent 激活时调用，返回值追加到 system prompt 末尾。
        /// 返回 null 或空字符串表示无动态内容。
        /// 替代原 IPromptBuilder 的动态内容生成职责。
        /// </summary>
        string GetDynamicContext(IWorkspace workspace, IReadOnlyList<IGameEvent> events);

        /// <summary>
        /// 该模块提供的 MCP 工具列表。返回空数组表示无工具。
        /// McpTool.SourceSkillId 由框架在 RegisterModule() 时自动设置为 this.Id。
        /// </summary>
        IReadOnlyList<McpTool> GetTools();
    }
}
