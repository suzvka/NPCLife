using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NPCLife.Skills
{
    /// <summary>
    /// 写作工具集的 MCP 提供者。通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + ILogger）。
    /// 供编剧和即兴编剧共用。所有"读"操作（事件列表、剧情线上下文）由 prompt 自动注入，
    /// 此 Provider 仅提供写操作工具（push_dialogue / push_narration / push_action / push_pause / route_events / finish_round）。
    /// </summary>
    [SkillDefinition(
        Id = "storyline_writing",
        Name = "写作工具集",
        Description = "用于创作具体台词脚本的工具",
        DefaultRoles = new[] { WorkspaceRole.Screenwriter, WorkspaceRole.Improviser })]
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
                public string PromptInstruction => "写台词时注意口语化，技巧如下：\\n-- 遇到表示结束的标点符号就断句。\\n-- 一个自然的实现方式是，如果你想让角色说一段很长的话，那么就多断几句。\\n-- 我们预期此时收到连续多个的同一角色发言。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushDialogue)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushNarration)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushAction)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(PushPause)), this),
                // route_events 已统一收归 system skill
                //McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(RouteEvents)), this),
                McpTool.FromMethod(typeof(WritingMcpProvider).GetMethod(nameof(FinishRound)), this),
            };
        }
        // ================================================================
        // 逐句台词推送
        // ================================================================

        /// <summary>
        /// 内部共享实现：推送单句台词到当前剧情线。
        /// </summary>
        private string PushLineInternal(string speakerId, string text, double delay, string type)
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
                _logger.Warning($"[NPCLife.WritingMcp] push_line({type}) failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 推送一句角色对话。每句立即投递到游戏侧显示。可并行调用多句。
        /// </summary>
        [McpTool(Name = "dialogue_line",
                 Description = "[+5] 写一句台词，也可以写拟声词")]
        public string PushDialogue(
            [McpParam(Description = "说话角色的ID")]
            string speakerId,
            [McpParam(Description = "对话正文")]
            string text,
            [McpParam(Description = "本行起始延迟秒数，默认 0。")]
            double delay = 0)
        {
            return PushLineInternal(speakerId, text, delay, "dialogue");
        }

        /// <summary>
        /// 推送一句旁白或环境描写。
        /// </summary>
        [McpTool(Name = "narration_line",
                 Description = "[+1] 写一句旁白或环境描写")]
        public string PushNarration(
            [McpParam(Description = "旁白正文")]
            string text,
            [McpParam(Description = "本行起始延迟秒数，默认 0。")]
            double delay = 0)
        {
            return PushLineInternal("", text, delay, "narration");
        }

        /// <summary>
        /// 推送一句动作描写。
        /// </summary>
        [McpTool(Name = "action_line",
                 Description = "[+1] 写一句动作描写")]
        public string PushAction(
            [McpParam(Description = "动作主体角色ID")]
            string speakerId,
            [McpParam(Description = "动作描写正文")]
            string text,
            [McpParam(Description = "本行起始延迟秒数，默认 0。")]
            double delay = 0)
        {
            return PushLineInternal(speakerId, text, delay, "action");
        }

        /// <summary>
        /// 插入一个纯停顿。
        /// </summary>
        [McpTool(Name = "pause_",
                 Description = "[+1] 插入一个纯停顿")]
        public string PushPause(
            [McpParam(Description = "停顿时长秒数，默认 0")]
            double delay = 0)
        {
            return PushLineInternal("", "", delay, "pause");
        }

        // ================================================================
        // 结束本轮
        // ================================================================

        /// <summary>
        /// 将事件从当前编剧工作空间发送给导演。无需指定目标，自动路由到导演工作空间。
        /// 编剧完成所有台词推送后，可调用此工具将关联事件反馈给导演。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "[-5] 将事件退稿并投诉导演")]
        public string RouteEvents(
            [McpParam(Description = "要发送的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "说明为什么这些事件不该推到此处",
                      Required = McpRequired.False)] string message = null)
        {
            try
            {
                string focusCharacterIds = null;
                string knowledgeTags = null;
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
                {
                    routed = events.Count;
                    // 推送成功后从源工作空间事件池中移除已路由事件
                    sourceWs.EventPool?.RemoveEvents(ids);
                }

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
                 Description = "[+20] 撰写总结报告，结束本轮工作")]
        public string FinishRound(
            [McpParam(Description = "本轮叙事的总结，将作为下一轮叙事的前情提要")]
            string recap,
            [McpParam(Description = "给工作群组的留言，说明剧情线是否可继续、期望接收什么类型的事件等",
                      Required = McpRequired.False)]
            string directorNote = null,
            [McpParam(Description = "本轮叙事中使用到的事件 ID，多个用逗号分隔。你写的台词如果涉及某个事件，就应在此标记它。",
                      Required = McpRequired.False)]
            string usedEventIds = null)
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

                // 标记并移除本轮叙事已使用的事件
                if (!string.IsNullOrEmpty(usedEventIds))
                {
                    var ids = ParseStringList(usedEventIds);
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
