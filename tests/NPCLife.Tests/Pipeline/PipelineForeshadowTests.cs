using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Script;
using NPCLife.Pipeline;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NPCLife.Tests.Pipeline
{
    /// <summary>
    /// open-loops 机械清单（默认伏笔机制）+ 可选规划器钩子 + 装配注入 + 零路由 LLM 不变量。全程零模型、除写手外零 LLM。
    /// </summary>
    public class PipelineForeshadowTests
    {
        private const long Now = 100_000;

        private static AnnotatedEvent Make(string id, string[] actors, float importance, long tick = 0)
            => CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = new EventCardData { EventID = id, DefName = "E", Importance = importance, Payload = new Dictionary<string, string> { { "id", id } } },
                ActorIds = actors,
                GameTick = tick
            });

        private static UtteranceMoment Moment(string speaker = "NPC", string listener = "PLAYER", MomentKind kind = MomentKind.Interaction)
            => new UtteranceMoment("m", kind, speaker, listener, "Scene", Now);

        private static InMemoryEventStream Stream(params AnnotatedEvent[] evts)
        {
            var s = new InMemoryEventStream();
            foreach (var e in evts) s.TryAppend(e);
            return s;
        }

        private static MechanicalOpenLoopTracker Tracker(IEventStream s, IUtteranceMemory mem,
            IForeshadowPlanner planner = null, float minImp = 8f, int maxLoops = 4)
            => new MechanicalOpenLoopTracker(s, new CoarseConeFilter(s), mem, planner, minImp, maxLoops);

        // ================================================================
        // 机械清单：浮现 / 排除 / 闭合
        // ================================================================

        [Fact]
        public void Tracker_ListsUnresolvedHighImportance_InSpeakerCone()
        {
            var s = Stream(
                Make("big", new[] { "NPC", "PLAYER" }, 10f),   // K∩V，未提 → open
                Make("npcBig", new[] { "NPC" }, 9f),            // K\V，未提 → open
                Make("small", new[] { "NPC" }, 2f),             // 重要度不足 → 排除
                Make("playerBig", new[] { "PLAYER" }, 10f));    // V\K，NPC 不知 → 排除
            var loops = Tracker(s, new InMemoryUtteranceMemory()).OpenLoops(Moment());

            Assert.Equal(new[] { "big", "npcBig" }, loops.Select(l => l.EventId).OrderBy(x => x));
        }

        [Fact]
        public void Tracker_ClosedOnceMentionedBySpeaker()
        {
            var s = Stream(Make("big", new[] { "NPC", "PLAYER" }, 10f), Make("npcBig", new[] { "NPC" }, 9f));
            var mem = new InMemoryUtteranceMemory();
            mem.Record(new UtteranceRecord { SpeakerId = "NPC", ListenerId = "PLAYER", ReferencedEventIds = new[] { "big" } });

            var loops = Tracker(s, mem).OpenLoops(Moment());
            Assert.DoesNotContain("big", loops.Select(l => l.EventId));   // 已提及即闭合
            Assert.Contains("npcBig", loops.Select(l => l.EventId));
        }

        [Fact]
        public void Tracker_RespectsMaxLoops_AndSortsBySalienceDesc()
        {
            var s = Stream(
                Make("a", new[] { "NPC" }, 9f),
                Make("b", new[] { "NPC" }, 20f),
                Make("c", new[] { "NPC" }, 15f));
            var loops = Tracker(s, new InMemoryUtteranceMemory(), maxLoops: 2).OpenLoops(Moment());

            Assert.Equal(2, loops.Count);
            Assert.Equal("b", loops[0].EventId);   // 最重要者优先
            Assert.Equal("c", loops[1].EventId);
        }

        [Fact]
        public void Tracker_MergesOptionalPlanner_AndDedups()
        {
            var s = Stream(Make("inCone", new[] { "NPC" }, 10f));
            var planner = new FakePlanner(new[]
            {
                new OpenLoop(Make("inCone", new[] { "NPC" }, 10f), 1f, MaterialRegion.NpcOnly), // 与机械重复
                new OpenLoop(Make("crossScene", new[] { "NPC" }, 0f), 0.5f, MaterialRegion.NpcOnly), // 机械未见（低重要度）
            });
            var loops = Tracker(s, new InMemoryUtteranceMemory(), planner).OpenLoops(Moment());
            var ids = loops.Select(l => l.EventId).ToList();

            Assert.Contains("inCone", ids);
            Assert.Contains("crossScene", ids);           // 规划器补入跨场景线索
            Assert.Single(ids, x => x == "inCone"); // 去重
        }

        private sealed class FakePlanner : IForeshadowPlanner
        {
            private readonly IEnumerable<OpenLoop> _loops;
            public FakePlanner(IEnumerable<OpenLoop> loops) { _loops = loops; }
            public IEnumerable<OpenLoop> PlanOpenLoops(UtteranceMoment m) => _loops;
        }

        // ================================================================
        // 装配注入 + 写手输入
        // ================================================================

        [Fact]
        public void Assembler_FillsBagOpenLoops()
        {
            var s = Stream(Make("big", new[] { "NPC", "PLAYER" }, 10f));
            var mem = new InMemoryUtteranceMemory();
            var asm = new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(mem, null), null, mem,
                Tracker(s, mem));
            var bag = asm.Assemble(Moment());

            Assert.Contains("big", bag.OpenLoops.Select(l => l.EventId));
        }

        [Fact]
        public void WriterPrompt_IncludesOpenLoopsSection()
        {
            var bag = new MaterialBag(Moment(),
                new[] { new MaterialItem(Make("big", new[] { "NPC", "PLAYER" }, 10f), 0.9f, MaterialRegion.SharedExperience) },
                null, null, null, null, null,
                new[] { new OpenLoop(Make("npcBig", new[] { "NPC" }, 9f), 0.5f, MaterialRegion.NpcOnly) });
            var msg = MaterialBagPrompt.BuildUserMessage(bag);
            Assert.Contains("openLoops", msg);
            Assert.Contains("npcBig", msg);
        }

        [Fact]
        public void Assembler_NullProvider_YieldsEmptyOpenLoops_BagStillValid()
        {
            var s = Stream(Make("big", new[] { "NPC", "PLAYER" }, 10f));
            var asm = new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(null, null));
            var bag = asm.Assemble(Moment());
            Assert.Empty(bag.OpenLoops);   // 未接入 provider 时为空，不影响其余装配
        }

        // ================================================================
        // 零路由 LLM 不变量（除写手外全系统零 LLM 调用）
        // ================================================================

        private sealed class FakeLlm : ILlmService
        {
            public int CallCount;
            public Task<LlmResponse> ChatAsync(LlmRequest r, IReadOnlyList<LlmCredential> c, CancellationToken ct = default)
            {
                CallCount++;
                return Task.FromResult(new LlmResponse { Content = "[{\"s\":\"NPC\",\"t\":\"还记得那事。\"}]", FinishReason = "stop", UsageTotalTokens = 5 });
            }
            public Task<bool> TestConnectionAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string[]> ListModelsAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(new[] { "m" });
        }

        private sealed class FakeCreds : ICredentialStore
        {
            public IReadOnlyList<LlmCredential> GetActiveCredentials() =>
                new List<LlmCredential> { new LlmCredential { BaseUrl = "http://x", ApiKey = "k", ModelNames = new List<string> { "m" } } };
            public bool HasCredentials => true;
            public LlmCredential Resolve(string name, string model) => GetActiveCredentials()[0];
        }

        private static UtterancePipeline Pipeline(IEventStream s, FakeLlm llm, IUtteranceMemory mem)
        {
            var tracker = new MechanicalOpenLoopTracker(s, new CoarseConeFilter(s), mem, null, 8f, 4);
            var asm = new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(mem, null), null, mem, tracker);
            var writer = new LlmWriter(llm, new FakeCreds(), new WriterConfig { SystemPromptOverride = "SYS" });
            return new UtterancePipeline(asm, new SalienceTierGate(), writer, new TemplateTier0Renderer(), null, mem);
        }

        [Fact]
        public async Task Invariant_OpenLoopTrackingAndAssembly_UseNoLlm_OnlyWriterCalls()
        {
            var mem = new InMemoryUtteranceMemory();
            var full = Stream(Make("big", new[] { "NPC", "PLAYER" }, 10f));

            // 有材料+有 open-loops 的时刻：仅写手一次调用（路由/装配/追踪零 LLM）。
            var llm1 = new FakeLlm();
            var r1 = await Pipeline(full, llm1, mem).GenerateAsync(Moment());
            Assert.Equal(RenderTier.Full, r1.Tier);
            Assert.Equal(1, llm1.CallCount);
            Assert.Contains("big", r1.Bag.OpenLoops.Select(l => l.EventId));

            // 空袋时刻：零 LLM 调用（open-loop 追踪仍纯机械）。
            var mem2 = new InMemoryUtteranceMemory();
            var empty = Stream(Make("x", new[] { "OTHER" }, 10f));
            var llm2 = new FakeLlm();
            var r2 = await Pipeline(empty, llm2, mem2).GenerateAsync(Moment());
            Assert.Equal(RenderTier.Template, r2.Tier);
            Assert.Equal(0, llm2.CallCount);
        }
    }
}
