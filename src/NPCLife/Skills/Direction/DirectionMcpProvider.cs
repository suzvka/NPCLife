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
    /// 导演 Agent 的 MCP 工具提供者。通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + ILogger），
    /// 零静态耦合。
    /// </summary>
    [SkillDefinition(
        Id = "storyline_direction",
        Name = "剧情分支管理",
        Description = "剧情线的创建、分支、合并、生命周期管理",
        DefaultRoles = new[] { WorkspaceRole.Director })]
    public class DirectionMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        public DirectionMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "storyline_direction";
        public string HookName => "剧情分支管理";
        public string HookDescription => "剧情线的创建、分支、合并、生命周期管理";
                public string PromptInstruction => "你是一个无情的事件投递机器，主要判断\"逻辑上事件适合哪个剧情线\"，而非\"编剧接下来可能会怎么写\"。即兴剧情线如果长期闲置会定时唤醒，此时由于没有可用事件会产生幻觉，你有责任避免此事：如果新事件看起来没有前因后果，就推到即兴剧情线。成组推送可以间接定制剧情方向。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CreateWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CreateEvent)), this),
                // route_events 已统一收归 system skill
                //McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(RouteEvents)), this),
                // list_storyline 和 get_storyline 已移至 Director 上下文注入
                // （BuildDirectorWorkspaceSummary），不再作为 MCP 工具提供
                //McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(ListWorkspaces)), this),
                //McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(GetWorkspace)), this),
                //McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(SuspendWorkspace)), this),
                //McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(ResumeWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CloseWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(BranchWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(MergeWorkspaces)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(FinishRound)), this),
            };
        }

        // ================================================================
        // 本轮完成
        // ================================================================

        /// <summary>
        /// 通知系统本轮导演工作已完成。所有事件已路由完毕即可调用，结束当前 Agent 循环。
        /// </summary>
        [McpTool(Name = "finish_round",
                 Description = "[+20] 结束本轮导演工作。所有事件已路由完毕时调用。")]
        public string FinishRound(
            [McpParam(Description = "本轮已处理的事件 ID，多个用逗号分隔。包括已路由的、已确认无需路由的。未在此列出的事件将被视为忽略，重要度减半。",
                      Required = McpRequired.False)]
            string usedEventIds = null)
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (!string.IsNullOrEmpty(workspaceId) && !string.IsNullOrEmpty(usedEventIds))
                {
                    var manager = _getWorkspaceManager();
                    var ws = manager?.Get(workspaceId);
                    var ids = ParseStringList(usedEventIds);
                    if (ws?.EventPool != null && ids.Count > 0)
                        ws.EventPool.RemoveEvents(ids);
                }
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] finish_round cleanup failed: {e.Message}");
            }

            McpSkillRegistry.RoundFinished.Value = true;
            return "{\"ok\":true}";
        }
        // ================================================================
        // 创建
        // ================================================================

        /// <summary>
        /// 创建新的剧情线剧情线。创建者角色固定为 Director。
        /// </summary>
        [McpTool(Name = "create_storyline",
                 Description = "[-10] 创建新的剧情线\\n- 对积分策略的解释：剧情线回收时会有回合积分，因此这里可视作投资本轮积分到可能产生精彩叙事的新剧情线，避免无节制新建。")]
        public string CreateWorkspace(
            [McpParam(Description = "剧情线标题")] string label,
            [McpParam(Description = "剧情分类标签，有多个时用逗号分隔",
                      Required = McpRequired.False)] string tags = null,
            [McpParam(Description = "剧情线描述/简介，说明这条剧情线的叙事方向",
                      Required = McpRequired.False)] string description = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var tagList = ParseStringList(tags);

                var ws = manager.Create(label, WorkspaceRole.Screenwriter);

                // 存储导演对剧情线的描述
                if (!string.IsNullOrEmpty(description))
                    manager.SetDirectorMessage(ws.Id, description);

                return SerializeDirectorView(ws);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] create_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 事件创作
        // ================================================================

        /// <summary>
        /// 在目标剧情线的事件池中创建新事件卡片。导演可在无外部事件时主动注入叙事驱动力。
        /// 创建的事件 DefName 建议以 DirectorBeat_ 为前缀。
        /// </summary>
        [McpTool(Name = "create_event",
                 Description = "[0] 在指定剧情线中新建事件卡片，仅在无合适事件时使用")]
        public string CreateEvent(
            [McpParam(Description = "目标剧情线标题")] string targetLabel,
            [McpParam(Description = "事件标题")] string defName,
            [McpParam(Description = "事件内容")] string description,
            [McpParam(Description = "重要度，默认 3.0。越高越容易触发编剧激活。范围建议 1.0-5.0")] double importance = 3.0,
            [McpParam(Description = "关联角色 ThingID，逗号分隔",
                      Required = McpRequired.False)] string actorIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔",
                      Required = McpRequired.False)] string knowledgeTags = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{\"success\":false,\"error\":\"WorkspaceManager unavailable\"}";

                var ws = FindWorkspaceByLabel(manager, targetLabel);
                if (ws == null)
                    return "{\"success\":false,\"error\":" + JsonHelper.Quote("target storyline not found by label: " + targetLabel) + "}";

                if (ws.Status != WorkspaceStatus.Active)
                    return "{\"success\":false,\"error\":\"target storyline is not Active\"}";

                var eventId = "dir_" + Guid.NewGuid().ToString("N").Substring(0, 8);

                var actorList = new List<EventActorRef>();
                if (!string.IsNullOrEmpty(actorIds))
                {
                    foreach (var id in ParseStringList(actorIds))
                        actorList.Add(EventActorRef.Pawn(id, id, "Bystander"));
                }

                var eventCard = new EventCardData
                {
                    EventID = eventId,
                    DefName = defName,
                    Importance = (float)importance,
                    Actors = actorList,
                    Payload = BuildCreateEventPayload(description, knowledgeTags)
                };

                ws.EventPool.Append(eventCard);

                var w = new JsonWriter(256);
                w.Prop("success", true);
                w.Prop("eventId", eventId);
                w.Prop("defName", defName);
                w.Prop("targetLabel", targetLabel);
                w.Prop("targetId", ws.Id);
                w.Prop("importance", eventCard.Importance, "F2");
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] create_event failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 事件路由（导演专属：按标题索引目标剧情线）
        // ================================================================

        /// <summary>
        /// 将事件从导演工作空间推送到目标剧情线。按标题匹配目标剧情线。
        /// 导演可在此实现限流、审核等策略（区别于编剧的 route_events）。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "[+5] 将事件推送到目标剧情线。按标题匹配目标。")]
        public string RouteEvents(
            [McpParam(Description = "目标剧情线标题")] string targetLabel,
            [McpParam(Description = "事件 ID，逗号分隔")] string eventIds,
            [McpParam(Description = "附带给目标剧情线的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "本事件涉及的主要角色ID，逗号分隔",
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

                var targetWs = FindWorkspaceByLabel(manager, targetLabel);
                if (targetWs == null)
                    return "{\"success\":false,\"error\":" + JsonHelper.Quote("target storyline not found by label: " + targetLabel) + "}";

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
                if (events.Count > 0 && manager.RouteEvents(targetWs.Id, events, focusList.Count > 0 ? focusList : null))
                {
                    routed = events.Count;
                    // 推送成功后从源工作空间事件池中移除已路由事件
                    sourceWs.EventPool?.RemoveEvents(ids);
                }

                // 存储导演对目标剧情线的备注/描述
                if (!string.IsNullOrEmpty(message))
                    manager.SetDirectorMessage(targetWs.Id, message);

                var w = new JsonWriter(128);
                w.Prop("success", routed > 0);
                w.Prop("routed", routed);
                w.Prop("total", ids.Count);
                w.Prop("targetLabel", targetLabel);
                w.Prop("targetId", targetWs.Id);
                if (routed < ids.Count)
                    w.Prop("warning", $"{ids.Count - routed} event(s) not found or target storyline inactive");
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 查询
        // ================================================================

        /// <summary>
        /// 列出剧情线摘要（导演视图：含信号、统计，不含叙事内容）。
        /// </summary>
        public string ListWorkspaces(string status = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "[]";

                WorkspaceStatus? statusFilter = null;
                if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkspaceStatus>(status, true, out var s))
                    statusFilter = s;

                var workspaces = manager.List(statusFilter);
                return SerializeDirectorSummaryList(workspaces);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] list_workspaces failed: {e.Message}");
                return "[]";
            }
        }

        /// <summary>
        /// 获取单个剧情线的导演视图（含信号、统计，不含叙事内容）。
        /// </summary>
        public string GetWorkspace(
            [McpParam(Description = "剧情线ID")] string workspaceId)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var ws = manager.Get(workspaceId);
                if (ws == null) return "{}";

                return SerializeDirectorView(ws);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] get_workspace({workspaceId}) failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 生命周期
        // ================================================================

        /// <summary>
        /// 挂起剧情线。仅 Director 可调用（入口层面由 Skill 归属保证）。
        /// </summary>
        public string SuspendWorkspace(
            [McpParam(Description = "剧情线 ID")] string workspaceId)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                bool ok = manager.UpdateStatus(workspaceId, WorkspaceStatus.Suspended);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] suspend_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 恢复已挂起的剧情线。仅 Director 可调用。
        /// </summary>
        public string ResumeWorkspace(
            [McpParam(Description = "剧情线 ID")] string workspaceId)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                bool ok = manager.UpdateStatus(workspaceId, WorkspaceStatus.Active);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] resume_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 关闭剧情线（完成或废弃）。仅 Director 可调用。
        /// </summary>
        [McpTool(Name = "close_workspace",
                 Description = "[+10] 关闭剧情线")]
        public string CloseWorkspace(
            [McpParam(Description = "剧情线 ID")] string workspaceId)
        {
            try
            {
                string outcomeType = "Completed";
                string reason = null;
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                WorkspaceStatus targetStatus;
                if (string.Equals(outcomeType, "Completed", StringComparison.OrdinalIgnoreCase))
                    targetStatus = WorkspaceStatus.Completed;
                else
                {
                    _logger.Warning($"[NPCLife.DirectionMcp] close_workspace: invalid outcomeType '{outcomeType}', must be Completed or Abandoned.");
                    return "{}";
                }

                bool ok = manager.UpdateStatus(workspaceId, targetStatus, reason);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(workspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] close_workspace failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 分支 / 合并
        // ================================================================

        /// <summary>
        /// 从现有剧情线分叉出新空间。仅 Director 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "branch_storyline",
                 Description = "[0] 从父剧情线分叉创建新的子剧情线。拷贝父空间的轮次历史，追加一条 Branch 轮。")]
        public string BranchWorkspace(
            [McpParam(Description = "父剧情线 ID")] string parentWorkspaceId,
            [McpParam(Description = "新剧情线标签")] string label,
            [McpParam(Description = "分支前情提要：编剧对为什么要开分支以及新线当前状态的总结。")]
            string branchRecap)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var child = manager.Branch(parentWorkspaceId, label, branchRecap, WorkspaceRole.Director);
                return child != null ? SerializeDirectorView(child) : "{}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] branch_workspace failed: {e.Message}");
                return "{}";
            }
        }

        /// <summary>
        /// 合并两个剧情线。仅 Director 可调用，内部由 storylineManager 校验。
        /// </summary>
        [McpTool(Name = "merge_storylines",
                 Description = "[+20] 合并剧情线")]
        public string MergeWorkspaces(
            [McpParam(Description = "源剧情线 ID（将被合并并废弃）")] string sourceWorkspaceId,
            [McpParam(Description = "目标剧情线 ID（接收数据）")] string targetWorkspaceId,
            [McpParam(Description = "合并前情提要：两条线合并后的叙事状态总结。")]
            string mergeRecap)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                bool ok = manager.Merge(sourceWorkspaceId, targetWorkspaceId, mergeRecap, WorkspaceRole.Director);
                if (!ok) return "{}";
                return SerializeDirectorView(manager.Get(targetWorkspaceId));
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] merge_workspaces failed: {e.Message}");
                return "{}";
            }
        }

        // ================================================================
        // 导演视图序列化（不含叙事内容，含信号和统计）
        // ================================================================

        /// <summary>
        /// 导演视图序列化：含元数据、信号、轮次统计，不含具体叙事内容。
        /// </summary>
        private string SerializeDirectorView(IWorkspace ws)
        {
            if (ws == null) return "{}";

            var w = new JsonWriter(1024);
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
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            w.Prop("createdAt", ws.CreatedAt ?? "");
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);

            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                w.Prop("directorMessage", ws.DirectorMessage);

            return w.Close();
        }

        /// <summary>
        /// 导演视图摘要列表（轻量，不含轮次详情，含导演留言）。
        /// </summary>
        private string SerializeDirectorSummaryList(IReadOnlyList<IWorkspace> workspaces)
        {
            if (workspaces == null || workspaces.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < workspaces.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SerializeDirectorSummary(workspaces[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>
        /// 单个剧情线的导演摘要（轻量）。
        /// </summary>
        private string SerializeDirectorSummary(IWorkspace ws)
        {
            var w = new JsonWriter(256);
            w.Prop("id", ws.Id ?? "");
            w.Prop("label", ws.Label ?? "");
            w.Prop("status", ws.Status.ToString());
            w.Prop("createdByRole", ws.CreatedByRole.ToString());
            if (ws.ParentId != null)
                w.Prop("parentId", ws.ParentId);
            w.Prop("roundCount", ws.Rounds?.Count ?? 0);
            if (ws.FocusCharacterIds != null && ws.FocusCharacterIds.Count > 0)
                w.Array("focusCharacterIds", ws.FocusCharacterIds);
            w.Prop("createdAt", ws.CreatedAt ?? "");
            w.Prop("lastActivityAt", ws.LastActivityAt ?? "");
            if (ws.Outcome != null)
                w.Prop("outcome", ws.Outcome);
            // 导演留言
            if (!string.IsNullOrEmpty(ws.DirectorMessage))
                w.Prop("directorMessage", Truncate(ws.DirectorMessage, 120));
            return w.Close();
        }

        // ================================================================
        // 辅助
        // ================================================================

        /// <summary>
        /// 按标题（Label）从活跃工作空间中查找目标。大小写不敏感。
        /// 若匹配到多个同标题 workspace，选择最近活跃的并记录警告。
        /// </summary>
        private IWorkspace FindWorkspaceByLabel(IWorkspaceManager manager, string label)
        {
            if (manager == null || string.IsNullOrEmpty(label)) return null;

            var actives = manager.GetActive();
            var matches = new List<IWorkspace>();
            foreach (var ws in actives)
            {
                if (string.Equals(ws.Label, label, StringComparison.OrdinalIgnoreCase))
                    matches.Add(ws);
            }

            if (matches.Count == 0) return null;
            if (matches.Count == 1) return matches[0];

            // 多个匹配：选最近活跃的
            _logger.Warning($"[NPCLife.DirectionMcp] Multiple storylines with label '{label}' found ({matches.Count}). Using most recently active.");
            matches.Sort((a, b) => string.Compare(b.LastActivityAt, a.LastActivityAt, StringComparison.OrdinalIgnoreCase));
            return matches[0];
        }

        private List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        /// <summary>
        /// 构建 create_event 的 Payload 字典。始终包含 description 和 source，
        /// 可选附加 knowledge_tags。
        /// </summary>
        private static Dictionary<string, string> BuildCreateEventPayload(string description, string knowledgeTags)
        {
            var payload = new Dictionary<string, string>
            {
                { "description", description },
                { "source", "director" }
            };
            if (!string.IsNullOrEmpty(knowledgeTags))
                payload["knowledge_tags"] = knowledgeTags;
            return payload;
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
        }
    }
}
