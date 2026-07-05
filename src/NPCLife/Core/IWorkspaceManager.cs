using System;
using System.Collections.Generic;
using NPCLife.Cards;
using NPCLife.Workspace;

namespace NPCLife.Core
{
    /// <summary>
    /// 工作空间管理器抽象接口。
    /// 职责：工作空间的 CRUD、分支/合并结构操作、事件路由。
    /// 工作空间内部组件（事件池、技能槽）通过 IWorkspace 接口访问，管理器不关心。
    /// 实现：WorkspaceManager。
    /// </summary>
    public interface IWorkspaceManager
    {
        // --- CRUD（5 方法） ---

        /// <summary>创建新的工作空间。</summary>
        IWorkspace Create(string label, WorkspaceRole createdByRole);

        /// <summary>按 ID 查询工作空间。</summary>
        IWorkspace Get(string id);

        /// <summary>列出工作空间（可选状态过滤）。</summary>
        IReadOnlyList<IWorkspace> List(WorkspaceStatus? statusFilter = null);

        /// <summary>获取所有 Active 状态的工作空间。</summary>
        IReadOnlyList<IWorkspace> GetActive();

        /// <summary>
        /// 获取叙事剧情线工作空间。排除导演（Director）自身工作空间，
        /// 仅返回编剧（Screenwriter）和即兴编剧（Improviser）创建的剧情线。
        /// 框架封装角色可见性规则，适配层无需手动按 CreatedByRole 过滤。
        /// </summary>
        /// <param name="status">可选状态过滤，默认不限制。</param>
        IReadOnlyList<IWorkspace> GetStorylines(WorkspaceStatus? status = null);

        /// <summary>更新工作空间状态。</summary>
        bool UpdateStatus(string id, WorkspaceStatus newStatus, string outcome = null);

        /// <summary>设置工作空间标题。由适配层在初始化时调用。</summary>
        void SetLabel(string id, string label);

        /// <summary>设置工作空间的导演留言（简介）。由适配层在初始化时调用。</summary>
        void SetDirectorMessage(string id, string message);

        /// <summary>
        /// 设置工作空间的当前选中模型。
        /// modelJson 格式: {"cred":"凭证名","model":"模型名"}。
        /// </summary>
        void SetCurrentModel(string workspaceId, string modelJson);

        // --- 结构操作（2 方法） ---

        /// <summary>从父工作空间分支。</summary>
        IWorkspace Branch(string parentId, string newLabel, string branchRecap, WorkspaceRole callerRole);

        /// <summary>合并工作空间。</summary>
        bool Merge(string sourceId, string targetId, string mergeRecap, WorkspaceRole callerRole);

        // --- 事件路由（1 方法） ---

        /// <summary>
        /// 将事件路由到指定工作空间的事件池。
        /// 管理器只做转发，实际写入由工作空间的 EventPool 组件处理。
        /// </summary>
        bool RouteEvents(string workspaceId, IReadOnlyList<IGameEvent> events, IReadOnlyList<string> focusCharacterIds = null);

        // --- 持久化（1 方法） ---

        /// <summary>
        /// 将当前内存中的所有工作空间状态刷入持久化存储。
        /// 由宿主在适当的持久化时机调用。
        /// </summary>
        void Persist();
    }
}
