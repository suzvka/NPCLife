using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace NPCLife.Infrastructure
{
    /// <summary>
    /// 知识上下文注入拦截器。在 Agent 激活时扫描事件 Payload 中的 knowledge_tags，
    /// 自动从知识库查询对应词条并注入到 LLM 提示词中。
    ///
    /// 使用方式：
    ///   AgentPipeline.AddInterceptor(
    ///       new KnowledgeContextInterceptor(() => knowledgeService, logger),
    ///       priority: 10);
    ///
    /// 零外部依赖，仅依赖 IKnowledgeService 接口。
    /// </summary>
    public class KnowledgeContextInterceptor : AgentInterceptorBase
    {
        private readonly Func<IKnowledgeService> _getKnowledgeService;
        private readonly ILogger _logger;

        /// <summary>
        /// 创建知识上下文拦截器。
        /// </summary>
        /// <param name="getKnowledgeService">延迟获取 IKnowledgeService 的工厂委托。</param>
        /// <param name="logger">可选日志接口。</param>
        public KnowledgeContextInterceptor(Func<IKnowledgeService> getKnowledgeService, ILogger logger = null)
        {
            _getKnowledgeService = getKnowledgeService ?? throw new ArgumentNullException(nameof(getKnowledgeService));
            _logger = logger;
        }

        // ================================================================
        // IAgentInterceptor
        // ================================================================

        /// <summary>
        /// Prompt 构造后：扫描事件列表中的 knowledge_tags，查询知识库并注入上下文。
        /// </summary>
        public override void OnBeforePrompt(PromptContext ctx)
        {
            if (ctx?.Events == null || ctx.Events.Count == 0) return;

            try
            {
                // 1. 从所有事件的 Payload 中收集知识标签
                var tags = CollectKnowledgeTags(ctx.Events);
                if (tags.Count == 0) return;

                // 2. 获取知识服务
                var svc = _getKnowledgeService();
                if (svc == null) return;

                // 3. 查询每个标签对应的词条
                var entries = LookupTags(tags, svc);
                if (entries.Count == 0) return;

                // 4. 将知识上下文追加到用户消息末尾
                ctx.UserMessage += BuildKnowledgeSection(entries);
            }
            catch (Exception ex)
            {
                _logger?.Warning($"[NPCLife.KnowledgeInterceptor] OnBeforePrompt failed: {ex.Message}");
            }
        }

        // ================================================================
        // 内部逻辑
        // ================================================================

        /// <summary>
        /// 从事件列表中收集知识标签。扫描每个事件的 Payload["knowledge_tags"]，
        /// 逗号分隔，去重，剔除空白。
        /// </summary>
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

        /// <summary>
        /// 批量查询知识标签，返回所有命中的词条列表。
        /// 每个标签可能命中多条（不同来源），全部收集。
        /// </summary>
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
        /// 构建知识上下文 Markdown 块，追加到用户消息末尾。
        /// </summary>
        private static string BuildKnowledgeSection(List<KnowledgeEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("## 关联知识词条");
            sb.AppendLine();
            sb.AppendLine("以下是从知识库中自动查询到的与当前事件关联的世界观词条，可作为叙事参考：");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                sb.Append("- **");
                sb.Append(entry.Term ?? "?");
                sb.Append("**");
                if (!string.IsNullOrEmpty(entry.Source))
                {
                    sb.Append(" (`");
                    sb.Append(entry.Source);
                    sb.Append("`)");
                }
                if (!string.IsNullOrEmpty(entry.Definition))
                {
                    sb.Append(": ");
                    sb.Append(entry.Definition);
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
