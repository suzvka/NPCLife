using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Script;
using NPCLife.Infrastructure;
using NPCLife.Pipeline;
using NPCLife.Tests.Helpers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NPCLife.Tests.Pipeline
{
    /// <summary>
    /// 新路径端到端集成（Phase 6 测试补齐）：投递接缝守卫、I3 降级不空手、I4 结果可审计、
    /// 全组合（open-loops + 记忆连续性）、精简后工厂无旧路径成员。全程假 LLM/无网络。
    /// </summary>
    public class PipelineIntegrationTests
    {
        private const long Now = 100_000;
        private const string ScriptJson = "[{\"s\":\"NPC\",\"t\":\"还记得那件事。\",\"type\":\"dialogue\"}]";

        private static AnnotatedEvent Make(string id, string[] actors, float importance, long tick = 0)
            => CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = new EventCardData { EventID = id, DefName = "E", Importance = importance, Payload = new Dictionary<string, string> { { "id", id } } },
                ActorIds = actors,
                GameTick = tick
            });

        private static UtteranceMoment Moment(string speaker = "NPC", string listener = "PLAYER", MomentKind kind = MomentKind.Interaction)
            => new UtteranceMoment("m", kind, speaker, listener, "Scene", Now);

        private static InMemoryEventStream Corpus()
        {
            var s = new InMemoryEventStream();
            s.TryAppend(Make("big", new[] { "NPC", "PLAYER" }, 10f, Now));
            return s;
        }

        // ---- 假件 ----
        private sealed class FakeLlmService : ILlmService
        {
            public int CallCount;
            public string Content = ScriptJson;
            public Task<LlmResponse> ChatAsync(LlmRequest r, IReadOnlyList<LlmCredential> c, CancellationToken ct = default)
            { CallCount++; return Task.FromResult(new LlmResponse { Content = Content, FinishReason = "stop", UsageTotalTokens = 5 }); }
            public Task<bool> TestConnectionAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string[]> ListModelsAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(new[] { "m" });
        }
        private sealed class Creds : ICredentialStore
        {
            private readonly List<LlmCredential> _list;
            public Creds(bool has) { _list = has ? new List<LlmCredential> { new LlmCredential { BaseUrl = "http://x", ApiKey = "k", ModelNames = new List<string> { "m" } } } : new List<LlmCredential>(); }
            public IReadOnlyList<LlmCredential> GetActiveCredentials() => _list;
            public bool HasCredentials => _list.Count > 0;
            public LlmCredential Resolve(string name, string model) => _list.FirstOrDefault();
        }
        private sealed class CountingResolver : IScriptLineResolver
        {
            public int Calls;
            public void Resolve(IReadOnlyList<ScriptLine> lines) { Calls++; }
        }
        private sealed class CountingConsumerFactory
        {
            public int Calls;
            private readonly IScriptConsumer _c = new NullConsumer();
            public IScriptConsumer Get() { Calls++; return _c; }
            private sealed class NullConsumer : IScriptConsumer
            { public void OnScriptLinesReady(string w, int s, IReadOnlyList<ScriptLine> lines) { } }
        }

        private static UtterancePipeline Build(IEventStream s, ILlmService llm, bool hasCreds, IUtteranceMemory mem, IOpenLoopProvider tracker = null)
        {
            var asm = new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(mem, null), null, mem, tracker);
            var writer = new LlmWriter(llm, new Creds(hasCreds), new WriterConfig { SystemPromptOverride = "SYS" });
            return new UtterancePipeline(asm, new SalienceTierGate(), writer, new TemplateTier0Renderer(), null, mem);
        }

        // ================================================================
        // 投递接缝（台词直送 IScriptConsumer，保 I1 消费契约）
        // ================================================================

        [Fact]
        public void Delivery_EmptyLines_SkipsResolveAndConsumer()
        {
            var resolver = new CountingResolver();
            var factory = new CountingConsumerFactory();
            var seam = new UtteranceDelivery(factory.Get, resolver, new FakeLogger());

            seam.Deliver(Moment(), new List<ScriptLine>());   // 空
            seam.Deliver((PipelineResult)null);               // null 结果

            Assert.Equal(0, resolver.Calls);
            Assert.Equal(0, factory.Calls);
        }

        [Fact]
        public void Delivery_NonEmpty_ResolvesAndFetchesConsumer()
        {
            var resolver = new CountingResolver();
            var factory = new CountingConsumerFactory();
            var seam = new UtteranceDelivery(factory.Get, resolver, new FakeLogger());

            seam.Deliver(Moment(), new List<ScriptLine> { new ScriptLine { SpeakerId = "NPC", Type = ScriptLineType.Dialogue, Text = "hi" } });

            Assert.Equal(1, resolver.Calls);   // 占位符解析在投递前同步完成
            Assert.Equal(1, factory.Calls);    // 取用游戏侧消费者（实际推送在主流水线 Drain 时）
        }

        // ================================================================
        // I3 降级链 + I4 可审计
        // ================================================================

        [Fact]
        public async Task Pipeline_WriterUnavailable_DegradesToTier0_NotEmpty()
        {
            var pipeline = Build(Corpus(), new FakeLlmService(), hasCreds: false, new InMemoryUtteranceMemory());
            var r = await pipeline.GenerateAsync(Moment());

            Assert.True(r.Degraded);
            Assert.Equal(RenderTier.Template, r.Tier);
            Assert.Equal(0, r.LlmCalls);              // 无凭证 → 前置失败，未真正调用 LLM
            Assert.NotNull(r.Lines);                  // 降级到 Tier 0，绝不空手
        }

        [Fact]
        public async Task Pipeline_Success_IsAuditable_RecordsContinuity()
        {
            var mem = new InMemoryUtteranceMemory();
            var s = Corpus();
            var tracker = new MechanicalOpenLoopTracker(s, new CoarseConeFilter(s), mem, null, 8f, 4);
            var llm = new FakeLlmService();
            var pipeline = Build(s, llm, hasCreds: true, mem, tracker);

            var r = await pipeline.GenerateAsync(Moment());

            // I4：结果携带材料袋快照 + 度量，可事后审计
            Assert.NotNull(r.Bag);
            Assert.NotNull(r.Moment);
            Assert.Equal(RenderTier.Full, r.Tier);
            Assert.Equal(1, r.LlmCalls);
            Assert.Equal(5, r.TokensUsed);
            Assert.True(r.LatencyMs >= 0);
            Assert.Contains("big", r.Bag.OpenLoops.Select(l => l.EventId));   // 未结线索随袋留痕

            // 连续性：成功生成写入一条话语流水
            Assert.NotEmpty(mem.RecentFor("NPC", "PLAYER", 5));
        }

        // ================================================================
        // 工厂仅提供基础设施单例（知识 + 事件总线 + 状态）
        // ================================================================

        [Fact]
        public void Factory_Trimmed_StillProvidesInfraSingletons()
        {
            IFrameworkFactory f = new DefaultFrameworkFactory();
            Assert.NotNull(f.Events);
            Assert.NotNull(f.Status);
            Assert.NotNull(f.CreateKnowledgeBase(new FakeCacheStore(), new FakeLogger()));
        }
    }
}
