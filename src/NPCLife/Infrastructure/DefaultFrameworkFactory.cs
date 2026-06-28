using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Infrastructure.Knowledge;
using NPCLife.Workspace;
using System;

namespace NPCLife.Infrastructure
{
    /// <summary>
    /// IFrameworkFactory 默认实现。持有框架级默认配置（DriverConfig、CardSerializer），
    /// 将具体类的构造细节封装在框架内部。
    /// </summary>
    public class DefaultFrameworkFactory : IFrameworkFactory
    {
        private readonly DriverConfig _driverConfig;

        /// <summary>
        /// 创建默认工厂。使用框架默认 DriverConfig。
        /// </summary>
        public DefaultFrameworkFactory()
        {
            _driverConfig = DriverConfig.CreateDefault();
        }

        /// <summary>
        /// 创建工厂并注入自定义 DriverConfig。
        /// </summary>
        public DefaultFrameworkFactory(DriverConfig driverConfig)
        {
            _driverConfig = driverConfig ?? DriverConfig.CreateDefault();
        }

        public IWorkspaceManager CreateWorkspaceManager(
            IAuthorityStore store,
            ILogger logger,
            Func<string> timeProvider,
            Action<string> onWorkspaceReady = null)
        {
            return new WorkspaceManager(
                store, logger, timeProvider, _driverConfig,
                serializer: CardSerializer.Default,
                onWorkspaceReady: onWorkspaceReady);
        }

        public IKnowledgeBase CreateKnowledgeBase(ICacheStore store, ILogger logger)
        {
            return new BuiltInKnowledgeBase(store, logger);
        }

        public IAgentInterceptor CreateMetricsInterceptor(AgentRole role)
        {
            return new MetricsInterceptor(role);
        }

        // ================================================================
        // 基础设施服务（单例，委托到静态类）
        // ================================================================

        private readonly IEventBus _events = new StaticEventBusAdapter();
        private readonly IMcpSkillRegistry _skills = new StaticMcpSkillRegistryAdapter();
        private readonly IAgentPipeline _pipeline = new StaticAgentPipelineAdapter();
        private readonly IMetricsRecorder _metrics = new StaticMetricsRecorderAdapter();
        private readonly IFrameworkStatus _status = new StaticFrameworkStatusAdapter();

        public IEventBus Events => _events;
        public IMcpSkillRegistry Skills => _skills;
        public IAgentPipeline Pipeline => _pipeline;
        public IMetricsRecorder Metrics => _metrics;
        public IFrameworkStatus Status => _status;
    }
}
