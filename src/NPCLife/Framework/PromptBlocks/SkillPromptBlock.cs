using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// Skill 提示词块。将 IMcpHookProvider 适配为 PromptBlock 体系。
    /// 同时实现 ITextPromptBlock 和 IToolProviderBlock（可选 IToolPreQueryBlock）。
    /// 
    /// - PromptInstruction → ITextPromptBlock.GetContent()
    /// - GetTools() → IToolProviderBlock.GetTools()（取 Definition 部分）
    /// - 暂无 IToolPreQueryBlock（未来 Skill 可扩展）
    /// </summary>
    public class SkillPromptBlock : ITextPromptBlock, IToolProviderBlock
    {
        private readonly IMcpHookProvider _provider;
        private IReadOnlyList<McpToolDefinition> _cachedTools;

        /// <param name="provider">底层 MCP Hook 提供者。</param>
        public SkillPromptBlock(IMcpHookProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public string Id => _provider.HookId;
        public string Header => _provider.HookName;

        /// <summary>从 Provider 的 PromptInstruction 获取提示词文本。</summary>
        public string GetContent() => _provider.PromptInstruction;

        /// <summary>从 Provider 的 GetTools() 提取 McpToolDefinition 列表。</summary>
        public IReadOnlyList<McpToolDefinition> GetTools()
        {
            if (_cachedTools != null) return _cachedTools;

            var tools = _provider.GetTools();
            if (tools == null || tools.Count == 0)
            {
                _cachedTools = Array.Empty<McpToolDefinition>();
            }
            else
            {
                _cachedTools = tools.Select(t => t.Definition).ToList();
            }
            return _cachedTools;
        }
    }
}
