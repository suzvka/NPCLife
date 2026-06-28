using System;

namespace NPCLife.Framework
{
    /// <summary>
    /// 事件总线接口。将静态 EventBus 抽象为可注入实例，
    /// 使宿主不直接依赖具体静态类。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>发布事件。</summary>
        void Publish(string eventName, EventArg args = null);

        /// <summary>
        /// 订阅事件。返回的 Action 调用即可取消订阅。
        /// priority 越小越先执行（0 为默认）。
        /// </summary>
        Action Subscribe(string eventName, Action<EventArg> handler, int priority = 0);

        /// <summary>清除全部订阅。</summary>
        void ClearAll();

        /// <summary>注入日志接口。</summary>
        ILogger Logger { set; }
    }
}
