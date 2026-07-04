using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 导演精简 MCP 工具提供者。仅暴露 Action 类工具（创建、分支），
    /// 移除 info 查询类工具（get_storyline、list_storyline）、低频生命周期管理
    /// （suspend/resume/close/merge）和 route_events（已移至 system skill）。
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

        public string HookId => "storyline_direction_slim";
        public string HookName => "剧情分支管理(精简)";
        public string HookDescription => "剧情线的创建、事件创作和分支。仅 Action 操作。导演专用。";
                public string PromptInstruction => null;

        public IReadOnlyList<McpTool> GetTools()
        {
            return new McpTool[]
            {
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(CreateWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(CreateEvent)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(RouteEvents)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(BranchWorkspace)), this),
                McpTool.FromMethod(typeof(DirectionMcpSlimProvider).GetMethod(nameof(FinishRound)), this),
            };
        }

        // ================================================================
        // 委托方法（签名与 DirectionMcpProvider 完全一致）
        // ================================================================

        [McpTool(Name = "create_storyline",
                 Description = "[评分+5] 创建新的剧情线，返回完整信息。角色为 Screenwriter，由编剧负责内容创作。")]
        public string CreateWorkspace(
            [McpParam(Description = "剧情线标题")] string label,
            [McpParam(Description = "剧情分类标签，有多个时用逗号分隔",
                      Required = McpRequired.False)] string tags = null)
            => _inner.CreateWorkspace(label, tags);

        [McpTool(Name = "create_event",
                 Description = "[评分+5] 在目标剧情线中新建事件卡片。按标题匹配目标。")]
        public string CreateEvent(
            [McpParam(Description = "目标剧情线标题")] string targetLabel,
            [McpParam(Description = "事件标题")] string defName,
            [McpParam(Description = "事件内容")] string description,
            [McpParam(Description = "重要度，默认 3.0。越高越容易触发编剧激活。范围建议 1.0-5.0")] double importance = 3.0,
            [McpParam(Description = "关联角色 ThingID，逗号分隔",
                      Required = McpRequired.False)] string actorIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔",
                      Required = McpRequired.False)] string knowledgeTags = null)
            => _inner.CreateEvent(targetLabel, defName, description, importance, actorIds, knowledgeTags);

        [McpTool(Name = "branch_storyline",
                 Description = "[评分+5] 从父剧情线分叉创建新的子剧情线。拷贝父空间的轮次历史。")]
        public string BranchWorkspace(
            [McpParam(Description = "父剧情线 ID")] string parentWorkspaceId,
            [McpParam(Description = "新剧情线标签")] string label,
            [McpParam(Description = "分支前情提要")] string branchRecap)
            => _inner.BranchWorkspace(parentWorkspaceId, label, branchRecap);

        [McpTool(Name = "route_events",
                 Description = "[评分+5] 将事件推送到目标剧情线。按标题匹配目标。")]
        public string RouteEvents(
            [McpParam(Description = "目标剧情线标题")] string targetLabel,
            [McpParam(Description = "要路由的事件 ID，多个用逗号分隔")] string eventIds,
            [McpParam(Description = "附带给目标剧情线的备注",
                      Required = McpRequired.False)] string message = null,
            [McpParam(Description = "聚焦角色 ID，逗号分隔，用于指定该批事件应聚焦的角色",
                      Required = McpRequired.False)] string focusCharacterIds = null,
            [McpParam(Description = "知识库索引标签，逗号分隔，用于标记专有名词，避免接收方产生误解",
                      Required = McpRequired.False)] string knowledgeTags = null)
            => _inner.RouteEvents(targetLabel, eventIds, message, focusCharacterIds, knowledgeTags);

        [McpTool(Name = "finish_round",
                 Description = "[评分+10] 结束本轮导演工作。")]
        public string FinishRound()
            => _inner.FinishRound();
    }
}
