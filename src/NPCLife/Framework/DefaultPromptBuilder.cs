using NPCLife.Cards;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.PromptBlocks;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using System.Text;

namespace NPCLife.Framework
{
    /// <summary>
    /// 默认提示词构建器。通过 PromptBlockRegistry 聚合所有 PromptBlock，
    /// 产出 LLM 请求所需的 SystemPrompt、注入消息和 ToolsJson。
    /// 
    /// 聚合规则：
    /// - ITextPromptBlock → 按 "## Header" 分段拼接 → SystemPrompt
    /// - IToolProviderBlock → 收集工具定义 → 序列化为 ToolsJson
    /// - IDialoguePrimingBlock → 收集注入消息 → PrimingMessages
    /// - IToolPreQueryBlock → 收集注入消息 → PreQueriedMessages
    /// </summary>
    public class DefaultPromptBuilder
    {
        private readonly ILogger _logger;

        /// <param name="logger">可选日志接口，用于诊断输出。</param>
        public DefaultPromptBuilder(ILogger logger = null)
        {
            _logger = logger;
            // 让 PromptBlockRegistry 也能输出诊断日志（通过同一个 logger）
            if (logger != null) PromptBlockRegistry.Logger = logger;
        }

        public PromptBuildResult Build(IWorkspace workspace, IReadOnlyList<IGameEvent> events)
        {
            // 构建有效技能 ID：system 技能始终隐式可用（不显示在 SkillSlot 中）
            var activeIds = workspace?.SkillSlot?.ActiveSkillIds;
            var effectiveIds = new List<string> { McpSkillRegistry.SystemSkillId };
            if (activeIds != null) effectiveIds.AddRange(activeIds);

            _logger?.Message($"[DefaultPromptBuilder.DIAG] Querying PromptBlockRegistry: effectiveIds=[{string.Join(",", effectiveIds)}]");
            var blocks = PromptBlockRegistry.GetActiveBlocks(effectiveIds);
            _logger?.Message($"[DefaultPromptBuilder.DIAG] GetActiveBlocks returned {blocks.Count} blocks");

            var sb = new StringBuilder();
            var priming = new List<LlmMessage>();
            var preQueried = new List<LlmMessage>();

            foreach (var block in blocks)
            {
                // 文本块 → SystemPrompt
                if (block is ITextPromptBlock textBlock)
                {
                    var content = textBlock.GetContent();
                    if (!string.IsNullOrEmpty(content))
                    {
                        if (!string.IsNullOrEmpty(textBlock.Header))
                        {
                            sb.Append("## ");
                            sb.AppendLine(textBlock.Header);
                        }
                        sb.AppendLine(content);
                        sb.AppendLine();
                    }
                }

                // 对话注入块 → PrimingMessages
                if (block is IDialoguePrimingBlock primeBlock)
                {
                    var msgs = primeBlock.GetMessages(events);
                    if (msgs != null && msgs.Count > 0)
                        priming.AddRange(msgs);
                }

                // 预查询工具块 → PreQueriedMessages
                if (block is IToolPreQueryBlock preBlock)
                {
                    var msgs = preBlock.GetMessages(events);
                    if (msgs != null && msgs.Count > 0)
                        preQueried.AddRange(msgs);
                }
            }

            var result = new PromptBuildResult
            {
                SystemPrompt = sb.ToString(),
                PrimingMessages = priming,
                PreQueriedMessages = preQueried
            };

            _logger?.Message($"[DefaultPromptBuilder.DIAG] Build complete: blockCount={blocks.Count}, systemPromptLen={result.SystemPrompt.Length}");
            return result;
        }
    }
}
