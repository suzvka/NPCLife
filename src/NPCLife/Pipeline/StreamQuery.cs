namespace NPCLife.Pipeline
{
    /// <summary>
    /// 事件流检索条件（机械过滤，零语义判断）。
    /// 所有条件按 AND 组合；null / long.MinValue 表示不限制。
    /// 命中单一索引（ActorId / LocationKey / Fingerprint / Provenance）时走对应索引，
    /// 无索引可用时退化为时间序全扫描（降级行为，仅性能损失）。
    /// </summary>
    public sealed class StreamQuery
    {
        /// <summary>不限条件的全量查询单例。</summary>
        public static readonly StreamQuery All = new StreamQuery();

        /// <summary>actor 索引：检索该实体参与（actor 集合含此 ID）的事件。</summary>
        public string ActorId;

        /// <summary>空间索引：按 LocationKey 精确匹配。</summary>
        public string LocationKey;

        /// <summary>指纹索引：按内容指纹精确匹配。</summary>
        public string Fingerprint;

        /// <summary>溯源索引：按 Provenance 精确匹配。</summary>
        public string Provenance;

        /// <summary>时间下界（含）。long.MinValue 表示不限。</summary>
        public long TickFrom = long.MinValue;

        /// <summary>时间上界（含）。long.MinValue 表示不限。</summary>
        public long TickTo = long.MinValue;

        /// <summary>结果偏移（按写入顺序，即时间序）。</summary>
        public int Offset;

        /// <summary>结果条数上限。0 表示不限。</summary>
        public int Limit;
    }
}
