using NPCLife.Workspace;

namespace NPCLife.Core
{
    /// <summary>
    /// Agent 配置工厂委托。框架注入 workspace 和 manager，游戏侧返回
    /// <see cref="AgentConfig"/>（系统提示词策略、可选的行为参数覆盖）。
    /// 返回 null 表示当前条件不满足（如 LLM 未配置），框架将不会缓存并允许后续重试。
    /// 
    /// AgentLoop 的构造由框架内部完成，适配层不再需要直接接触 AgentLoop 类型。
    /// </summary>
    public delegate AgentConfig AgentConfigFactory(IWorkspace workspace, IWorkspaceManager manager);

    /// <summary>
    /// Agent 编排器。管理所有 Agent 的完整生命周期。
    /// 
    /// 职责（框架侧，与游戏无关）：
    /// - 按角色懒创建/查找工作空间
    /// - Agent 实例缓存（双重检查锁）
    /// - 销毁/重建/全量清理
    /// - 新工作空间就绪 → Agent 角色路由
    /// - 内部构造 AgentLoop（游戏侧无需关心）
    /// 
    /// 游戏侧通过 <see cref="Register"/> 注入 <see cref="AgentConfigFactory"/> 委托，
    /// 提供提示词策略和可选的运行时参数。
    /// </summary>
    public interface IAgentOrchestrator
    {
        /// <summary>
        /// 注册指定角色的 Agent 配置工厂。
        /// 同一角色仅允许注册一次。
        /// </summary>
        void Register(WorkspaceRole role, AgentConfigFactory factory);

        /// <summary>
        /// 获取或懒创建指定角色的 Agent（Director / Improviser）。
        /// 内部按角色查找活跃工作空间，不存在时自动创建。
        /// 返回 <see cref="IAgentLoop"/> 只读观测句柄。
        /// </summary>
        IAgentLoop GetAgent(WorkspaceRole role);

        /// <summary>
        /// 获取或懒创建指定工作空间的 Agent（Screenwriter）。
        /// 工作空间必须已存在（由导演通过 create_storyline 创建）。
        /// 返回 <see cref="IAgentLoop"/> 只读观测句柄。
        /// </summary>
        IAgentLoop GetAgent(string workspaceId);

        /// <summary>
        /// 获取或懒创建指定角色的工作空间（不创建 Agent）。
        /// 用于需要直接操作 EventPool 的场景（如定时器脉冲注入）。
        /// </summary>
        IWorkspace GetOrCreateWorkspace(WorkspaceRole role);

        /// <summary>
        /// 新工作空间就绪回调。根据角色路由到正确的 Agent 工厂。
        /// 应在 WorkspaceManager 创建/加载工作空间后调用。
        /// </summary>
        void OnWorkspaceReady(string workspaceId);

        /// <summary>
        /// 销毁并移除指定工作空间的 Agent。
        /// </summary>
        void DisposeAgent(string workspaceId);

        /// <summary>
        /// 销毁所有 Agent 并立即为活跃工作空间重建。
        /// 用于提示词或驱动参数变更后热重载。
        /// </summary>
        void RebuildAll();

        /// <summary>
        /// 销毁所有 Agent 并清空缓存。用于存档切换或框架关闭。
        /// </summary>
        void DisposeAll();
    }
}
