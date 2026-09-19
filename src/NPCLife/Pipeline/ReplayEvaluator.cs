using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 日志回放评估器（旁路分析件）：事件流 → 逐时刻装配材料袋 → 聚合召回率 / 成本代理 / Tier 分布。
    /// 纯确定性、离线，不调用任何 LLM——成本以"消费袋规模 + 估计写手调用次数"代理。可独立停用，不影响主路径。
    /// </summary>
    public static class ReplayEvaluator
    {
        public static ReplayReport Run(IEnumerable<UtteranceMoment> moments,
            IMaterialAssembler assembler, SalienceTierGate gate, IBagBaseline baseline = null)
        {
            var report = new ReplayReport();
            if (moments == null || assembler == null || gate == null) return report;

            var samples = new List<MomentSample>();
            foreach (var m in moments)
            {
                if (m == null) continue;
                var bag = assembler.Assemble(m);
                var tier = gate.Decide(bag);
                int size = bag?.Count ?? 0;

                double recall = 0;
                if (baseline != null)
                {
                    var rr = BagRecallEvaluator.Evaluate(bag, baseline, m);
                    if (rr.BaselineCount > 0) { report.RecallSum += rr.Recall; report.RecallSamples++; recall = rr.Recall; }
                }

                samples.Add(new MomentSample(m.GameTick, m.Kind, tier, size, recall));
                report.MomentsEvaluated++;
                report.TotalBagSize += size;
                switch (tier)
                {
                    case RenderTier.Template: report.TemplateCount++; break;
                    case RenderTier.Cached: report.CachedCount++; break;
                    default: report.FullCount++; report.ConsumedMaterialSum += size; break; // Full 才送达写手
                }
            }
            report.Samples = samples;
            return report;
        }
    }

    /// <summary>单个阈值的扫描结果点。</summary>
    public sealed class ThresholdPoint
    {
        public float SalienceThreshold { get; }
        public ReplayReport Report { get; }
        public ThresholdPoint(float salienceThreshold, ReplayReport report)
        {
            SalienceThreshold = salienceThreshold; Report = report;
        }
    }

    /// <summary>
    /// 显著度阈值回归扫描：对一组阈值各跑一次回放，产出召回率/成本的权衡曲线与可读调参报告。
    /// 阈值越高 → 袋越小 → 成本越低、召回可能越低；用于离线定标 <c>SalienceConfig.SalienceThreshold</c>。
    /// </summary>
    public static class SalienceSweep
    {
        /// <param name="assemblerFor">给定阈值构造一个装配器（调用方决定其内部组件；同一流可复用）。</param>
        public static IReadOnlyList<ThresholdPoint> Run(IEnumerable<float> thresholds,
            IReadOnlyList<UtteranceMoment> moments, IBagBaseline baseline,
            Func<float, IMaterialAssembler> assemblerFor, SalienceTierGate gate)
        {
            var points = new List<ThresholdPoint>();
            if (thresholds == null || assemblerFor == null) return points;
            foreach (var t in thresholds)
                points.Add(new ThresholdPoint(t, ReplayEvaluator.Run(moments, assemblerFor(t), gate, baseline)));
            return points;
        }

        /// <summary>把扫描结果格式化为对齐的文本表格（离线调参报告）。</summary>
        public static string Format(IReadOnlyList<ThresholdPoint> points)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("SalienceSweep (threshold → cost / recall tradeoff)");
            sb.AppendLine("  threshold | full%  | consumed | avgBag | avgRecall");
            if (points == null) return sb.ToString();
            foreach (var p in points)
            {
                var r = p.Report;
                double fullFrac = r.MomentsEvaluated > 0 ? (double)r.FullCount / r.MomentsEvaluated : 0;
                sb.AppendLine(string.Format(ci,
                    "  {0,8:F2} | {1,5:P0} | {2,8} | {3,6:F2} | {4,9:F3}",
                    p.SalienceThreshold, fullFrac, r.ConsumedMaterialSum, r.AvgBagSize, r.AvgRecall));
            }
            return sb.ToString();
        }

        /// <summary>
        /// 调参结论：在召回率不低于 <paramref name="minRecall"/> 的前提下，取阈值最高者（袋最小、成本最低）。
        /// 假设 points 按阈值升序、召回随阈值单调不增；无满足项时返回 null。
        /// </summary>
        public static float? PickThreshold(IReadOnlyList<ThresholdPoint> points, double minRecall)
        {
            if (points == null) return null;
            float? best = null;
            foreach (var p in points)
                if (p.Report != null && p.Report.AvgRecall >= minRecall)
                    best = p.SalienceThreshold;
            return best;
        }
    }
}
