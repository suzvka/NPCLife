using NPCLife.Cards;
using NPCLife.Pipeline;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NPCLife.Tests.Pipeline
{
    /// <summary>
    /// 材料装配 + 确定性判别器测试：三区归属、判别器确定性/特征单调性、
    /// 未提及度、预算截断（条数上限 + 三区保底）、bag 召回（影子旁路件）、打分延迟。全程零 LLM。
    /// </summary>
    public class PipelineMaterialTests
    {
        private const long Now = 100_000;

        private static AnnotatedEvent Make(string id, string[] actors, long tick, float importance = 5f)
        {
            return CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = new EventCardData
                {
                    EventID = id,
                    DefName = "E",
                    Importance = importance,
                    Payload = new Dictionary<string, string> { { "id", id } }
                },
                ActorIds = actors,
                GameTick = tick
            });
        }

        private static UtteranceMoment Moment(string speaker = "NPC", string listener = "PLAYER")
            => new UtteranceMoment("m", MomentKind.Interaction, speaker, listener, "Scene", Now);

        private static MaterialAssembler Harness(IEventStream stream, SalienceConfig cfg = null,
            IUtteranceMemory memory = null)
        {
            memory = memory ?? new InMemoryUtteranceMemory();
            var disc = new DeterministicSalienceDiscriminator(memory, cfg);
            return new MaterialAssembler(stream, new CoarseConeFilter(stream), disc, cfg, memory);
        }

        private static InMemoryEventStream BuildRegionStream()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Make("s1", new[] { "NPC", "PLAYER" }, Now));      // Shared
            s.TryAppend(Make("s2", new[] { "NPC", "PLAYER" }, Now - 10)); // Shared
            s.TryAppend(Make("n1", new[] { "NPC" }, Now));                 // NpcOnly
            s.TryAppend(Make("n2", new[] { "NPC", "OTHER" }, Now));        // NpcOnly
            s.TryAppend(Make("p1", new[] { "PLAYER" }, Now));              // PlayerOnly
            s.TryAppend(Make("x1", new[] { "OTHER" }, Now));               // 三区外
            return s;
        }

        // ================================================================
        // 三区归属（装配正确性）
        // ================================================================

        [Fact]
        public void Assembler_PlacesMaterialsInCorrectRegions()
        {
            var s = BuildRegionStream();
            var asm = Harness(s);
            var bag = asm.Assemble(Moment());

            Assert.Equal(new[] { "s1", "s2" }, bag.SharedExperience.Select(m => m.EventId).OrderBy(x => x));
            Assert.Equal(new[] { "n1", "n2" }, bag.NpcOnly.Select(m => m.EventId).OrderBy(x => x));
            Assert.Equal(new[] { "p1" }, bag.PlayerOnly.Select(m => m.EventId));
            // 三区外事件不得入袋
            Assert.DoesNotContain("x1", bag.AllMaterials().Select(m => m.EventId));
        }

        [Fact]
        public void Assembler_EmptyCone_YieldsEmptyBag()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Make("x1", new[] { "OTHER" }, Now));
            var bag = Harness(s).Assemble(Moment());
            Assert.True(bag.IsEmpty);
            Assert.Equal(0, bag.Count);
        }

        // ================================================================
        // 判别器：确定性 + 特征单调性
        // ================================================================

        [Fact]
        public void Discriminator_IsDeterministic()
        {
            var disc = new DeterministicSalienceDiscriminator(new InMemoryUtteranceMemory(), null);
            var e = Make("e", new[] { "NPC", "PLAYER" }, Now);
            var m = Moment();
            Assert.Equal(disc.Score(e, MaterialRegion.SharedExperience, m),
                         disc.Score(e, MaterialRegion.SharedExperience, m));
        }

        [Fact]
        public void Discriminator_ImportanceMonotonic()
        {
            var disc = new DeterministicSalienceDiscriminator(null, null);
            var m = Moment();
            var low = Make("low", new[] { "NPC", "PLAYER" }, Now, importance: 1f);
            var high = Make("high", new[] { "NPC", "PLAYER" }, Now, importance: 20f);
            Assert.True(disc.Score(high, MaterialRegion.SharedExperience, m)
                        > disc.Score(low, MaterialRegion.SharedExperience, m));
        }

        [Fact]
        public void Discriminator_RecencyMonotonic()
        {
            var cfg = new SalienceConfig { ImportanceSaturation = 10f };
            var disc = new DeterministicSalienceDiscriminator(null, cfg);
            var m = Moment();
            var recent = Make("r", new[] { "NPC", "PLAYER" }, Now, importance: 5f);
            var stale = Make("s", new[] { "NPC", "PLAYER" }, Now - 500_000, importance: 5f);
            Assert.True(disc.Score(recent, MaterialRegion.SharedExperience, m)
                        > disc.Score(stale, MaterialRegion.SharedExperience, m));
        }

        [Fact]
        public void Discriminator_ActorOverlapMonotonic()
        {
            var disc = new DeterministicSalienceDiscriminator(null, null);
            var m = Moment("NPC", "PLAYER");
            // 同区内比较：actor 与本话语 dyad 重叠更高的候选显著度更高
            var twoInNpc = Make("t", new[] { "NPC", "PLAYER" }, Now);
            var oneInNpc = Make("w", new[] { "NPC", "OTHER" }, Now);
            Assert.True(disc.Score(twoInNpc, MaterialRegion.NpcOnly, m)
                        > disc.Score(oneInNpc, MaterialRegion.NpcOnly, m));
        }

        [Fact]
        public void Discriminator_RegionOrdering()
        {
            var disc = new DeterministicSalienceDiscriminator(null, null);
            var m = Moment();
            var e = Make("e", new[] { "NPC", "PLAYER" }, Now);
            var shared = disc.Score(e, MaterialRegion.SharedExperience, m);
            var npc = disc.Score(e, MaterialRegion.NpcOnly, m);
            var player = disc.Score(e, MaterialRegion.PlayerOnly, m);
            Assert.True(shared > npc && npc > player);
        }

        [Fact]
        public void Discriminator_Novelty_DecaysWithMentions()
        {
            var mem = new InMemoryUtteranceMemory();
            var disc = new DeterministicSalienceDiscriminator(mem, null);
            var m = Moment("NPC", "PLAYER");
            var e = Make("e", new[] { "NPC", "PLAYER" }, Now);
            float fresh = disc.Score(e, MaterialRegion.SharedExperience, m);

            mem.Record(new UtteranceRecord { SpeakerId = "NPC", ListenerId = "PLAYER", ReferencedEventIds = new[] { "e" } });
            float mentioned = disc.Score(e, MaterialRegion.SharedExperience, m);

            Assert.True(fresh > mentioned); // 说过一次 → 未提及度下降 → 显著度下降
        }

        // ================================================================
        // 预算截断（条数上限 + 三区保底）
        // ================================================================

        [Fact]
        public void Budget_RespectsMaxBagItems_WhenNoFloorPressure()
        {
            var s = new InMemoryEventStream();
            for (int i = 0; i < 20; i++) s.TryAppend(Make("s" + i, new[] { "NPC", "PLAYER" }, Now, importance: 5f));
            var cfg = new SalienceConfig { MaxBagItems = 5, MinPerRegion = 1, SalienceThreshold = 0f };
            var bag = Harness(s, cfg).Assemble(Moment());

            Assert.Equal(5, bag.Count); // 单区 20 候选，截断到 5
            Assert.Equal(5, bag.SharedExperience.Count);
        }

        [Fact]
        public void Budget_DefaultConfig_AppliesNoControl()
        {
            // 默认配置不限流——即便候选数远超历史默认上限，也应全部保留
            var s = new InMemoryEventStream();
            for (int i = 0; i < 40; i++) s.TryAppend(Make("s" + i, new[] { "NPC", "PLAYER" }, Now, importance: 5f));
            var bag = Harness(s).Assemble(Moment()); // cfg = null → 默认
            Assert.Equal(40, bag.SharedExperience.Count);
        }

        [Fact]
        public void Budget_GuaranteesPerRegionFloor()
        {
            var s = BuildRegionStream();
            var cfg = new SalienceConfig { MaxBagItems = 3, MinPerRegion = 1, SalienceThreshold = 0f };
            var bag = Harness(s, cfg).Assemble(Moment());

            Assert.True(bag.SharedExperience.Count >= 1);
            Assert.True(bag.NpcOnly.Count >= 1);
            Assert.True(bag.PlayerOnly.Count >= 1);   // 即便全局上限紧张，三区各保底不被饿死
            Assert.Equal(3, bag.Count);
        }

        [Fact]
        public void Budget_FloorsOverrideCap()
        {
            // MaxBagItems 小于非空区数 × 保底：保底优先（总条数可超上限）
            var s = BuildRegionStream(); // 3 个非空区
            var cfg = new SalienceConfig { MaxBagItems = 2, MinPerRegion = 1, SalienceThreshold = 0f };
            var bag = Harness(s, cfg).Assemble(Moment());
            Assert.Equal(3, bag.Count); // 三区各保底 1，突破上限 2
        }

        // ================================================================
        // 话语记忆随袋注入
        // ================================================================

        [Fact]
        public void Bag_CarriesRecentUtterances()
        {
            var mem = new InMemoryUtteranceMemory();
            mem.Record(new UtteranceRecord { SpeakerId = "NPC", ListenerId = "PLAYER", Tick = 1, ReferencedEventIds = new[] { "s1" } });
            mem.Record(new UtteranceRecord { SpeakerId = "NPC", ListenerId = "PLAYER", Tick = 2, ReferencedEventIds = new[] { "n1" } });
            var s = BuildRegionStream();
            var bag = Harness(s, null, mem).Assemble(Moment());
            Assert.Equal(2, bag.RecentUtterances.Count);
        }

        // ================================================================
        // bag 召回（影子旁路件）+ 打分延迟
        // ================================================================

        [Fact]
        public void Recall_HighWhenBudgetGenerous()
        {
            var s = BuildRegionStream();
            // 低阈值 + 宽预算 = 慷慨分组（P5），应完整召回说话者相关事件
            var cfg = new SalienceConfig { MaxBagItems = 50, MinPerRegion = 1, SalienceThreshold = 0f };
            var bag = Harness(s, cfg).Assemble(Moment("NPC"));

            var report = BagRecallEvaluator.Evaluate(bag, new SpeakerParticipantBaseline(s), Moment("NPC"));
            Assert.Equal(4, report.BaselineCount);        // s1 s2 n1 n2（NPC 参与的全部）
            Assert.Equal(1f, report.Recall);              // 100% 召回
        }

        [Fact]
        public void Recall_ShadowsBaselineComparison_NotBelowNinetyPercent()
        {
            // 影子对比：中等预算下仍应保持高召回（≥ 90%），验证判别侧未因截断大量漏链
            var s = new InMemoryEventStream();
            for (int i = 0; i < 10; i++) s.TryAppend(Make("s" + i, new[] { "NPC", "PLAYER" }, Now - i, importance: 5f));
            var cfg = new SalienceConfig { MaxBagItems = 9, MinPerRegion = 1, SalienceThreshold = 0f };
            var bag = Harness(s, cfg).Assemble(Moment("NPC"));

            var report = BagRecallEvaluator.Evaluate(bag, new SpeakerParticipantBaseline(s), Moment("NPC"));
            Assert.True(report.Recall >= 0.9f, $"bag 召回率过低：{report.Recall:P1}");
        }

        [Fact]
        public void Discriminator_ScoreLatency_UnderOneMillisecond()
        {
            var disc = new DeterministicSalienceDiscriminator(new InMemoryUtteranceMemory(), null);
            var m = Moment();
            var e = Make("e", new[] { "NPC", "PLAYER" }, Now);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int N = 200_000;
            float acc = 0;
            for (int i = 0; i < N; i++) acc += disc.Score(e, MaterialRegion.SharedExperience, m);
            sw.Stop();

            double avgMs = sw.Elapsed.TotalMilliseconds / N;
            Assert.True(acc > 0);
            Assert.True(avgMs < 1.0, $"单次打分平均 {avgMs:F5}ms 超出 1ms 预算");
        }
    }
}
