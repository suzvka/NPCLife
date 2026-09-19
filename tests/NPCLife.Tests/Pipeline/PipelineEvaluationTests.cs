using NPCLife.Cards;
using NPCLife.Pipeline;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NPCLife.Tests.Pipeline
{
    /// <summary>
    /// 离线回放评估（Phase 5 QA 基线）：日志回放、Tier/成本/召回聚合、阈值扫描单调性与调参结论。
    /// 全程确定性、零 LLM；成本以消费袋规模代理。
    /// </summary>
    public class PipelineEvaluationTests
    {
        private static AnnotatedEvent Make(string id, string[] actors, float importance, long tick)
            => CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = new EventCardData { EventID = id, DefName = "E", Importance = importance, Payload = new Dictionary<string, string> { { "id", id } } },
                ActorIds = actors,
                GameTick = tick
            });

        private static UtteranceMoment Moment(string speaker, string listener, MomentKind kind, long tick)
            => new UtteranceMoment("m", kind, speaker, listener, "Scene", tick);

        private static InMemoryEventStream Corpus()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Make("e1", new[] { "NPC" }, 2f, 100));
            s.TryAppend(Make("e2", new[] { "NPC" }, 5f, 1000));
            s.TryAppend(Make("e3", new[] { "NPC", "PLAYER" }, 9f, 10000));
            s.TryAppend(Make("e4", new[] { "NPC" }, 3f, 20000));
            s.TryAppend(Make("e5", new[] { "PLAYER" }, 8f, 20000));   // 玩家独知，NPC 袋外
            s.TryAppend(Make("e6", new[] { "NPC" }, 1f, 30000));
            return s;
        }

        private static IReadOnlyList<UtteranceMoment> Moments() => new[]
        {
            Moment("NPC", "PLAYER", MomentKind.Interaction, 30000),
            Moment("NPC", "PLAYER", MomentKind.Interaction, 10000),
            Moment("NPC", "PLAYER", MomentKind.AmbientPulse, 30000),
            Moment("SOLO", "SOLO2", MomentKind.Interaction, 30000),   // 无相关事件 → 空袋 → Template
        };

        private static MaterialAssembler Assembler(IEventStream s, float threshold)
        {
            var cfg = new SalienceConfig { SalienceThreshold = threshold };
            return new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(null, cfg), cfg);
        }

        // ================================================================
        // ① 回放聚合
        // ================================================================

        [Fact]
        public void Replay_AggregatesTierCostAndRecall()
        {
            var s = Corpus();
            var report = ReplayEvaluator.Run(Moments(), Assembler(s, 0.05f),
                new SalienceTierGate(), new SpeakerParticipantBaseline(s));

            Assert.Equal(4, report.MomentsEvaluated);
            Assert.Equal(report.MomentsEvaluated, report.TemplateCount + report.CachedCount + report.FullCount);
            Assert.Equal(report.FullCount, report.LlmCallEstimate);
            // 成本代理 = Σ Full 时刻袋规模
            Assert.Equal(report.Samples.Where(x => x.Tier == RenderTier.Full).Sum(x => x.BagSize),
                (int)report.ConsumedMaterialSum);
            // 仅 3 个 NPC 时刻有非空基线被计入召回均值
            Assert.Equal(3, report.RecallSamples);
            Assert.InRange(report.AvgRecall, 0d, 1d);
        }

        [Fact]
        public void Replay_EmptyBagMomentIsTemplate_ZeroCost()
        {
            var s = Corpus();
            var report = ReplayEvaluator.Run(new[] { Moment("SOLO", "SOLO2", MomentKind.Interaction, 30000) },
                Assembler(s, 0.05f), new SalienceTierGate(), new SpeakerParticipantBaseline(s));

            Assert.Equal(1, report.TemplateCount);
            Assert.Equal(0, report.ConsumedMaterialSum);
            Assert.Equal(0, report.RecallSamples);   // 基线空，不计召回
        }

        [Fact]
        public void Replay_IsDeterministic_AcrossRuns()
        {
            var s = Corpus();
            var a = ReplayEvaluator.Run(Moments(), Assembler(s, 0.3f), new SalienceTierGate(), new SpeakerParticipantBaseline(s));
            var b = ReplayEvaluator.Run(Moments(), Assembler(s, 0.3f), new SalienceTierGate(), new SpeakerParticipantBaseline(s));

            Assert.Equal(a.FullCount, b.FullCount);
            Assert.Equal(a.ConsumedMaterialSum, b.ConsumedMaterialSum);
            Assert.Equal(a.AvgRecall, b.AvgRecall);
        }

        [Fact]
        public void LogReplayRoundTrip_ProducesSameReport()
        {
            var original = Corpus();
            var log = EventStreamLog.ToLog(original);
            var replayed = new InMemoryEventStream();
            int n = EventStreamLog.Replay(log, replayed);
            Assert.Equal(6, n);

            var r1 = ReplayEvaluator.Run(Moments(), Assembler(original, 0.2f), new SalienceTierGate(), new SpeakerParticipantBaseline(original));
            var r2 = ReplayEvaluator.Run(Moments(), Assembler(replayed, 0.2f), new SalienceTierGate(), new SpeakerParticipantBaseline(replayed));

            Assert.Equal(r1.ConsumedMaterialSum, r2.ConsumedMaterialSum);
            Assert.Equal(r1.AvgRecall, r2.AvgRecall);
            Assert.Equal(r1.FullCount, r2.FullCount);
        }

        // ================================================================
        // ② 阈值扫描：召回/成本单调性 + 调参结论
        // ================================================================

        private static IReadOnlyList<ThresholdPoint> Sweep()
        {
            var s = Corpus();
            var moments = Moments();
            var baseline = new SpeakerParticipantBaseline(s);
            return SalienceSweep.Run(new[] { 0.05f, 0.2f, 0.4f, 0.6f, 0.8f }, moments, baseline,
                t => Assembler(s, t), new SalienceTierGate());
        }

        [Fact]
        public void Sweep_CostAndRecall_AreMonotonicNonIncreasing()
        {
            var pts = Sweep();
            for (int i = 1; i < pts.Count; i++)
            {
                Assert.True(pts[i].Report.ConsumedMaterialSum <= pts[i - 1].Report.ConsumedMaterialSum,
                    $"成本应随阈值升高不增（{i}）");
                Assert.True(pts[i].Report.AvgRecall <= pts[i - 1].Report.AvgRecall + 1e-9,
                    $"召回应随阈值升高不增（{i}）");
            }
        }

        [Fact]
        public void Sweep_LowThreshold_HighRecall_DefaultFloor()
        {
            var pts = Sweep();
            Assert.True(pts[0].Report.AvgRecall >= 0.8, "极低阈值下应对说话者相关事件高召回");
        }

        [Fact]
        public void PickThreshold_ReturnsMaxThresholdMeetingFloor()
        {
            var pts = Sweep();
            var picked = SalienceSweep.PickThreshold(pts, 0.8);
            Assert.NotNull(picked);
            var chosen = pts.First(p => p.SalienceThreshold == picked.Value);
            Assert.True(chosen.Report.AvgRecall >= 0.8);
            // 更高阈值都不再满足下限（最大化）
            Assert.All(pts.Where(p => p.SalienceThreshold > picked.Value),
                p => Assert.True(p.Report.AvgRecall < 0.8));
        }

        [Fact]
        public void Reports_AreProducible()
        {
            var s = Corpus();
            Assert.Contains("ReplayReport",
                ReplayEvaluator.Run(Moments(), Assembler(s, 0.05f), new SalienceTierGate(), new SpeakerParticipantBaseline(s)).ToReport());
            Assert.Contains("SalienceSweep", SalienceSweep.Format(Sweep()));
        }
    }
}
