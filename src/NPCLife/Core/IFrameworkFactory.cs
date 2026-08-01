using NPCLife.Agent;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using System;

namespace NPCLife.Core
{
    /// <summary>
    /// 框架组件工厂接口。将 WorkspaceManager、BuiltInKnowledgeBase 等
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
        /// 创建 Agent 编排器。管理所有 Agent 的创建、缓存、生命周期。
        /// 游戏侧通过 AgentOrchestrator.Register 注入 AgentConfigFactory 委托。
        /// </summary>
        /// <param name="manager">工作空间管理器。</param>
        /// <param name="sharedDeps">全局基础设施依赖（LLM 服务、凭证、日志等），所有 Agent 共享。</param>
        IAgentOrchestrator CreateAgentOrchestrator(IWorkspaceManager manager, AgentLoopDependencies sharedDeps);

        // ================================================================
        // 基础设施服务（单例，委托到静态类）
        // ================================================================

        /// <summary>事件总线。</summary>
        IEventBus Events { get; }

        /// <summary>MCP 技能注册表。</summary>
        IMcpSkillRegistry Skills { get; }

        /// <summary>运行时度量记录器。</summary>
        IMetricsRecorder Metrics { get; }

        /// <summary>框架状态内省。</summary>
        IFrameworkStatus Status { get; }
    }
}
