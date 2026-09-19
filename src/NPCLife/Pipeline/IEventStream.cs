using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 事件流抽象接口：世界的单一 append-only 事件存储 + 多维索引。
    /// 事件只写一次、不预分配归属；语义由消费时刻的投影现算。
    ///
    /// 契约：
    /// - append-only：写入后不可变更、不可删除；
    /// - 事件不预分配归属、不做语义分组；
    /// - 内存实现见 <see cref="InMemoryEventStream"/>。
    /// </summary>
    public interface IEventStream
    {
        /// <summary>
        /// 追加一条标注事件。返回 false 当且仅当 EventId 已存在（幂等拒写）。
        /// 内容指纹相同但 EventId 不同的事件**允许写入**——重复发生的事件
        /// （如两轮掠夺）在 per-consumer 投影中携带不同新近度，是合法材料。
        /// </summary>
        bool TryAppend(AnnotatedEvent evt);

        /// <summary>按条件检索事件切片（写入顺序即时间序）。</summary>
        IReadOnlyList<AnnotatedEvent> Query(StreamQuery query);

        /// <summary>满足条件的事件总数（不受 Offset/Limit 限制）。</summary>
        int Count(StreamQuery query);

        /// <summary>按事件 ID 精确查找，不存在返回 null。</summary>
        AnnotatedEvent GetById(string eventId);

        /// <summary>最后写入的事件，空流返回 null。</summary>
        AnnotatedEvent Latest { get; }

        /// <summary>流中事件总数。</summary>
        int TotalCount { get; }
    }
}
