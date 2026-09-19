using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NPCLife.Pipeline
{
    /// <summary>单时刻回放样本：时刻类型、渲染层级、袋规模、对基线的召回率。</summary>
    public sealed class MomentSample
    {
        public long Tick;
        public MomentKind Kind;
        public RenderTier Tier;
        public int BagSize;
        public double Recall;         // 基线为空（无相关事件）时为 0，且不计入召回均值

        public MomentSample(long tick, MomentKind kind, RenderTier tier, int bagSize, double recall)
        {
            Tick = tick; Kind = kind; Tier = tier; BagSize = bagSize; Recall = recall;
        }
    }

    /// <summary>
    /// 回放评估报告：Tier 分布 + 成本代理（袋规模/写手调用次数）+ 召回率。纯离线，不含真实 token（成本以消费袋规模代理）。
    /// </summary>
    public sealed class ReplayReport
    {
        public int MomentsEvaluated { get; internal set; }
        public int TemplateCount { get; internal set; }
        public int CachedCount { get; internal set; }
        public int FullCount { get; internal set; }

        /// <summary>成本代理①：估计写手 LLM 调用次数（每个 Full 时刻一次；Template/Cached 零调用）。</summary>
        public int LlmCallEstimate => FullCount;

        /// <summary>成本代理②：Σ 消费袋规模（仅计入实际送达写手的 Full 时刻）。</summary>
        public long ConsumedMaterialSum { get; internal set; }

        /// <summary>全部时刻的平均袋规模（含空袋）。</summary>
        public double AvgBagSize => MomentsEvaluated > 0 ? (double)TotalBagSize / MomentsEvaluated : 0;
        internal long TotalBagSize { get; set; }

        /// <summary>召回率均值（仅对有非空基线的时刻取平均）。</summary>
        public double AvgRecall => RecallSamples > 0 ? RecallSum / RecallSamples : 0;
        internal double RecallSum { get; set; }
        public int RecallSamples { get; internal set; }

        public IReadOnlyList<MomentSample> Samples { get; internal set; }

        public ReplayReport() { Samples = new List<MomentSample>(); }

        private double Fraction(int count) => MomentsEvaluated > 0 ? (double)count / MomentsEvaluated : 0;

        /// <summary>生成人类可读的报告文本（用于离线调参报告/日志）。</summary>
        public string ToReport()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("ReplayReport");
            sb.AppendLine($"  moments          : {MomentsEvaluated}");
            sb.AppendLine($"  tier Template    : {TemplateCount} ({Pct(Fraction(TemplateCount), ci)})");
            sb.AppendLine($"  tier Cached      : {CachedCount} ({Pct(Fraction(CachedCount), ci)})");
            sb.AppendLine($"  tier Full        : {FullCount} ({Pct(Fraction(FullCount), ci)})");
            sb.AppendLine($"  llmCallEstimate  : {LlmCallEstimate}");
            sb.AppendLine($"  consumedMaterials: {ConsumedMaterialSum}");
            sb.AppendLine($"  avgBagSize       : {AvgBagSize.ToString("F2", ci)}");
            sb.AppendLine($"  avgRecall        : {AvgRecall.ToString("F3", ci)} (over {RecallSamples} scored moments)");
            return sb.ToString();
        }

        private static string Pct(double f, CultureInfo ci) => (f * 100).ToString("F1", ci) + "%";
    }
}
