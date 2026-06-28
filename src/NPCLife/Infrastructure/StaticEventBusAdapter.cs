using System;

namespace NPCLife.Infrastructure
{
    /// <summary>委托到静态 EventBus 的 IEventBus 实现。</summary>
    internal class StaticEventBusAdapter : Framework.IEventBus
    {
        public void Publish(string eventName, Framework.EventArg args = null)
            => Framework.EventBus.Publish(eventName, args);

        public Action Subscribe(string eventName, Action<Framework.EventArg> handler, int priority = 0)
            => Framework.EventBus.Subscribe(eventName, handler, priority);

        public void ClearAll() => Framework.EventBus.ClearAll();

        public Framework.ILogger Logger
        {
            set => Framework.EventBus.Logger = value;
        }
    }
}
