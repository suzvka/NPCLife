using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.PromptBlocks;
using NPCLife.Workspace;
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
    public class DefaultPromptBuilder : IPromptBuilder
    {
        public PromptBuildResult Build(IWorkspace workspace, IReadOnlyList<IGameEvent> events)
        {
            var activeIds = workspace?.SkillSlot?.ActiveSkillIds;
            var blocks = PromptBlockRegistry.GetActiveBlocks(activeIds);

            var sb = new StringBuilder();
            var tools = new List<McpToolDefinition>();
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

                // 工具块 → ToolsJson
                if (block is IToolProviderBlock toolBlock)
                {
                    var blockTools = toolBlock.GetTools();
                    if (blockTools != null && blockTools.Count > 0)
                        tools.AddRange(blockTools);
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

            return new PromptBuildResult
            {
                SystemPrompt = sb.ToString(),
                PrimingMessages = priming,
                PreQueriedMessages = preQueried,
                ToolsJson = SerializeTools(tools)
            };
        }

        private static string SerializeTools(List<McpToolDefinition> tools)
        {
            if (tools.Count == 0) return "[]";

            var sb = new StringBuilder("[\n");
            for (int i = 0; i < tools.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append(McpToolGenerator.Serialize(tools[i]));
            }
            sb.Append("\n]");
            return sb.ToString();
        }
    }
}
