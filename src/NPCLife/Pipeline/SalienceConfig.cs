namespace NPCLife.Pipeline
{
    /// <summary>
    /// 显著度判别器配置：**手工常量**，零学习、零模型。
    /// 判别器为确定性特征线性打分——权重/阈值/预算全部由此 POCO 集中声明，
    /// 依离线影子/回放报告回归调参，不做在线学习。
    ///
    /// 设计基线（召回高于精度）：阈值取保守低值、宁多勿漏；误分组的"软"由写手吸收。
    /// </summary>
    public sealed class SalienceConfig
    {
        // ---- 特征权重（确定性线性加权，权重和自动归一）----

        /// <summary>新近度权重（时间邻近衰减）。</summary>
        public float WeightRecency = 1.0f;

        /// <summary>actor 重叠权重（与本话语 dyad 的实体重合）。</summary>
        public float WeightActorOverlap = 1.2f;

        /// <summary>情感/重要度权重（事件 Importance 饱和归一）。</summary>
        public float WeightImportance = 1.0f;

        /// <summary>未提及度权重（说过越多分越低，防重复）。</summary>
        public float WeightNovelty = 1.5f;

        // ---- 特征归一化常量 ----

        /// <summary>新近度半衰期（tick）：age 越大越接近 0；&lt;=0 视为恒定 1（无时间信息时的降级）。</summary>
        public long RecencyHalfLifeTicks = 60000;

        /// <summary>重要度饱和常数：imp/(imp+k)，k 越大对高重要度越不敏感。</summary>
        public float ImportanceSaturation = 10f;

        /// <summary>未提及度衰减：1/(1+提及次数·k)。</summary>
        public float MentionDecay = 1.0f;

        // ---- 三区显著度系数（共同经历优先，作乘性偏置）----

        /// <summary>K∩V 共同经历系数。</summary>
        public float RegionFactorShared = 1.0f;

        /// <summary>K\V NPC 独知系数。</summary>
        public float RegionFactorNpcOnly = 0.85f;

        /// <summary>V\K 玩家独知系数（认知边界，仅供拦截不参与主动言说）。</summary>
        public float RegionFactorPlayerOnly = 0.7f;

        // ---- 阈值与预算 ----

        /// <summary>入选显著度阈值（保守低值 = 高召回）。低于此值直接淘汰。</summary>
        public float SalienceThreshold = 0.05f;

        /// <summary>
        /// 单袋材料总条数上限（三区共享）。**默认 0 = 不限流**：具体用量需集成测试标定，
        /// 故只保留截断机制、默认不启用；&gt;0 时才按条数截断。
        /// </summary>
        public int MaxBagItems = 0;

        /// <summary>每个非空区的保底名额（仅在 MaxBagItems&gt;0 时参与截断）。0 表示不保底。</summary>
        public int MinPerRegion = 0;

        /// <summary>随袋注入写手的最近话语条数上限。</summary>
        public int RecentUtteranceLimit = 6;

        public static SalienceConfig CreateDefault() => new SalienceConfig();
    }
}
