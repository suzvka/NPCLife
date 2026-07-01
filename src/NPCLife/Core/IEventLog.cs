using NPCLife.Cards;
using System;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 事件日志抽象接口。提供 append-only 写入、按条件查询，以及 pending 缓冲区的阈值激活能力。
    /// AgentLoop 依赖此接口获取待处理事件并订阅激活通知。
    /// 实现：WorkspaceEventPool。
    /// </summary>
    public interface IEventLog
    {
        /// <summary>所属工作空间的唯一 ID。</summary>
        string WorkspaceId { get; }

        // --- 日志写入与查询 ---

        /// <summary>追加一条事件。</summary>
        void Append(IGameEvent evt);

        /// <summary>按条件查询事件（支持分页）。</summary>
        IReadOnlyList<IGameEvent> Query(EventQuery query);

        /// <summary>返回满足条件的总数（不受 Limit 限制）。</summary>
        int Count(EventQuery query);

        /// <summary>最近一条事件，无事件时返回 null。</summary>
        IGameEvent Latest { get; }

        /// <summary>按事件 ID 查找事件（查 recent 缓冲区）。路由工具依赖此方法。</summary>
        IGameEvent GetById(string eventId);

        /// <summary>累计追加的事件总数。</summary>
        int TotalAppended { get; }

        // --- 池激活语义 ---

        /// <summary>pending 缓冲区中的事件数。</summary>
        int PendingCount { get; }

        /// <summary>
        /// pending 缓冲区是否为空。
        /// 适配层在注入定时器脉冲（TimerPulse）前应检查此项：
        /// 当缓冲区非空时跳过脉冲注入，避免事件堆积和通知丢失。
        /// </summary>
        bool IsPendingEmpty { get; }

        /// <summary>pending 缓冲区中所有事件的重要度总和。</summary>
        float TotalImportance { get; }

        /// <summary>
        /// 取出所有 pending 事件并清空缓冲区。
        /// 调用者获得事件所有权，池重置计数器和重要度。
        /// </summary>
        IReadOnlyList<IGameEvent> DrainPending();

        /// <summary>当池状态变化且满足任一阈值时触发。订阅者（AgentLoop）被动激活。</summary>
        event Action OnThresholdReached;

        /// <summary>
        /// 清空事件缓存。Agent 运行结束时调用，所有已处理/未处理的事件一律清除。
        /// </summary>
        void ClearCache();

        /// <summary>
        /// 从 EventCache 中移除指定事件。用于 Agent 选择性清理已处理事件，保留未处理事件供下一轮复用。
        /// 同时从 PendingEventIds 中移除（如果存在），并重新计算 pending 重要度。
        /// </summary>
        void RemoveEvents(IReadOnlyCollection<string> eventIds);

        /// <summary>
        /// pending 缓冲区中是否存在 DefName 不等于 <paramref name="excludeDefName"/> 的事件。
        /// AgentLoop 用此方法过滤 TimerPulse：仅 TimerPulse 时不自动激活，等待真实事件到达后再一并处理。
        /// </summary>
        bool HasPendingExcept(string excludeDefName);
    }
}
