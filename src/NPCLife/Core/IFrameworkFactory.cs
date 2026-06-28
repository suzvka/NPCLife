using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;

namespace NPCLife.Core
{
    /// <summary>
    /// 框架组件工厂接口。将 WorkspaceManager、BuiltInKnowledgeBase、MetricsInterceptor 等
    /// 具体类的创建抽象化，使宿主（如 RimLife）不直接依赖构造签名。
    /// 
    /// 框架内部重构（增删构造参数、替换实现类）不再影响宿主编译。
    /// </summary>
    public interface IFrameworkFactory
    {
        /// <summary>
        /// 创建工作空间管理器。内部注入序列化器、DriverConfig 等框架级依赖。
        /// </summary>
        /// <param name="store">权威存储（存档文件）。</param>
        /// <param name="logger">日志接口。</param>
        /// <param name="timeProvider">时间提供者。</param>
        /// <param name="onWorkspaceReady">工作空间就绪回调（参数为 workspaceId）。</param>
        IWorkspaceManager CreateWorkspaceManager(
            IAuthorityStore store,
            ILogger logger,
            Func<string> timeProvider,
            Action<string> onWorkspaceReady = null);

        /// <summary>
        /// 创建内置知识库。
        /// </summary>
        /// <param name="store">缓存存储（本地文件）。</param>
        /// <param name="logger">日志接口。</param>
        IKnowledgeBase CreateKnowledgeBase(ICacheStore store, ILogger logger);

        /// <summary>
        /// 创建度量拦截器。
        /// </summary>
        /// <param name="role">所属 Agent 角色。</param>
        IAgentInterceptor CreateMetricsInterceptor(AgentRole role);

        // ================================================================
        // 基础设施服务（单例，委托到静态类）
        // ================================================================

        /// <summary>事件总线。</summary>
        IEventBus Events { get; }

        /// <summary>MCP 技能注册表。</summary>
        IMcpSkillRegistry Skills { get; }

        /// <summary>Agent 管道拦截器管理器。</summary>
        IAgentPipeline Pipeline { get; }

        /// <summary>运行时度量记录器。</summary>
        IMetricsRecorder Metrics { get; }

        /// <summary>框架状态内省。</summary>
        IFrameworkStatus Status { get; }
    }
}
