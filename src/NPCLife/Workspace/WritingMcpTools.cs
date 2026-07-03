using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 写作工具集的 MCP 提供者。通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + ILogger）。
    /// 供编剧和即兴编剧共用。所有"读"操作（事件列表、剧情线上下文）由 prompt 自动注入，
    /// 此 Provider 仅提供写操作工具（push_line / finish_round）。
    /// </summary>
    public class WritingMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        public WritingMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "storyline_writing";
        public string HookName => "写作工具集";
        public string HookDescription => "用于创作具体台词脚本的工具";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushLine)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(RouteEvents)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(FinishRound)), this),
            };
        }
        // ================================================================
        // 逐句台词推送
        // ================================================================

        /// <summary>
        /// 推送单句台词到当前剧情线。每句立即投递到游戏侧显示。可并行调用多句。
        /// </summary>
        [McpTool(Name = "push_line",
                 Description = "[dynamic] 写一句台词。其中对话内容建议遇到结束标点符号就断句，以最大化得分。注意：不符合语法或通常文学表述方式的台词将被判定为失败。")]
        public string PushLine(
            [McpParam(Description = "本句台词主体角色(如说话人)的ID")]
            string speakerId,
            [McpParam(Description = "正文。pause 类型时可为空。")]
            string text,
            [McpParam(Description = "本行起始延迟秒数，默认 0。")]
            double delay = 0,
            [McpParam(Description = "类型：[+3,单句过长时-1]dialogue(角色对话) / [+1]narration(旁白/环境描写) / [+1]action(动作描写) / [+1]pause(纯停顿)，默认 dialogue。")]
            string type = "dialogue")
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(workspaceId)) return "{}";

                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                bool ok = ws.PushLine(speakerId, text, (float)delay, type,
                                      ws.CreatedByRole);
                return ok ? "{\"ok\":true}" : "{}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] push_line failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 结束本轮
        // ================================================================

        /// <summary>
        /// 将事件从当前编剧工作空间发送给导演。无需指定目标，自动路由到导演工作空间。
        /// 编剧完成所有台词推送后，可调用此工具将关联事件反馈给导演。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "[+5] 将事件发送给导演")]
        public string RouteEvents(
            [McpParam(Description = "要发送的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "附带给导演的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "聚焦角色 ID，逗号分隔，用于指定该批事件应聚焦的角色",
                      Required = McpRequired.False)] string focusCharacterIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔，用于标记专有名词，避免接收方产生误解",
                      Required = McpRequired.False)] string knowledgeTags = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null)
                    return "{\"success\":false,\"error\":\"WorkspaceManager unavailable\"}";

                var ids = ParseStringList(eventIds);
                if (ids.Count == 0)
                    return "{\"success\":false,\"error\":\"no eventIds provided\"}";

                // 找到导演工作空间
                var actives = manager.GetActive();
                IWorkspace directorWs = null;
                foreach (var ws in actives)
                {
                    if (ws.CreatedByRole == WorkspaceRole.Director)
                    {
                        directorWs = ws;
                        break;
                    }
                }
                if (directorWs == null)
                    return "{\"success\":false,\"error\":\"director workspace not found\"}";

                var sourceWorkspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(sourceWorkspaceId))
                    return "{\"success\":false,\"error\":\"no source workspace context\"}";

                var sourceWs = manager.Get(sourceWorkspaceId);
                if (sourceWs == null)
                    return "{\"success\":false,\"error\":\"source workspace not found\"}";

                var focusList = ParseStringList(focusCharacterIds);

                var events = new List<IGameEvent>();
                foreach (var id in ids)
                {
                    var evt = sourceWs.EventPool?.GetById(id);
                    if (evt == null) continue;

                    if (!string.IsNullOrEmpty(knowledgeTags) && evt.Payload != null)
                        evt.Payload["knowledge_tags"] = knowledgeTags;

                    events.Add(evt);
                }

                int routed = 0;
                if (events.Count > 0 && manager.RouteEvents(directorWs.Id, events, focusList.Count > 0 ? focusList : null))
                    routed = events.Count;

                var w = new JsonWriter(128);
                w.Prop("success", routed > 0);
                w.Prop("routed", routed);
                w.Prop("total", ids.Count);
                w.Prop("targetId", directorWs.Id);
                if (routed < ids.Count)
                    w.Prop("warning", $"{ids.Count - routed} event(s) not found");
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 结束本轮
        // ================================================================

        /// <summary>
        /// 结束本轮叙事。归档 recap，可选给导演留言。所有台词推送完毕后必须调用。
        /// </summary>
        [McpTool(Name = "finish_round",
                 Description = "[评分+20] 撰写总结报告，结束本轮工作")]
        public string FinishRound(
            [McpParam(Description = "本轮叙事的总结，将作为下一轮叙事的前情提要")]
            string recap,
            [McpParam(Description = "给工作群组的留言，说明剧情线是否可继续、期望接收什么类型的事件等",
                      Required = McpRequired.False)]
            string directorNote = null,
            [McpParam(Description = "要丢弃的事件 ID，多个用逗号分隔。已处理完毕不再需要的事件应在此列出，系统将清除这些事件释放资源。",
                      Required = McpRequired.False)]
            string discardedEventIds = null)
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(workspaceId)) return "{}";

                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                bool ok = ws.FinishRound(recap, null, directorNote, null,
                                         ws.CreatedByRole);
                if (!ok) return "{}";

                // 丢弃指定事件
                if (!string.IsNullOrEmpty(discardedEventIds))
                {
                    var ids = ParseStringList(discardedEventIds);
                    if (ws.EventPool != null && ids.Count > 0)
                        ws.EventPool.RemoveEvents(ids);
                }

                // 返回简略确认 + 标记循环终止
                McpSkillRegistry.RoundFinished.Value = true;
                return "{\"ok\":true}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.WritingMcp] finish_round failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 编剧视图序列化（含完整叙事内容）
        // ================================================================

        /// <summary>
        /// 编剧视图序列化：含完整轮次列表和叙事内容。
        /// </summary>
        private string SerializeWriterView(IWorkspace ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(2048);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            w.Prop("createdByRole", ws.CreatedByRole.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            if (ws.MergedFromIds != null && ws.MergedFromIds.Count > 0)
                w.Array("mergedFromIds", ws.MergedFromIds);
            if (ws.FocusCharacterIds != null && ws.FocusCharacterIds.Count > 0)
                w.Array("focusCharacterIds", ws.FocusCharacterIds);
            w.Prop("createdAt", ws.CreatedAt ?? "");
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            w.Prop("currentRecap", ws.CurrentRecap ?? "");

            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                w.Prop("directorMessage", ws.DirectorMessage);

            if (ws.Rounds != null && ws.Rounds.Count > 0)
            {
                var roundJsons = new List<string>();
                foreach (var r in ws.Rounds)
                    roundJsons.Add(SerializeRound(r));
                w.ArrayRaw("rounds", roundJsons);
            }

            return w.Close();
        }

        /// <summary>
        /// 单个轮次的序列化（含完整叙事内容和作者信息）。
        /// </summary>
        private string SerializeRound(WorkspaceRound r)
        {
            var w = new JsonWriter(512);
            w.Prop("seq", r.Seq);
            w.Prop("type", r.Type.ToString());
            w.Prop("recap", r.Recap ?? "");
            if (!string.IsNullOrEmpty(r.Narrative))
                w.Prop("narrative", r.Narrative);
            w.Prop("createdAt", r.CreatedAt ?? "");

            if (r.TriggerEventIds != null && r.TriggerEventIds.Count > 0)
                w.Array("triggerEventIds", r.TriggerEventIds);

            w.Prop("authorRole", r.AuthorRole.ToString());
            if (!string.IsNullOrEmpty(r.AuthorId))
                w.Prop("authorId", r.AuthorId);

            return w.Close();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
