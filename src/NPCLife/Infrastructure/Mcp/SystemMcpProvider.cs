using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Infrastructure.Mcp
{
    /// <summary>
    /// 系统的 MCP 元工具集。提供 Skill 列表查询、激活、反激活能力。
    /// 属于 system skill，对所有 workspace 隐式可用。
    /// 通过 IMcpHookProvider 接口注入依赖（WorkspaceManager + timeProvider + ILogger），
    /// 通过接口注入依赖，零静态耦合。
    /// </summary>
    public class SystemMcpProvider : IMcpHookProvider
    {
        private readonly Func<IWorkspaceManager> _getWorkspaceManager;
        private readonly Func<string> _getTime;
        private readonly ILogger _logger;

        public SystemMcpProvider(Func<IWorkspaceManager> getWorkspaceManager, Func<string> getTime, ILogger logger)
        {
            _getWorkspaceManager = getWorkspaceManager ?? throw new ArgumentNullException(nameof(getWorkspaceManager));
            _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string HookId => McpSkillRegistry.SystemSkillId;
        public string HookName => "系统";
        public string HookDescription => "系统元工具集（技能列表、激活、反激活、当前时间）";

        public IReadOnlyList<McpTool> GetTools()
        {
            return McpTool.ScanAllFrom(this);
        }

        /// <summary>
        /// 列出当前工作空间的所有可用 Skill 及其激活状态。
        /// </summary>
        [McpTool(Name = "list_skills",
                 Description = "列出当前工作空间的所有可用技能分组及激活状态。激活后才能使用对应技能的工具。")]
        public string ListSkills()
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(workspaceId))
                    return McpSkillRegistry.MakeError("No active workspace context.");
                var wm = _getWorkspaceManager();
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                var ws = wm.Get(workspaceId);
                if (ws == null)
                    return McpSkillRegistry.MakeError("Workspace not found.");
                var activeIds = ws.SkillSlot.ActiveSkillIds;
                return McpSkillRegistry.GetSkillListJson(activeIds);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.SystemMcp] list_skills failed: {e.Message}");
                return "{\"skills\":[],\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 为当前工作空间激活一个 Skill，使其工具可用。
        /// </summary>
        [McpTool(Name = "activate_skill",
                 Description = "为当前工作空间激活一个技能分组，使其中的工具在当前对话中可用。可多次调用叠加激活。返回新激活的工具定义。")]
        public string ActivateSkill(
            [McpParam(Description = "要激活的技能 ID")]
            string skillId)
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(workspaceId))
                    return McpSkillRegistry.MakeError("No active workspace context.");
                var wm = _getWorkspaceManager();
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                var ws = wm.Get(workspaceId);
                if (ws == null)
                    return McpSkillRegistry.MakeError("Workspace not found.");
                return ws.SkillSlot.Activate(skillId);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.SystemMcp] activate_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 为当前工作空间反激活一个 Skill。
        /// </summary>
        [McpTool(Name = "deactivate_skill",
                 Description = "为当前工作空间反激活一个技能分组。system 技能不可反激活。已反激活的技能的工具将不再可用。")]
        public string DeactivateSkill(
            [McpParam(Description = "要反激活的技能 ID")]
            string skillId)
        {
            try
            {
                var workspaceId = McpSkillRegistry.CurrentWorkspaceId.Value;
                if (string.IsNullOrEmpty(workspaceId))
                    return McpSkillRegistry.MakeError("No active workspace context.");
                var wm = _getWorkspaceManager();
                if (wm == null)
                    return McpSkillRegistry.MakeError("WorkspaceManager not available.");
                var ws = wm.Get(workspaceId);
                if (ws == null)
                    return McpSkillRegistry.MakeError("Workspace not found.");
                return ws.SkillSlot.Deactivate(skillId);
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.SystemMcp] deactivate_skill({skillId}) failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 获取当前游戏时间字符串。时间信息通常随 Agent 唤醒事件一同注入，
        /// 此工具仅在 Agent 需要主动获取当前时间时使用。
        /// </summary>
        //[McpTool(Name = "get_current_time",
        //        Description = "获取当前游戏时间的格式化字符串。返回值为游戏侧提供的原样时间文本（如 '第2年·夏季·第5天·14h'）。")]
        public string GetCurrentTime()
        {
            try
            {
                string time = _getTime();
                if (time == null)
                    return "{\"error\":true,\"message\":\"TimeProvider returned null.\"}";
                return "{\"time\":" + JsonHelper.Quote(time) + "}";
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.SystemMcp] get_current_time failed: {e.Message}");
                return "{\"error\":true,\"message\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        /// <summary>
        /// 中止当前对话。
        /// </summary>
        [McpTool(Name = "abort",
                 Description = "如果由于错误过多、触发安全或道德底线等原因而无法继续输出，则调用此工具。调用后 Agent 立即停止本轮处理。")]
        public void Abort()
        {
            McpSkillRegistry.AbortRequested.Value = true;
        }

        // ================================================================
        // 事件路由（通用：源剧情线 → 目标剧情线）
        // ================================================================

        /// <summary>
        /// 将事件从当前剧情线推送到目标剧情线。可附加留言、聚焦角色和知识索引标签。
        /// 默认从当前剧情线取事件（源 ID 留空即用当前上下文）。
        /// </summary>
        [McpTool(Name = "route_events",
                 Description = "将事件推送到目标剧情线。默认从当前剧情线取事件。可附加留言、聚焦角色和知识索引标签。")]
        public string RouteEvents(
            [McpParam(Description = "目标剧情线 ID")]
            string targetWorkspaceId,
            [McpParam(Description = "要路由的事件 ID，多个用逗号分隔")]
            string eventIds,
            [McpParam(Description = "附带给目标剧情线的备注",
                      Required = McpRequired.False)]
            string message = null,
            [McpParam(Description = "聚焦角色 ID，逗号分隔，用于指定该批事件应聚焦的角色",
                      Required = McpRequired.False)]
            string focusCharacterIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔，用于标记专有名词，避免接收方产生误解",
                      Required = McpRequired.False)]
            string knowledgeTags = null)
        {
            try
            {
                var manager = _getWorkspaceManager();
                if (manager == null)
                    return "{\"success\":false,\"error\":\"WorkspaceManager unavailable\"}";

                var ids = ParseStringList(eventIds);
                if (ids.Count == 0)
                    return "{\"success\":false,\"error\":\"no eventIds provided\"}";

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
                _logger.Warning($"[NPCLife.SystemMcp] route_events failed: {e.Message}");
                return "{\"success\":false,\"error\":" + JsonHelper.Quote(e.Message) + "}";
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static List<string> ParseStringList(string input)
        {
            if (string.IsNullOrEmpty(input)) return new List<string>();
            return input.Split(new char[] { ',' })
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }
    }
}
