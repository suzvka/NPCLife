using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 事件查询参数对象。支持多维筛选与分页。
    /// </summary>
    public class EventQuery
    {
        /// <summary>起始 tick（含）。null 表示不限。</summary>
        public int? SinceTick;

        /// <summary>结束 tick（不含）。null 表示不限。</summary>
        public int? UntilTick;

        /// <summary>匹配参与 Actor 的 ID。null 表示不限。</summary>
        public string ActorId;

        /// <summary>最小重要度过滤。null 表示不限。</summary>
        public float? MinImportance;

        /// <summary>最大返回数。null 表示不限。</summary>
        public int? Limit;

        /// <summary>分页偏移（从 0 开始）。null 等价于 0。</summary>
        public int? Offset;

        /// <summary>创建一个匹配所有事件的查询。</summary>
        public static EventQuery All => new EventQuery();

        /// <summary>创建按时间范围筛选的查询。</summary>
        public static EventQuery Since(int tick) => new EventQuery { SinceTick = tick };
    }
}
