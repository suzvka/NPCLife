using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NPCLife.Infrastructure.Mcp
{
    /// <summary>
    /// 内置知识库的 MCP 工具集。提供词条查询、学习、列举、删除和元数据统计能力。
    /// 通过 KnowledgeService 聚合内部缓存与外部源，一次性返回全部来源的释义。
    /// </summary>
    public class KnowledgeMcpProvider : IMcpHookProvider
    {
        private readonly Func<KnowledgeService> _getKnowledgeService;
        private readonly ILogger _logger;

        public KnowledgeMcpProvider(Func<KnowledgeService> getKnowledgeService, ILogger logger)
        {
            _getKnowledgeService = getKnowledgeService ?? throw new ArgumentNullException(nameof(getKnowledgeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "knowledge_management";
        public string HookName => "知识管理";
        public string HookDescription => "词条查询、学习、列举、删除、统计";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(KnowledgeMcpProvider).GetMethod(nameof(LookupTerm)), this),
                McpTool.FromMethod(typeof(KnowledgeMcpProvider).GetMethod(nameof(LearnTerm)), this),
                McpTool.FromMethod(typeof(KnowledgeMcpProvider).GetMethod(nameof(ListKnownTerms)), this),
                McpTool.FromMethod(typeof(KnowledgeMcpProvider).GetMethod(nameof(ForgetTerm)), this),
                McpTool.FromMethod(typeof(KnowledgeMcpProvider).GetMethod(nameof(GetTermStats)), this),
            };
        }

        // ================================================================
        // 知识库工具
        // ================================================================

        /// <summary>
        /// 查询词条释义。并行查询内部缓存和所有外部源，返回全部命中的释义列表。
        /// </summary>
        [McpTool(Name = "lookup_term",
                 Description = "查询词条释义。并行查询所有知识源，返回全部命中的释义列表，每条标注来源。")]
        public string LookupTerm(
            [McpParam(Description = "要查询的词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var svc = _getKnowledgeService();
                if (svc == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeService unavailable\"}";

                var hits = svc.Lookup(term);

                if (hits.Count > 0)
                    return SerializeLookupHits(term, hits);

                return MakeMiss(term);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.KnowledgeMcp] lookup_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 主动学习一个词条的释义并存储到内部知识库。直接覆盖已有词条。
        /// </summary>
        [McpTool(Name = "learn_term",
                 Description = "将一个词条及其释义存储到内部知识库。若已存在则直接覆盖。")]
        public string LearnTerm(
            [McpParam(Description = "词条名")] string term,
            [McpParam(Description = "释义文本")] string definition,
            [McpParam(Description = "信心度 0.0~1.0，默认 0.8")] float confidence = 0.8f,
            [McpParam(Description = "来源名，如 LLM / AgentDeduction / Wiki，默认 LLM",
                      Required = McpRequired.False)] string source = "LLM",
            [McpParam(Description = "关联语义标签，逗号分隔，如 Combat,Faction,Lore",
                      Required = McpRequired.False)] string tags = null)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var svc = _getKnowledgeService();
                if (svc == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeService unavailable\"}";

                var entry = new KnowledgeEntry
                {
                    Term = term.Trim(),
                    Definition = definition ?? "",
                    Source = source ?? "LLM",
                    Confidence = Math.Max(0f, Math.Min(1f, confidence)),
                    ContextTags = ParseTagList(tags)
                };

                svc.Store(entry);

                // 回读确认
                var hits = svc.Lookup(term);
                if (hits.Count > 0)
                    return SerializeLookupHits(term, hits);

                return "{\"hit\":false,\"error\":\"store failed\"}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.KnowledgeMcp] learn_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 列出已知词条，支持前缀和标签过滤。
        /// </summary>
        [McpTool(Name = "list_known_terms",
                 Description = "列出内部知识库中的已知词条摘要。支持前缀过滤和语义标签过滤。")]
        public string ListKnownTerms(
            [McpParam(Description = "前缀过滤，如 '心灵'。留空=全部",
                      Required = McpRequired.False)] string prefix = null,
            [McpParam(Description = "语义标签过滤，逗号分隔，如 Combat,Lore。留空=不限",
                      Required = McpRequired.False)] string tags = null,
            [McpParam(Description = "最大返回数，默认 30")] int limit = 30)
        {
            try
            {
                var svc = _getKnowledgeService();
                if (svc == null) return "[]";

                IReadOnlyList<KnowledgeEntry> entries;

                if (!string.IsNullOrEmpty(tags))
                {
                    var tagList = ParseTagList(tags);
                    entries = svc.ListByTags(tagList);
                }
                else if (!string.IsNullOrEmpty(prefix))
                {
                    entries = svc.ListByPrefix(prefix);
                }
                else
                {
                    entries = svc.ListAll();
                }

                if (entries.Count > limit)
                    entries = entries.Take(limit).ToList();

                return SerializeTermSummaryList(entries);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.KnowledgeMcp] list_known_terms failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 删除指定词条。
        /// </summary>
        [McpTool(Name = "forget_term",
                 Description = "从内部知识库中删除指定词条。不存在时静默返回。")]
        public string ForgetTerm(
            [McpParam(Description = "要删除的词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var svc = _getKnowledgeService();
                if (svc == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeService unavailable\"}";

                svc.Delete(term);

                var w = new JsonWriter(64);
                w.Prop("hit", true);
                w.Prop("term", term);
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.KnowledgeMcp] forget_term({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 获取词条的元数据统计（信心度、来源等）。
        /// </summary>
        [McpTool(Name = "get_term_stats",
                 Description = "获取指定词条的元数据：信心度、来源。")]
        public string GetTermStats(
            [McpParam(Description = "词条名")] string term)
        {
            try
            {
                if (string.IsNullOrEmpty(term))
                    return "{\"hit\":false,\"error\":\"term is required\"}";

                var svc = _getKnowledgeService();
                if (svc == null)
                    return "{\"hit\":false,\"error\":\"KnowledgeService unavailable\"}";

                var hits = svc.Lookup(term);
                if (hits.Count > 0)
                    return SerializeTermStatsList(hits);

                return MakeMiss(term);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.KnowledgeMcp] get_term_stats({term}) failed: {e.Message}");
                return "{\"hit\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeLookupHits(string term, IReadOnlyList<KnowledgeEntry> entries)
        {
            if (entries == null || entries.Count == 0) return MakeMiss(term);

            if (entries.Count == 1)
            {
                // 单条：保留简化的旧格式兼容性
                var w = new JsonWriter(1024);
                w.Prop("hit", true);
                WProps(w, entries[0]);
                return w.Close();
            }

            // 多条：返回数组
            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var w = new JsonWriter(512);
                WProps(w, entries[i]);
                sb.Append(w.Close());
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void WProps(JsonWriter w, KnowledgeEntry entry)
        {
            w.Prop("term", entry.Term ?? "");
            w.Prop("definition", entry.Definition ?? "");
            w.Prop("source", entry.Source ?? "");
            w.Prop("confidence", entry.Confidence, "F2");
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
        }

        private static string SerializeTermSummaryList(IReadOnlyList<KnowledgeEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeTermSummary(entries[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string SerializeTermSummary(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definitionPreview", Truncate(entry.Definition ?? "", 120));
            w.Prop("source", entry.Source ?? "");
            w.Prop("confidence", entry.Confidence, "F2");
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
            return w.Close();
        }

        private static string SerializeTermStatsList(IReadOnlyList<KnowledgeEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "[]";

            if (entries.Count == 1)
                return SerializeTermStats(entries[0]);

            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeTermStats(entries[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string SerializeTermStats(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("hit", true);
            w.Prop("term", entry.Term ?? "");
            w.Prop("source", entry.Source ?? "");
            w.Prop("confidence", entry.Confidence, "F3");
            if (entry.ContextTags != null && entry.ContextTags.Count > 0)
                w.Array("contextTags", entry.ContextTags);
            return w.Close();
        }

        private static string MakeMiss(string query)
        {
            var w = new JsonWriter(128);
            w.Prop("hit", false);
            w.Prop("query", query ?? "");
            return w.Close();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static List<string> ParseTagList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
        }
    }
}
