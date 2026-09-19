using NPCLife.Cards;
using NPCLife.Pipeline;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NPCLife.Tests.Pipeline
{
    /// <summary>
    /// 机械地基测试：采集层（句柄推导/永不拒绝）、事件流（append-only + 多索引）、
    /// 粗视锥过滤器（三区划分机械正确率 100%）、日志回放流水线。全程零 LLM。
    /// </summary>
    public class PipelineFoundationTests
    {
        // ================================================================
        // 测试脚手架
        // ================================================================

        private static IGameEvent Evt(string id, string defName = "E", float importance = 1f,
            Dictionary<string, string> payload = null)
        {
            return new EventCardData
            {
                EventID = id,
                DefName = defName,
                Importance = importance,
                Payload = payload ?? new Dictionary<string, string> { { "id", id } }
            };
        }

        private static AnnotatedEvent Annotate(string id, string[] actors, long tick = 0,
            string location = null, string provenance = null, string defName = "E",
            Dictionary<string, string> payload = null)
        {
            return CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = Evt(id, defName, payload: payload),
                ActorIds = actors,
                GameTick = tick,
                LocationKey = location,
                Provenance = provenance
            });
        }

        // ================================================================
        // 采集层：句柄推导 + 永不拒绝（P8）
        // ================================================================

        [Fact]
        public void Capture_PrefersContextActorIds()
        {
            var a = Annotate("e1", new[] { "X", "Y" });
            Assert.Equal(new[] { "X", "Y" }, a.ActorIds);
        }

        [Fact]
        public void Capture_DerivesActorsFromEventWhenContextMissing()
        {
            var raw = new EventCardData
            {
                EventID = "e1",
                DefName = "E",
                Actors = new List<EventActorRef> { EventActorRef.Pawn("P1", "n", "Initiator") },
                Payload = new Dictionary<string, string>()
            };
            var a = CaptureLayer.Annotate(EventCaptureContext.FromRaw(raw));
            Assert.Equal(new[] { "P1" }, a.ActorIds);
        }

        [Fact]
        public void Capture_SynthesizesHandlesWhenAbsent_NeverRejects()
        {
            // 仅提供 raw，无 actor/位置/溯源：全部走合成，事件仍被产出（P8 永不拒绝）
            var a = CaptureLayer.Annotate(EventCaptureContext.FromRaw(Evt("e1")));
            Assert.NotNull(a);
            Assert.NotEmpty(a.ActorIds);                            // 合成 actor 占位
            Assert.Equal(CaptureLayer.SyntheticLocation, a.LocationKey);
            Assert.Equal(CaptureLayer.SyntheticProvenance, a.Provenance);
            Assert.False(string.IsNullOrEmpty(a.Fingerprint));
        }

        [Fact]
        public void Capture_LocationDerivedFromPayload()
        {
            var a = Annotate("e1", new[] { "X" }, payload: new Dictionary<string, string> { { "mapId", "Map_A" } });
            Assert.Equal("Map_A", a.LocationKey);
        }

        [Fact]
        public void Capture_NullRawReturnsNull()
        {
            Assert.Null(CaptureLayer.Annotate(null));
            Assert.Null(CaptureLayer.Annotate(new EventCaptureContext()));
        }

        [Fact]
        public void Fingerprint_IsStableRegardlessOfPayloadOrder()
        {
            var p1 = new Dictionary<string, string> { { "a", "1" }, { "b", "2" } };
            var p2 = new Dictionary<string, string> { { "b", "2" }, { "a", "1" } };
            var f1 = CaptureLayer.DeriveFingerprint(Evt("x", payload: p1));
            var f2 = CaptureLayer.DeriveFingerprint(Evt("y", payload: p2));
            Assert.Equal(f1, f2); // 按 key 排序，忽略插入顺序
        }

        // ================================================================
        // 事件流：append-only + 索引
        // ================================================================

        [Fact]
        public void Stream_AppendAndIdempotentDedup()
        {
            var s = new InMemoryEventStream();
            Assert.True(s.TryAppend(Annotate("e1", new[] { "X" })));
            Assert.False(s.TryAppend(Annotate("e1", new[] { "X" }))); // 同 EventId 幂等拒写
            Assert.Equal(1, s.TotalCount);
        }

        [Fact]
        public void Stream_AllowsSameFingerprintDifferentId()
        {
            // 重复发生的合法事件（同内容不同 ID）不因指纹被丢弃
            var s = new InMemoryEventStream();
            var p = new Dictionary<string, string> { { "k", "v" } };
            Assert.True(s.TryAppend(Annotate("e1", new[] { "X" }, defName: "Raid", payload: p)));
            Assert.True(s.TryAppend(Annotate("e2", new[] { "X" }, defName: "Raid", payload: p)));
            Assert.Equal(2, s.TotalCount);
        }

        [Fact]
        public void Stream_ActorIndexAndTimeOrder()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Annotate("e1", new[] { "X", "Y" }, tick: 10));
            s.TryAppend(Annotate("e2", new[] { "Y" }, tick: 20));
            s.TryAppend(Annotate("e3", new[] { "Z" }, tick: 30));

            var yEvents = s.Query(new StreamQuery { ActorId = "Y" });
            Assert.Equal(new[] { "e1", "e2" }, yEvents.Select(e => e.EventId)); // 时间序
            Assert.Equal("e3", s.Latest.EventId);
        }

        [Fact]
        public void Stream_TickRangeAndPaging()
        {
            var s = new InMemoryEventStream();
            for (int i = 1; i <= 5; i++) s.TryAppend(Annotate("e" + i, new[] { "X" }, tick: i * 10));

            var mid = s.Query(new StreamQuery { TickFrom = 20, TickTo = 40 });
            Assert.Equal(new[] { "e2", "e3", "e4" }, mid.Select(e => e.EventId));

            var paged = s.Query(new StreamQuery { Offset = 1, Limit = 2 });
            Assert.Equal(new[] { "e2", "e3" }, paged.Select(e => e.EventId));
            Assert.Equal(5, s.Count(StreamQuery.All));
        }

        [Fact]
        public void Stream_LocationAndProvenanceIndexes()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Annotate("e1", new[] { "X" }, location: "Map_A", provenance: "ModA"));
            s.TryAppend(Annotate("e2", new[] { "X" }, location: "Map_B", provenance: "ModA"));

            Assert.Equal(1, s.Count(new StreamQuery { LocationKey = "Map_A" }));
            Assert.Equal(2, s.Count(new StreamQuery { Provenance = "ModA" }));
        }

        // ================================================================
        // 粗视锥：三区划分机械正确率 100%（独立 oracle 对照）
        // ================================================================

        /// <summary>独立计算三区（不依赖被测实现），作为机械正确率的判定基准。</summary>
        private static (HashSet<string> shared, HashSet<string> npcOnly, HashSet<string> playerOnly)
            Oracle(IEnumerable<AnnotatedEvent> events, string speaker, string listener)
        {
            var k = new HashSet<string>(events.Where(e => e.ActorIds.Contains(speaker)).Select(e => e.EventId));
            var v = new HashSet<string>(events.Where(e => e.ActorIds.Contains(listener)).Select(e => e.EventId));
            var shared = new HashSet<string>(k); shared.IntersectWith(v);
            var npcOnly = new HashSet<string>(k); npcOnly.ExceptWith(v);
            var playerOnly = new HashSet<string>(v); playerOnly.ExceptWith(k);
            return (shared, npcOnly, playerOnly);
        }

        private static List<AnnotatedEvent> BuildConeScenario()
        {
            return new List<AnnotatedEvent>
            {
                Annotate("both", new[] { "NPC", "PLAYER" }),   // K∩V
                Annotate("npcA", new[] { "NPC" }),             // K\V
                Annotate("npcB", new[] { "NPC", "OTHER" }),    // K\V
                Annotate("plrA", new[] { "PLAYER" }),           // V\K
                Annotate("none", new[] { "OTHER" }),            // 三区外
            };
        }

        [Fact]
        public void Cone_ThreeRegions_MatchOracle()
        {
            var s = new InMemoryEventStream();
            var events = BuildConeScenario();
            foreach (var e in events) s.TryAppend(e);

            var filter = new CoarseConeFilter(s);
            var m = new UtteranceMoment("m1", MomentKind.Interaction, "NPC", "PLAYER", "Scene", 100);
            var proj = filter.Project(m);

            var (shared, npcOnly, playerOnly) = Oracle(events, "NPC", "PLAYER");
            Assert.Equal(shared, new HashSet<string>(proj.SharedExperience));
            Assert.Equal(npcOnly, new HashSet<string>(proj.NpcOnly));
            Assert.Equal(playerOnly, new HashSet<string>(proj.PlayerOnly));
        }

        [Fact]
        public void Cone_ExcludesNonInvolvedEvents_AndStableTimeOrder()
        {
            var s = new InMemoryEventStream();
            foreach (var e in BuildConeScenario()) s.TryAppend(e);
            var proj = new CoarseConeFilter(s)
                .Project(new UtteranceMoment("m", MomentKind.Bark, "NPC", "PLAYER", null, 1));

            Assert.DoesNotContain("none", proj.SharedExperience.Concat(proj.NpcOnly).Concat(proj.PlayerOnly));
            Assert.Equal(new[] { "npcA", "npcB" }, proj.NpcOnly);  // 时间序稳定
        }

        [Fact]
        public void Cone_EmptyWhenNoCandidates()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Annotate("e1", new[] { "OTHER" }));
            var proj = new CoarseConeFilter(s)
                .Project(new UtteranceMoment("m", MomentKind.Interaction, "NPC", "PLAYER", null, 1));
            Assert.True(proj.IsEmpty);
        }

        // ================================================================
        // 回放流水线：日志 → 事件流 → 视锥（三区机械正确率 + 吞吐）
        // ================================================================

        [Fact]
        public void Replay_LogRoundTrip_PreservesEventsAndRegions()
        {
            var origin = new InMemoryEventStream();
            foreach (var e in BuildConeScenario()) origin.TryAppend(e);

            string log = EventStreamLog.ToLog(origin);
            var replayed = new InMemoryEventStream();
            int n = EventStreamLog.Replay(log, replayed);

            Assert.Equal(origin.TotalCount, n);
            Assert.Equal(origin.TotalCount, replayed.TotalCount);

            // 回放后三区结果应与原始流一致
            var a = new CoarseConeFilter(origin).Project(new UtteranceMoment("m", MomentKind.Interaction, "NPC", "PLAYER", null, 1));
            var b = new CoarseConeFilter(replayed).Project(new UtteranceMoment("m", MomentKind.Interaction, "NPC", "PLAYER", null, 1));
            Assert.Equal(a.SharedExperience, b.SharedExperience);
            Assert.Equal(a.NpcOnly, b.NpcOnly);
            Assert.Equal(a.PlayerOnly, b.PlayerOnly);
        }

        [Fact]
        public void Replay_SkipsCorruptLines_NeverThrows()
        {
            var log = EventStreamLog.ToLog(new InMemoryEventStream());
            var garbage = "not-json\n\n{\"eventId\":\"e1\",\"defName\":\"E\",\"actors\":[\"X\"]}\n";
            var s = new InMemoryEventStream();
            int n = EventStreamLog.Replay(garbage + log, s);
            Assert.Equal(1, n); // 仅合法行被摄入
        }

        [Fact]
        public void Replay_LargeBatch_ThroughputFloor()
        {
            // 吞吐回归护栏：20k 事件在极宽松上限内完成摄入 + 三区投影，捕捉病态性能回退（非精确计时断言）
            var s = new InMemoryEventStream();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20000; i++)
                s.TryAppend(Annotate("e" + i, new[] { "NPC", "PLAYER" }, tick: i));
            var proj = new CoarseConeFilter(s)
                .Project(new UtteranceMoment("m", MomentKind.Interaction, "NPC", "PLAYER", null, 1));
            sw.Stop();

            Assert.Equal(20000, s.TotalCount);
            Assert.Equal(20000, proj.SharedExperience.Count);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"摄入吞吐异常回退：{sw.ElapsedMilliseconds}ms");
        }
    }
}
