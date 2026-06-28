using NPCLife.Core;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Infrastructure
{
    /// <summary>委托到静态 McpSkillRegistry 的 IMcpSkillRegistry 实现。</summary>
    internal class StaticMcpSkillRegistryAdapter : IMcpSkillRegistry
    {
        public void InitializeDefaults() => McpSkillRegistry.InitializeDefaults();

        public int RegisterFromProvider(IMcpHookProvider provider)
            => McpSkillRegistry.RegisterFromProvider(provider);

        public int SkillCount => McpSkillRegistry.SkillCount;
        public int TotalToolCount => McpSkillRegistry.TotalToolCount;

        public string GetSkillListJson(IEnumerable<string> activeSkillIds)
            => McpSkillRegistry.GetSkillListJson(activeSkillIds);

        public string GetActiveToolsJson(IEnumerable<string> activeSkillIds)
            => McpSkillRegistry.GetActiveToolsJson(activeSkillIds);

        public IReadOnlyList<string> GetAllSkillIds()
            => McpSkillRegistry.GetAllSkillIds();

        public string GetSkillToolsJson(string skillId)
            => McpSkillRegistry.GetSkillToolsJson(skillId);

        public string InvokeTool(IEnumerable<string> activeSkillIds, string toolName, string jsonArgs)
            => McpSkillRegistry.InvokeTool(activeSkillIds, toolName, jsonArgs);
    }
}
