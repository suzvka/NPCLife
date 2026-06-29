using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 导演精简 MCP 工具提供者。仅暴露 Action 类工具（创建、路由、分支），
    /// 移除 info 查询类工具（get_workspace、list_workspaces）和低频生命周期管理
    /// （suspend/resume/close/merge），强制导演基于预注入上下文工作。
    ///
    /// 内部委托给 <see cref="DirectionMcpProvider"/> 实例，零代码重复。
    /// </summary>
    public class DirectionMcpSlimProvider : IMcpHookProvider
    {
        private readonly DirectionMcpProvider _inner;

        public DirectionMcpSlimProvider(Func<IWorkspaceManager> getWorkspaceManager, ILogger logger)
        {
            _inner = new DirectionMcpProvider(getWorkspaceManager, logger);
        }

        public string HookId => "workspace_direction_slim";
        public string HookName => "工作空间(导演-精简)";
        public string HookDescription => "剧情线工作空间的创建、事件创作、事件路由和分支。仅 Action 操作。导演专用。";

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(CreateWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(CreateEvent)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(RouteEvents)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(BranchWorkspace)), this),
            };
        }

        // ================================================================
        // 委托方法（签名与 DirectionMcpProvider 完全一致）
        // ================================================================

        [McpTool(Name = "create_workspace",
                 Description = "创建新的上下文空间（剧情线工作空间），返回工作空间完整信息。角色为 Screenwriter，由编剧负责内容创作。")]
        public string CreateWorkspace(
            [McpParam(Description = "人类可读标签，如 'RaidAftermath'")] string label,
            [McpParam(Description = "语义标签，逗号分隔，如 'Combat,Romance'",
                      Required = McpRequired.False)] string tags = null)
            => _inner.CreateWorkspace(label, tags);

        [McpTool(Name = "create_event",
                 Description = "在编剧/即兴编剧工作空间中创建新事件卡片，将导演的叙事意图注入目标空间以激活编剧。\n" +
                               "目标必须是编剧或即兴编剧工作空间，禁止填导演自己的工作空间 ID。")]
        public string CreateEvent(
            [McpParam(Description = "目标编剧/即兴编剧工作空间 ID（不是导演自己的 ID）")] string targetWorkspaceId,
            [McpParam(Description = "事件定义名，建议以 DirectorBeat_ 为前缀")] string defName,
            [McpParam(Description = "事件叙事描述。编剧在提示词中看到此文本。")] string description,
            [McpParam(Description = "重要度，默认 3.0。越高越容易触发编剧激活。")] double importance = 3.0,
            [McpParam(Description = "关联角色 ThingID，逗号分隔",
                      Required = McpRequired.False)] string actorIds = null,
            [McpParam(Description = "空间位置提示，如 '殖民地广场'",
                      Required = McpRequired.False)] string mapHint = null,
            [McpParam(Description = "知识库索引标签，逗号分隔",
                      Required = McpRequired.False)] string knowledgeTags = null)
            => _inner.CreateEvent(targetWorkspaceId, defName, description, importance, actorIds, mapHint, knowledgeTags);

        [McpTool(Name = "route_events",
                 Description = "将事件从源工作空间的事件池推送到目标工作空间。可附加留言、聚焦角色和知识索引标签。")]
        public string RouteEvents(
            [McpParam(Description = "源工作空间 ID（事件从这里取）")] string sourceWorkspaceId,
            [McpParam(Description = "目标工作空间 ID（事件推送到这里）")] string targetWorkspaceId,
            [McpParam(Description = "要路由的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "可选留言：附带给目标工作空间的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "可选聚焦角色 ThingID，逗号分隔。导演指定本轮叙事应重点关注的角色。",
                      Required = McpRequired.False)] string focusCharacterIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔。编剧激活时可在事件 payload 中看到这些标签。",
                      Required = McpRequired.False)] string knowledgeTags = null)
            => _inner.RouteEvents(sourceWorkspaceId, targetWorkspaceId, eventIds, message, focusCharacterIds, knowledgeTags);

        [McpTool(Name = "branch_workspace",
                 Description = "从父工作空间分叉创建新的子工作空间。拷贝父空间的轮次历史，追加一条 Branch 轮。")]
        public string BranchWorkspace(
            [McpParam(Description = "父工作空间 ID")] string parentWorkspaceId,
            [McpParam(Description = "新工作空间标签")] string label,
            [McpParam(Description = "分支前情提要：编剧对为什么要开分支以及新线当前状态的总结。")] string branchRecap)
            => _inner.BranchWorkspace(parentWorkspaceId, label, branchRecap);
    }
}
