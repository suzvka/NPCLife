using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework.Script;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Pipeline
{
    /// <summary>一次话语时刻生成的完整结果（供投递车道消费 + 审计）。</summary>
    public sealed class PipelineResult
    {
        public UtteranceMoment Moment;
        public MaterialBag Bag;
        public RenderTier Tier;
        public IReadOnlyList<ScriptLine> Lines = System.Array.Empty<ScriptLine>();

        /// <summary>本次实际发起的 LLM 调用次数（0=Tier0/1命中，1=Tier2）。成本 ∝ 消费的直接度量。</summary>
        public int LlmCalls;

        /// <summary>写手调用失败而降级到 Tier 0 时为 true。</summary>
        public bool Degraded;

        public int? TokensUsed;
        public double LatencyMs;
        public string Error;
    }

    /// <summary>
    /// 话语管线门面。编排：装配材料袋 → Tier 判定 → 渲染 → 台词。
    /// 这是 pull 投影管线的消费入口：宿主在话语时刻构造 <see cref="UtteranceMoment"/> 调用本方法，
    /// 得到 <see cref="ScriptLine"/> 后交既有投递车道（游戏侧消费契约不变）。
    ///
    /// 降级链：写手失败/无凭证/超时 → Tier 0 模板，绝不空手而归。
    /// </summary>
    public sealed class UtterancePipeline
    {
        private readonly IMaterialAssembler _assembler;
        private readonly ITierGate _gate;
        private readonly IWriter _writer;
        private readonly ITier0Renderer _tier0;
        private readonly ITier1Cache _cache;      // 可空：未接入则跳过 Tier 1
        private readonly IUtteranceMemory _memory; // 可空：接入则记连续性

        public UtterancePipeline(
            IMaterialAssembler assembler,
            ITierGate gate,
            IWriter writer,
            ITier0Renderer tier0Renderer,
            ITier1Cache cache = null,
            IUtteranceMemory memory = null)
        {
            _assembler = assembler ?? throw new System.ArgumentNullException(nameof(assembler));
            _gate = gate ?? throw new System.ArgumentNullException(nameof(gate));
            _writer = writer ?? throw new System.ArgumentNullException(nameof(writer));
            _tier0 = tier0Renderer ?? throw new System.ArgumentNullException(nameof(tier0Renderer));
            _cache = cache;
            _memory = memory;
        }

        public async Task<PipelineResult> GenerateAsync(
            UtteranceMoment moment,
            CharacterCard persona = null,
            IReadOnlyList<KnowledgeEntry> anchors = null,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var bag = _assembler.Assemble(moment, persona, anchors);
            var tier = _gate.Decide(bag);
            var result = new PipelineResult { Moment = moment, Bag = bag, Tier = tier };

            if (tier == RenderTier.Template)
            {
                result.Lines = _tier0.Render(bag);
                result.LlmCalls = 0;
            }
            else
            {
                // Tier 1（可选）：命中缓存则省一次 LLM。
                if (_cache != null && _cache.TryGet(bag, out var cached) && cached != null && cached.Count > 0)
                {
                    result.Tier = RenderTier.Cached;
                    result.Lines = cached;
                    result.LlmCalls = 0;
                }
                else
                {
                    var write = await _writer.WriteAsync(bag, ct).ConfigureAwait(false);
                    // 仅当写手实际发起调用才计成本（前置失败如无凭证未产生 LLM 开销）。
                    result.LlmCalls = write != null && write.AttemptedLlm ? 1 : 0;
                    if (write != null && write.Success)
                    {
                        result.Lines = write.Lines;
                        result.TokensUsed = write.TotalTokens;
                        _cache?.Store(bag, write.Lines);
                        RecordUtterance(moment, write);
                    }
                    else
                    {
                        // 降级到 Tier 0（不阻塞游戏，本轮仍产出台词）
                        result.Tier = RenderTier.Template;
                        result.Degraded = true;
                        result.Error = write?.Error;
                        result.Lines = _tier0.Render(bag);
                    }
                }
            }

            sw.Stop();
            result.LatencyMs = sw.Elapsed.TotalMilliseconds;
            return result;
        }

        // 记连续性：成功生成后存一条话语流水（refs 缺省时仅记文本）。
        private void RecordUtterance(UtteranceMoment moment, WriteResult write)
        {
            if (_memory == null || moment == null) return;
            string text = null;
            for (int i = 0; i < write.Lines.Count; i++)
                if (write.Lines[i].Type == ScriptLineType.Dialogue && !string.IsNullOrEmpty(write.Lines[i].Text))
                { text = write.Lines[i].Text; break; }

            _memory.Record(new UtteranceRecord
            {
                SpeakerId = moment.SpeakerId,
                ListenerId = moment.ListenerId,
                Tick = moment.GameTick,
                Text = text,
                ReferencedEventIds = write.ReferencedEventIds
            });
        }
    }

    /// <summary>
    /// 轻量成本聚合器（非热路径）。累计各话语结果的调用数/Tier 分布/token，
    /// 产出成本画像供离线对比。
    /// </summary>
    public sealed class GenerationMetrics
    {
        public int Utterances;
        public int TotalLlmCalls;
        public int Tier0Count;
        public int Tier1Count;
        public int Tier2Count;
        public long TotalTokens;
        public double TotalLatencyMs;

        /// <summary>平均每次话语消费的 LLM 调用数（成本 ∝ 消费的核心指标）。</summary>
        public double AvgLlmCallsPerUtterance => Utterances == 0 ? 0 : (double)TotalLlmCalls / Utterances;

        public void Record(PipelineResult r)
        {
            if (r == null) return;
            Utterances++;
            TotalLlmCalls += r.LlmCalls;
            switch (r.Tier)
            {
                case RenderTier.Template: Tier0Count++; break;
                case RenderTier.Cached: Tier1Count++; break;
                default: Tier2Count++; break;
            }
            if (r.TokensUsed.HasValue) TotalTokens += r.TokensUsed.Value;
            TotalLatencyMs += r.LatencyMs;
        }
    }
}
