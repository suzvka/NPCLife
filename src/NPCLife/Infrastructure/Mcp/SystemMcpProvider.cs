using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

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
            [McpParam(Description = "要激活的技能 ID，如 colony_overview / character_query / knowledge_management 等。使用 list_skills 查看全部可用技能。")]
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
            [McpParam(Description = "要反激活的技能 ID。使用 list_skills 查看当前激活状态。")]
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
        [McpTool(Name = "get_current_time",
                 Description = "获取当前游戏时间的格式化字符串。返回值为游戏侧提供的原样时间文本（如 '第2年·夏季·第5天·14h'）。")]
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
    }
}
