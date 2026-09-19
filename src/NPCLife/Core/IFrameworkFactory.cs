using NPCLife.Core;
using NPCLife.Framework;
using System;

namespace NPCLife.Core
{
    /// <summary>
    /// 框架组件工厂接口。将内置知识库等具体类的创建抽象化，使宿主不直接依赖构造签名。
    /// 本工厂仅提供基础设施构造（知识、事件总线、状态）；
    /// 视锥对齐管线的装配由宿主组合根直接完成（见 <c>Pipeline</c> 命名空间各组件的构造函数）。
    /// </summary>
    public interface IFrameworkFactory
    {
        /// <summary>创建内置知识库。</summary>
        /// <param name="store">缓存存储（本地文件）。</param>
        /// <param name="logger">日志接口。</param>
        IKnowledgeBase CreateKnowledgeBase(ICacheStore store, ILogger logger);

        /// <summary>事件总线。</summary>
        IEventBus Events { get; }

        /// <summary>框架状态内省。</summary>
        IFrameworkStatus Status { get; }
    }
}
