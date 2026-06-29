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
    /// 导演 Agent 的 MCP 工具提供者。通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + ILogger），
    /// 零静态耦合。
    /// </summary>
    public class DirectionMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly ILogger _logger;

        public DirectionMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => "workspace_direction";
        public string HookName => "工作空间(导演)";
        public string HookDescription => "剧情线工作空间的创建、分支、合并、生命周期管理。导演专用。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CreateWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CreateEvent)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(ListWorkspaces)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(GetWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(SuspendWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(ResumeWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(CloseWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(BranchWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(MergeWorkspaces)), this),
                McpTool.FromMethod(typeof(DirectionMcpProvider).GetMethod(nameof(RouteEvents)), this),
            };
        }
        // ================================================================
        // 创建
        // ================================================================

        /// <summary>
        /// 创建新的剧情线工作空间。创建者角色固定为 Director。
        /// </summary>
        [McpTool(Name = "create_workspace",
                 Description = "创建新的上下文空间（剧情线工作空间），返回工作空间完整信息。角色为 Screenwriter，由编剧负责内容创作，Director 负责协调。")]
        public string CreateWorkspace(
            [McpParam(Description = "人类可读标签，如 'RaidAftermath'")] string label,
            [McpParam(Description = "语义标签，逗号分隔，如 'Combat,Romance'",
                      Required = McpRequired.False)] string tags = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                var tagList = ParseStringList(tags);

                var ws = manager.Create(label, WorkspaceRole.Screenwriter);
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
        /// 在目标工作空间的事件池中创建新事件卡片。导演可在无外部事件时主动注入叙事驱动力。
        /// 创建的事件 DefName 建议以 DirectorBeat_ 为前缀。
        /// </summary>
        [McpTool(Name = "create_event",
                 Description = "在编剧/即兴编剧工作空间中创建新事件卡片，将导演的叙事意图注入目标空间以激活编剧。\n" +
                               "目标必须是编剧或即兴编剧工作空间（通过 create_workspace 创建的），禁止填导演自己的工作空间 ID。\n" +
                               "DefName 建议以 DirectorBeat_ 为前缀。可附带知识索引标签。")]
        public string CreateEvent(
            [McpParam(Description = "目标编剧/即兴编剧工作空间 ID（不是导演自己的 ID）。")] string targetWorkspaceId,
            [McpParam(Description = "事件定义名，建议以 DirectorBeat_ 为前缀。")] string defName,
            [McpParam(Description = "事件叙事描述。编剧在提示词中看到此文本作为事件主要内容。")] string description,
            [McpParam(Description = "重要度，默认 3.0。越高越容易触发编剧激活。范围建议 1.0-5.0。")] double importance = 3.0,
            [McpParam(Description = "关联角色 ThingID，逗号分隔。如 pawn_123,pawn_456",
                      Required = McpRequired.False)] string actorIds = null,
            [McpParam(Description = "空间位置提示，如 '殖民地广场'",
                      Required = McpRequired.False)] string mapHint = null,
            [McpParam(Description = "知识库索引标签，逗号分隔。如 '心灵感应,殖民地历史'。编剧处理此事件时可在 payload 中看到这些标签。",
                      Required = McpRequired.False)] string knowledgeTags = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{\"success\":false,\"error\":\"WorkspaceManager unavailable\"}";

                var ws = manager.Get(targetWorkspaceId);
                if (ws == null)
                    return "{\"success\":false,\"error\":\"target workspace not found\"}";

                if (ws.Status != WorkspaceStatus.Active)
                    return "{\"success\":false,\"error\":\"target workspace is not Active\"}";

                // 禁止向调用者自己的工作空间注入事件（事件流应为 Director → Screenwriter，单向）
                var callerWsId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (!string.IsNullOrEmpty(callerWsId)
                    && string.Equals(callerWsId, targetWorkspaceId, StringComparison.Ordinal))
                    return "{\"success\":false,\"error\":\"cannot create events in your own workspace. Target must be a screenwriter/improviser workspace.\"}";

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
                    MapHint = mapHint ?? "",
                    Payload = BuildCreateEventPayload(description, knowledgeTags)
                };

                ws.EventPool.Append(eventCard);

                var w = new JsonWriter(256);
                w.Prop("success", true);
                w.Prop("eventId", eventId);
                w.Prop("defName", defName);
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
        // 查询
        // ================================================================

        /// <summary>
        /// 列出工作空间摘要（导演视图：含信号、统计，不含叙事内容）。
        /// </summary>
        [McpTool(Name = "list_workspaces",
                 Description = "列出所有工作空间摘要（导演视图）。含信号、轮次数、角色数，不含叙事内容。可按状态过滤。")]
        public string ListWorkspaces(
            [McpParam(Description = "过滤状态：Active/Suspended/Completed/Abandoned，留空=全部",
                      Required = McpRequired.False)] string status = null)
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
        /// 获取单个工作空间的导演视图（含信号、统计，不含叙事内容）。
        /// </summary>
        [McpTool(Name = "get_workspace",
                 Description = "获取指定工作空间的导演视图：含角色、标签、信号、轮次统计，不含叙事台词内容。")]
        public string GetWorkspace(
            [McpParam(Description = "工作空间唯一 ID")] string workspaceId)
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
        /// 挂起工作空间。仅 Director 可调用（入口层面由 Skill 归属保证）。
        /// </summary>
        [McpTool(Name = "suspend_workspace",
                 Description = "挂起指定工作空间，保留数据但停止回合推送。")]
        public string SuspendWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId)
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
        /// 恢复已挂起的工作空间。仅 Director 可调用。
        /// </summary>
        [McpTool(Name = "resume_workspace",
                 Description = "恢复已挂起的工作空间，重新开始接受回合推送。")]
        public string ResumeWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId)
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
        /// 关闭工作空间（完成或废弃）。仅 Director 可调用。
        /// </summary>
        [McpTool(Name = "close_workspace",
                 Description = "关闭工作空间，标记为 Completed 或 Abandoned。")]
        public string CloseWorkspace(
            [McpParam(Description = "工作空间 ID")] string workspaceId,
            [McpParam(Description = "结束类型：Completed 或 Abandoned")] string outcomeType,
            [McpParam(Description = "结束原因描述",
                      Required = McpRequired.False)] string reason = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null) return "{}";

                WorkspaceStatus targetStatus;
                if (string.Equals(outcomeType, "Completed", StringComparison.OrdinalIgnoreCase))
                    targetStatus = WorkspaceStatus.Completed;
                else if (string.Equals(outcomeType, "Abandoned", StringComparison.OrdinalIgnoreCase))
                    targetStatus = WorkspaceStatus.Abandoned;
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
        /// 从现有工作空间分叉出新空间。仅 Director 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "branch_workspace",
                 Description = "从父工作空间分叉创建新的子工作空间。拷贝父空间的轮次历史，追加一条 Branch 轮。")]
        public string BranchWorkspace(
            [McpParam(Description = "父工作空间 ID")] string parentWorkspaceId,
            [McpParam(Description = "新工作空间标签")] string label,
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
        /// 合并两个工作空间。仅 Director 可调用，内部由 WorkspaceManager 校验。
        /// </summary>
        [McpTool(Name = "merge_workspaces",
                 Description = "将源空间的轮次合并到目标空间，然后废弃源空间。按 Seq 去重，追加一条 Merge 轮。")]
        public string MergeWorkspaces(
            [McpParam(Description = "源工作空间 ID（将被合并并废弃）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（接收数据）")] string targetWorkspaceId,
            [McpParam(Description = "合并前情提要：编剧对两条线合并后的叙事状态总结。")]
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
        // 事件路由（通用：源工作空间 → 目标工作空间）
        // ================================================================

        /// <summary>
        /// 将事件从源工作空间推送到目标工作空间。可附加留言。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "将事件从源工作空间的事件池推送到目标工作空间。可附加留言、聚焦角色和知识索引标签。源和目标都必须是已存在的工作空间 ID。")]
        public string RouteEvents(
            [McpParam(Description = "源工作空间 ID（事件从这里取）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（事件推送到这里）")] string targetWorkspaceId,
            [McpParam(Description = "要路由的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "可选留言：附带给目标工作空间的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "可选聚焦角色 ThingID，逗号分隔。导演指定本轮叙事应重点关注的角色，编剧激活时可见。每次推送覆盖更新。",
                      Required = McpRequired.False)] string focusCharacterIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔。如 '心灵感应,殖民地历史'。目标工作空间的编剧激活时可在事件 payload 中看到这些标签，用于关联知识库词条。",
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
        
                var sourceWs = manager.Get(sourceWorkspaceId);
                if (sourceWs == null)
                    return "{\"success\":false,\"error\":\"source workspace not found\"}";
        
                var focusList = ParseStringList(focusCharacterIds);
        
                var events = new List<IGameEvent>();
                foreach (var id in ids)
                {
                    var evt = sourceWs.EventPool?.GetById(id);
                    if (evt == null) continue;

                    // 导演可为此批次事件标注知识索引标签，编剧激活时自动可见
                    if (!string.IsNullOrEmpty(knowledgeTags) && evt.Payload != null)
                        evt.Payload["knowledge_tags"] = knowledgeTags;

                    events.Add(evt);
                }
        
                int routed = 0;
                if (events.Count > 0 && manager.RouteEvents(targetWorkspaceId, events, focusList.Count > 0 ? focusList : null))
                    routed = events.Count;

                var w = new JsonWriter(128);
                w.Prop("success", routed > 0);
                w.Prop("routed", routed);
                w.Prop("total", ids.Count);
                if (!string.IsNullOrEmpty(message))
                    w.Prop("message", message);
                if (routed < ids.Count)
                    w.Prop("warning", $"{ids.Count - routed} event(s) not found or target workspace inactive");
                return w.Close();
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.DirectionMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
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
        /// 单个工作空间的导演摘要（轻量）。
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
