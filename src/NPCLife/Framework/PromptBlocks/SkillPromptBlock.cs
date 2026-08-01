using NPCLife.Core;
using NPCLife.Framework.Mcp;
using System;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// Skill 提示词块。将 ISkillModule（或旧 IMcpHookProvider）适配为 PromptBlock 体系。
    /// 仅实现 ITextPromptBlock —— 工具收集由 AgentLoop 通过 McpSkillRegistry 统一处理。
    /// </summary>
    public class SkillPromptBlock : ITextPromptBlock
    {
        private readonly ISkillModule _module;
        private readonly IMcpHookProvider _provider;

        /// <param name="module">ISkillModule 实例（推荐）。</param>
        public SkillPromptBlock(ISkillModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        /// <param name="provider">旧 IMcpHookProvider 实例（向后兼容）。</param>
        [Obsolete("Use SkillPromptBlock(ISkillModule) instead.")]
        public SkillPromptBlock(IMcpHookProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public string Id => _module?.Id ?? _provider?.HookId;
        public string Header => _module?.Name ?? _provider?.HookName;

        /// <summary>从模块的 PromptInstruction 获取提示词文本。</summary>
        public string GetContent() => _module?.PromptInstruction ?? _provider?.PromptInstruction;
    }
}
