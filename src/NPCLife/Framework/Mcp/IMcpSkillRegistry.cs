using NPCLife.Core;
using System;
using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// MCP 技能注册表接口。将静态 McpSkillRegistry 抽象为可注入实例。
    /// </summary>
    public interface IMcpSkillRegistry
    {
        /// <summary>注册全部业务技能的元数据（从 SkillCatalog 派生）。调用一次即可。</summary>
        void InitializeDefaults();

        /// <summary>从 Hook 提供者注册工具。</summary>
        int RegisterFromProvider(IMcpHookProvider provider);

        /// <summary>当前已注册的技能数。</summary>
        int SkillCount { get; }

        /// <summary>当前已注册的工具总数。</summary>
        int TotalToolCount { get; }

        /// <summary>获取轻量技能列表 JSON。</summary>
        string GetSkillListJson(IEnumerable<string> activeSkillIds);

        /// <summary>获取激活技能的工具定义 JSON。</summary>
        string GetActiveToolsJson(IEnumerable<string> activeSkillIds);

        /// <summary>获取所有已注册的技能 ID 列表。</summary>
        IReadOnlyList<string> GetAllSkillIds();

        /// <summary>获取指定技能的工具列表 JSON。</summary>
        string GetSkillToolsJson(string skillId);

        /// <summary>调用指定工具。</summary>
        string InvokeTool(IEnumerable<string> activeSkillIds, string toolName, string jsonArgs);
    }
}
