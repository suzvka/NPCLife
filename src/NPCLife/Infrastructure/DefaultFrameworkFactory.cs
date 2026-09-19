using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Infrastructure.Knowledge;

namespace NPCLife.Infrastructure
{
    /// <summary>
    /// IFrameworkFactory 默认实现。持有框架级默认构造（内置知识库），
    /// 并将基础设施单例（事件总线/状态）适配为可注入接口。
    /// </summary>
    public class DefaultFrameworkFactory : IFrameworkFactory
    {
        public IKnowledgeBase CreateKnowledgeBase(ICacheStore store, ILogger logger)
        {
            return new BuiltInKnowledgeBase(store, logger);
        }

        // ================================================================
        // 基础设施服务（单例，委托到静态类）
        // ================================================================

        private readonly IEventBus _events = new StaticEventBusAdapter();
        private readonly IFrameworkStatus _status = new StaticFrameworkStatusAdapter();

        public IEventBus Events => _events;
        public IFrameworkStatus Status => _status;
    }
}
