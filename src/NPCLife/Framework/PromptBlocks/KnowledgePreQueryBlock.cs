using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// 知识预查询块。扫描事件中的 knowledge_tags，自动查询知识库，
    /// 将查询结果以模拟 tool_call + tool_result 消息的形式注入。
    /// 
    /// 注册为全局块后，所有 Agent 激活时自动生效。
    /// 替代原先 KnowledgeContextInterceptor 的纯文本注入方式——
    /// 模拟消息形式可避免 LLM 重复调用 lookup_term。
    /// </summary>
    public class KnowledgePreQueryBlock : IToolPreQueryBlock
    {
        private readonly Func<IKnowledgeService> _getKnowledgeService;
        private readonly ILogger _logger;

        /// <param name="getKnowledgeService">延迟获取 IKnowledgeService 的工厂委托。</param>
        /// <param name="logger">可选日志接口。</param>
        public KnowledgePreQueryBlock(Func<IKnowledgeService> getKnowledgeService, ILogger logger = null)
        {
            _getKnowledgeService = getKnowledgeService ?? throw new ArgumentNullException(nameof(getKnowledgeService));
            _logger = logger;
        }

        public string Id => "knowledge_pre_query";
        public string Header => null; // 预查询块无标题，不出现在 system prompt 中

        public IReadOnlyList<LlmMessage> GetMessages(IReadOnlyList<IGameEvent> events)
        {
            if (events == null || events.Count == 0)
                return Array.Empty<LlmMessage>();

            try
            {
                var tags = CollectKnowledgeTags(events);
                if (tags.Count == 0) return Array.Empty<LlmMessage>();

                var svc = _getKnowledgeService();
                if (svc == null) return Array.Empty<LlmMessage>();

                var entries = LookupTags(tags, svc);
                if (entries.Count == 0) return Array.Empty<LlmMessage>();

                return BuildToolCallMessages(entries);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[KnowledgePreQueryBlock] Failed: {ex.Message}");
                return Array.Empty<LlmMessage>();
            }
        }

        // ================================================================
        // 内部逻辑
        // ================================================================

        private static HashSet<string> CollectKnowledgeTags(IReadOnlyList<IGameEvent> events)
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                if (evt.Payload == null || evt.Payload.Count == 0) continue;
                if (!evt.Payload.TryGetValue("knowledge_tags", out var raw)) continue;
                if (string.IsNullOrWhiteSpace(raw)) continue;

                foreach (var tag in raw.Split(','))
                {
                    var trimmed = tag.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        tags.Add(trimmed);
                }
            }

            return tags;
        }

        private static List<KnowledgeEntry> LookupTags(HashSet<string> tags, IKnowledgeService svc)
        {
            var allEntries = new List<KnowledgeEntry>();
            foreach (var tag in tags)
            {
                var hits = svc.Lookup(tag);
                if (hits != null && hits.Count > 0)
                    allEntries.AddRange(hits);
            }
            return allEntries;
        }

        /// <summary>
        /// 构建模拟的工具调用消息序列。每个词条对应一组 assistant(tool_calls) + tool(result)。
        /// </summary>
        private static IReadOnlyList<LlmMessage> BuildToolCallMessages(List<KnowledgeEntry> entries)
        {
            var messages = new List<LlmMessage>();

            foreach (var entry in entries)
            {
                var callId = "preq_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                // assistant 消息：含 tool_calls
                messages.Add(new LlmMessage
                {
                    Role = "assistant",
                    Content = "",
                    ToolCalls = new List<LlmToolCall>
                    {
                        new LlmToolCall
                        {
                            Id = callId,
                            Name = "lookup_term",
                            Arguments = "{\"term\":" + JsonHelper.Quote(entry.Term ?? "") + "}"
                        }
                    }
                });

                // tool 结果消息
                var resultJson = BuildLookupResult(entry);
                messages.Add(LlmMessage.ToolResult(callId, resultJson));
            }

            return messages;
        }

        private static string BuildLookupResult(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("hit", true);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definition", entry.Definition ?? "");
            w.Prop("source", entry.Source ?? "");
            return w.Close();
        }
    }
}
