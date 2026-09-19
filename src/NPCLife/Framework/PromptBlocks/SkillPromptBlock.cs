using NPCLife.Core;
using NPCLife.Framework.Mcp;
using System;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// Skill 提示词块。将 ISkillModule 适配为 PromptBlock 体系。
    /// 仅实现 ITextPromptBlock —— 工具收集由 AgentLoop 通过 McpSkillRegistry 统一处理。
    /// </summary>
    public class SkillPromptBlock : ITextPromptBlock
    {
        private readonly ISkillModule _module;

        /// <param name="module">ISkillModule 实例。</param>
        public SkillPromptBlock(ISkillModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public string Id => _module.Id;
        public string Header => _module.Name;

        /// <summary>从模块的 PromptInstruction 获取提示词文本。</summary>
        public string GetContent() => _module.PromptInstruction;
    }
}
