namespace NPCLife.Pipeline
{
    /// <summary>
    /// 显著度判别器接口：管线的判别核心，**零生成、零模型**。
    /// 对候选材料打显著度分 [0,1] 并排序；KPI 是 bag 召回率而非分类精度。
    /// </summary>
    public interface ISalienceDiscriminator
    {
        /// <summary>
        /// 对单条候选在其所属区内打显著度分。确定性、无副作用、微秒级。
        /// 返回 [0,1]；调用方按 <see cref="SalienceConfig.SalienceThreshold"/> 与预算裁剪。
        /// </summary>
        float Score(AnnotatedEvent evt, MaterialRegion region, UtteranceMoment m);
    }

    /// <summary>
    /// 确定性特征线性打分判别器——判别器的**唯一实现**（不设 ML 分档）。
    ///
    /// 特征族：新近度（时间邻近）+ actor 重叠（连接性统计）+ Importance（情感权重）
    /// + 未提及度（防重复，取自 <see cref="IUtteranceMemory"/>），加权归一后乘以三区系数。
    /// 全部为机械/判别特征，零模型、零 LLM、零外部依赖；权重/阈值来自 <see cref="SalienceConfig"/>（手工常量）。
    /// </summary>
    public sealed class DeterministicSalienceDiscriminator : ISalienceDiscriminator
    {
        private readonly IUtteranceMemory _memory;
        private readonly SalienceConfig _cfg;

        public DeterministicSalienceDiscriminator(IUtteranceMemory memory, SalienceConfig config)
        {
            _memory = memory; // 可为 null：无记忆时未提及度恒为 1（全部视为新鲜）
            _cfg = config ?? SalienceConfig.CreateDefault();
        }

        public float Score(AnnotatedEvent evt, MaterialRegion region, UtteranceMoment m)
        {
            if (evt == null) return 0f;

            float recency = ComputeRecency(evt, m);
            float overlap = ComputeActorOverlap(evt, m);
            float importance = ComputeImportance(evt);
            float novelty = ComputeNovelty(evt, m);

            float weightSum = _cfg.WeightRecency + _cfg.WeightActorOverlap
                + _cfg.WeightImportance + _cfg.WeightNovelty;
            if (weightSum <= 0f) weightSum = 1f;

            float weighted = _cfg.WeightRecency * recency
                + _cfg.WeightActorOverlap * overlap
                + _cfg.WeightImportance * importance
                + _cfg.WeightNovelty * novelty;

            float base01 = Clamp01(weighted / weightSum);
            return Clamp01(base01 * RegionFactor(region));
        }

        // ---- 特征实现（全部确定性）----

        private float ComputeRecency(AnnotatedEvent evt, UtteranceMoment m)
        {
            if (_cfg.RecencyHalfLifeTicks <= 0) return 1f; // 无时间语义时的降级：视为等近
            long now = m != null ? m.GameTick : 0;
            double age = (double)now - evt.GameTick;
            if (age <= 0) return 1f; // 事件不早于时刻（含 tick 缺失/同刻）：最大新近度
            double halfLife = (double)_cfg.RecencyHalfLifeTicks;
            return Clamp01((float)System.Math.Pow(0.5, age / halfLife));
        }

        private static float ComputeActorOverlap(AnnotatedEvent evt, UtteranceMoment m)
        {
            if (m == null || evt.ActorIds == null || evt.ActorIds.Count == 0) return 0f;
            int matched = 0, denom = 0;
            if (!string.IsNullOrEmpty(m.SpeakerId)) { denom++; if (IndexOf(evt.ActorIds, m.SpeakerId) >= 0) matched++; }
            if (!string.IsNullOrEmpty(m.ListenerId)) { denom++; if (IndexOf(evt.ActorIds, m.ListenerId) >= 0) matched++; }
            return denom == 0 ? 0f : (float)matched / denom;
        }

        private float ComputeImportance(AnnotatedEvent evt)
        {
            float imp = evt.Raw != null ? evt.Raw.Importance : 0f;
            if (imp <= 0f) return 0f;
            float k = _cfg.ImportanceSaturation > 0f ? _cfg.ImportanceSaturation : 1f;
            return Clamp01(imp / (imp + k)); // 饱和归一，[0,1)
        }

        private float ComputeNovelty(AnnotatedEvent evt, UtteranceMoment m)
        {
            if (_memory == null) return 1f;
            string speaker = m != null ? m.SpeakerId : null;
            int mentions = !string.IsNullOrEmpty(speaker)
                ? _memory.MentionCount(speaker, evt.EventId)
                : _memory.MentionCount(evt.EventId);
            float decay = mentions * (_cfg.MentionDecay > 0f ? _cfg.MentionDecay : 1f);
            return Clamp01(1f / (1f + decay)); // 提及越多越接近 0
        }

        private float RegionFactor(MaterialRegion region)
        {
            switch (region)
            {
                case MaterialRegion.SharedExperience: return _cfg.RegionFactorShared;
                case MaterialRegion.NpcOnly: return _cfg.RegionFactorNpcOnly;
                default: return _cfg.RegionFactorPlayerOnly;
            }
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> list, string v)
        {
            for (int i = 0; i < list.Count; i++)
                if (System.StringComparer.Ordinal.Equals(list[i], v)) return i;
            return -1;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    }
}
