using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>渲染层（成本分层）。数值即 LLM 调用预算上界。</summary>
    public enum RenderTier
    {
        /// <summary>Tier 0：模板槽位填充，0 次 LLM。</summary>
        Template = 0,
        /// <summary>Tier 1：缓存/轻变体，0~1 次 LLM（可选，未接入时不产生）。</summary>
        Cached = 1,
        /// <summary>Tier 2：全量生成，1 次 LLM。</summary>
        Full = 2,
    }

    /// <summary>
    /// Tier 闸门接口：据材料袋与话语时刻决定渲染层。三元判定 = 显著度 × 重要度 × 新颖度。
    /// </summary>
    public interface ITierGate
    {
        RenderTier Decide(MaterialBag bag);
    }

    /// <summary>
    /// 默认 Tier 闸门。**默认保守（宁可全量生成，避免模板穿帮）**。
    /// 规则：
    /// - 空袋 → Tier 0（无料可写，走模板兜底）；
    /// - AmbientPulse 且袋内最大显著度低于阈值 → Tier 0（低频环境脉冲不值得花 LLM）；
    /// - 其余一律 Tier 2（含 Interaction/Bark/Letter 及一切有显著材料的时刻）。
    /// Tier 1 缓存由 <see cref="UtterancePipeline"/> 在本闸门之后按需插入，本闸门不主动选 Tier 1。
    /// </summary>
    public sealed class SalienceTierGate : ITierGate
    {
        private readonly float _ambientSalienceThreshold;

        /// <param name="ambientSalienceThreshold">环境脉冲值得走 LLM 的最低显著度（默认 0.2，保守）。</param>
        public SalienceTierGate(float ambientSalienceThreshold = 0.2f)
        {
            _ambientSalienceThreshold = ambientSalienceThreshold;
        }

        public RenderTier Decide(MaterialBag bag)
        {
            if (bag == null || bag.IsEmpty) return RenderTier.Template;

            // 仅 AmbientPulse 允许被降级到模板；其余时刻保守地走全量生成。
            if (bag.Moment != null && bag.Moment.Kind == MomentKind.AmbientPulse
                && TopSalience(bag) < _ambientSalienceThreshold)
                return RenderTier.Template;

            return RenderTier.Full;
        }

        private static float TopSalience(MaterialBag bag)
        {
            float top = 0f;
            Accumulate(bag.SharedExperience, ref top);
            Accumulate(bag.NpcOnly, ref top);
            return top; // PlayerOnly 是认知边界，不主动抬升"该说话"的显著度
        }

        private static void Accumulate(IReadOnlyList<MaterialItem> region, ref float top)
        {
            for (int i = 0; i < region.Count; i++)
                if (region[i].Salience > top) top = region[i].Salience;
        }
    }

    /// <summary>
    /// Tier 1 缓存钩子（可选）。命中历史渲染则省一次 LLM（成本 0）。
    /// 只定义接口，不内置缓存后端——宿主按需接入（以材料袋/事件指纹为键）。
    /// </summary>
    public interface ITier1Cache
    {
        bool TryGet(MaterialBag bag, out IReadOnlyList<Framework.Script.ScriptLine> lines);
        void Store(MaterialBag bag, IReadOnlyList<Framework.Script.ScriptLine> lines);
    }
}
