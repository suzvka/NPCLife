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
    /// 写手 + Tier 渲染 + 话语管线门面测试。用假 LLM 服务端到端驱动管线，
    /// 验证：单次生成契约、Tier 判定（保守默认）、Tier0 兜底、失败降级不空手、成本 ∝ 消费度量。零真实网络。
    /// </summary>
    public class PipelineWriterTests
    {
        private const long Now = 100_000;

        // ================================================================
        // 假基础设施
        // ================================================================

        private sealed class FakeCredentialStore : ICredentialStore
        {
            private readonly List<LlmCredential> _creds;
            public FakeCredentialStore(bool provide)
            {
                _creds = provide ? new List<LlmCredential>
                {
                    new LlmCredential { BaseUrl = "http://x", ApiKey = "k", ModelNames = new List<string> { "test-model" } }
                } : new List<LlmCredential>();
            }
            public IReadOnlyList<LlmCredential> GetActiveCredentials() => _creds;
            public bool HasCredentials => _creds.Count > 0;
            public LlmCredential Resolve(string credentialName, string modelName) => _creds.FirstOrDefault();
        }

        private sealed class FakeLlmService : ILlmService
        {
            public string ContentToReturn = "[]";
            public string FailWith; // 非空则返回错误响应
            public int CallCount;

            public Task<LlmResponse> ChatAsync(LlmRequest request, IReadOnlyList<LlmCredential> credentials, CancellationToken ct = default)
            {
                CallCount++;
                if (!string.IsNullOrEmpty(FailWith))
                    return Task.FromResult(LlmResponse.FromError(FailWith));
                return Task.FromResult(new LlmResponse
                {
                    Content = ContentToReturn,
                    FinishReason = "stop",
                    UsageInputTokens = 100,
                    UsageOutputTokens = 20,
                    UsageTotalTokens = 120
                });
            }
            public Task<bool> TestConnectionAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string[]> ListModelsAsync(LlmCredential c, CancellationToken ct = default) => Task.FromResult(new[] { "test-model" });
        }

        // ================================================================
        // 脚手架
        // ================================================================

        private static AnnotatedEvent Make(string id, string[] actors, long tick, float importance = 5f)
            => CaptureLayer.Annotate(new EventCaptureContext
            {
                Raw = new EventCardData { EventID = id, DefName = "E", Importance = importance, Payload = new Dictionary<string, string> { { "id", id } } },
                ActorIds = actors,
                GameTick = tick
            });

        private static UtteranceMoment Moment(MomentKind kind = MomentKind.Interaction, string speaker = "NPC", string listener = "PLAYER")
            => new UtteranceMoment("m", kind, speaker, listener, "Scene", Now);

        private static InMemoryEventStream Stream(params AnnotatedEvent[] evts)
        {
            var s = new InMemoryEventStream();
            foreach (var e in evts) s.TryAppend(e);
            return s;
        }

        private static UtterancePipeline BuildPipeline(IEventStream stream, FakeLlmService llm, bool creds = true,
            IUtteranceMemory memory = null, ITier1Cache cache = null, SalienceConfig cfg = null)
        {
            memory = memory ?? new InMemoryUtteranceMemory();
            var disc = new DeterministicSalienceDiscriminator(memory, cfg);
            var asm = new MaterialAssembler(stream, new CoarseConeFilter(stream), disc, cfg, memory);
            var writer = new LlmWriter(llm, new FakeCredentialStore(creds), new WriterConfig { SystemPromptOverride = "SYS" });
            return new UtterancePipeline(asm, new SalienceTierGate(), writer, new TemplateTier0Renderer(), cache, memory);
        }

        private const string ScriptJson = "[{\"s\":\"NPC\",\"t\":\"你听说了吗，北边遭袭了。\",\"d\":0,\"type\":\"dialogue\"},{\"s\":\"NPC\",\"t\":\"我还记得上次那一战。\",\"type\":\"dialogue\"}]";

        // ================================================================
        // 写手契约
        // ================================================================

        [Fact]
        public async Task Writer_HappyPath_ParsesScriptAndCostsOneCall()
        {
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var writer = new LlmWriter(llm, new FakeCredentialStore(true), new WriterConfig { SystemPromptOverride = "SYS" });
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var bag = new MaterialAssembler(s, new CoarseConeFilter(s),
                new DeterministicSalienceDiscriminator(null, null)).Assemble(Moment());

            var r = await writer.WriteAsync(bag);

            Assert.True(r.Success);
            Assert.True(r.AttemptedLlm);
            Assert.Equal(1, llm.CallCount);           // 单次调用（非多轮）
            Assert.Equal(2, r.Lines.Count);
            Assert.Equal("NPC", r.Lines[0].SpeakerId);
            Assert.Equal(120, r.TotalTokens);
        }

        [Fact]
        public async Task Writer_NoCredential_FailsWithoutLlmCall()
        {
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var writer = new LlmWriter(llm, new FakeCredentialStore(false), null);
            var r = await writer.WriteAsync(new MaterialBag(Moment(), null, null, null, null));

            Assert.False(r.Success);
            Assert.False(r.AttemptedLlm);            // 前置失败，未产生成本
            Assert.Equal(0, llm.CallCount);
        }

        [Fact]
        public async Task Writer_UnparsableOutput_Fails()
        {
            var llm = new FakeLlmService { ContentToReturn = "对不起，我无法完成。" };
            var writer = new LlmWriter(llm, new FakeCredentialStore(true), null);
            var r = await writer.WriteAsync(new MaterialBag(Moment(), null, null, null, null));

            Assert.False(r.Success);
            Assert.True(r.AttemptedLlm);            // 调用了但结果不可解析
        }

        [Fact]
        public async Task Writer_ApiError_Fails()
        {
            var llm = new FakeLlmService { FailWith = "rate limited" };
            var writer = new LlmWriter(llm, new FakeCredentialStore(true), null);
            var r = await writer.WriteAsync(new MaterialBag(Moment(), null, null, null, null));

            Assert.False(r.Success);
            Assert.Equal("rate limited", r.Error);
        }

        // ================================================================
        // Tier 判定（保守默认）
        // ================================================================

        [Fact]
        public void TierGate_EmptyBag_IsTemplate()
            => Assert.Equal(RenderTier.Template, new SalienceTierGate().Decide(new MaterialBag(Moment(), null, null, null, null)));

        [Fact]
        public void TierGate_InteractionWithMaterial_IsFull()
        {
            var bag = new MaterialBag(Moment(), new[] { new MaterialItem(Make("e", new[] { "NPC", "PLAYER" }, Now), 0.9f, MaterialRegion.SharedExperience) },
                null, null, null);
            Assert.Equal(RenderTier.Full, new SalienceTierGate().Decide(bag));
        }

        [Fact]
        public void TierGate_AmbientLowSalience_IsTemplate_ButHighSalience_IsFull()
        {
            var gate = new SalienceTierGate(ambientSalienceThreshold: 0.2f);
            var low = new MaterialBag(Moment(MomentKind.AmbientPulse),
                new[] { new MaterialItem(Make("e", new[] { "NPC" }, Now), 0.1f, MaterialRegion.NpcOnly) }, null, null, null);
            var high = new MaterialBag(Moment(MomentKind.AmbientPulse),
                new[] { new MaterialItem(Make("e", new[] { "NPC" }, Now), 0.8f, MaterialRegion.NpcOnly) }, null, null, null);
            Assert.Equal(RenderTier.Template, gate.Decide(low));
            Assert.Equal(RenderTier.Full, gate.Decide(high));
        }

        // ================================================================
        // Tier 0 模板兜底
        // ================================================================

        [Fact]
        public void Tier0_ProducesNonEmptyDeterministicLine()
        {
            var r = new TemplateTier0Renderer();
            var bag = new MaterialBag(Moment(MomentKind.AmbientPulse, "NPC"), null, null, null, null);
            var a = r.Render(bag);
            var b = r.Render(bag);
            Assert.Single(a);
            Assert.Equal("NPC", a[0].SpeakerId);
            Assert.False(string.IsNullOrEmpty(a[0].Text));
            Assert.Equal(a[0].Text, b[0].Text);   // 确定性（同输入同输出）
        }

        // ================================================================
        // 管线端到端 + 降级 + 成本
        // ================================================================

        [Fact]
        public async Task Pipeline_HappyPath_EndToEnd()
        {
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now), Make("n1", new[] { "NPC" }, Now));
            var pipe = BuildPipeline(s, llm);

            var r = await pipe.GenerateAsync(Moment());

            Assert.Equal(RenderTier.Full, r.Tier);
            Assert.Equal(1, r.LlmCalls);
            Assert.False(r.Degraded);
            Assert.Equal(2, r.Lines.Count);
            Assert.Equal(120, r.TokensUsed);
            Assert.True(r.Bag.Count >= 1);
        }

        [Fact]
        public async Task Pipeline_EmptyCone_Tier0ZeroCost()
        {
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var s = Stream(Make("x1", new[] { "OTHER" }, Now)); // NPC/PLAYER 都不参与 → 空袋
            var r = await BuildPipeline(s, llm).GenerateAsync(Moment());

            Assert.Equal(RenderTier.Template, r.Tier);
            Assert.Equal(0, r.LlmCalls);            // 无料不生成（P7：无消费结账则零成本）
            Assert.NotEmpty(r.Lines);               // 仍有兜底台词（I2 不断流）
            Assert.Equal(0, llm.CallCount);
        }

        [Fact]
        public async Task Pipeline_WriterFails_DegradesToTier0_NeverEmpty()
        {
            var llm = new FakeLlmService { FailWith = "boom" };
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var r = await BuildPipeline(s, llm).GenerateAsync(Moment());

            Assert.True(r.Degraded);
            Assert.Equal(RenderTier.Template, r.Tier);
            Assert.Equal(1, r.LlmCalls);            // 实际发起了一次（失败）
            Assert.NotEmpty(r.Lines);               // 降级仍产出台词
            Assert.Equal("boom", r.Error);
        }

        [Fact]
        public async Task Pipeline_NoCredential_DegradesWithZeroCost()
        {
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var r = await BuildPipeline(s, llm, creds: false).GenerateAsync(Moment());

            Assert.True(r.Degraded);
            Assert.Equal(0, r.LlmCalls);            // 前置失败未产生成本
            Assert.NotEmpty(r.Lines);
            Assert.Equal(0, llm.CallCount);
        }

        [Fact]
        public async Task Pipeline_Tier1CacheHit_SkipsLlm()
        {
            var cache = new FakeTier1Cache(ScriptFormat.Parse(ScriptJson));
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var pipe = BuildPipeline(s, llm, cache: cache);

            var r = await pipe.GenerateAsync(Moment());

            Assert.Equal(RenderTier.Cached, r.Tier);
            Assert.Equal(0, r.LlmCalls);
            Assert.Equal(0, llm.CallCount);         // 命中缓存省一次 LLM
        }

        private sealed class FakeTier1Cache : ITier1Cache
        {
            private readonly IReadOnlyList<ScriptLine> _lines;
            public FakeTier1Cache(IReadOnlyList<ScriptLine> lines) { _lines = lines; }
            public bool TryGet(MaterialBag bag, out IReadOnlyList<ScriptLine> lines) { lines = _lines; return true; }
            public void Store(MaterialBag bag, IReadOnlyList<ScriptLine> lines) { }
        }

        [Fact]
        public async Task Pipeline_RecordsContinuity_WhenMemoryWired()
        {
            var mem = new InMemoryUtteranceMemory();
            var llm = new FakeLlmService { ContentToReturn = ScriptJson };
            var s = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var pipe = BuildPipeline(s, llm, memory: mem);

            await pipe.GenerateAsync(Moment());

            Assert.Single(mem.RecentFor("NPC", "PLAYER", 5)); // 成功生成记入连续性
        }

        // ================================================================
        // 成本度量
        // ================================================================

        [Fact]
        public async Task Metrics_AvgLlmCallsPerUtterance_CapsAtOne_BelowHalfOfLegacy()
        {
            // 每次话语 ≤1 次调用，且空袋/缓存/前置失败为 0。混合三类，验证平均 ≤1。
            // 旧式双 LLM 多轮管线每消费 ≥2 次，故新路径应 ≤ 其 50%。
            var metrics = new GenerationMetrics();
            var llmOk = new FakeLlmService { ContentToReturn = ScriptJson };
            var full = Stream(Make("s1", new[] { "NPC", "PLAYER" }, Now));
            var empty = Stream(Make("x1", new[] { "OTHER" }, Now));

            metrics.Record(await BuildPipeline(full, llmOk).GenerateAsync(Moment()));      // 1
            metrics.Record(await BuildPipeline(empty, llmOk).GenerateAsync(Moment()));     // 0
            metrics.Record(await BuildPipeline(full, llmOk).GenerateAsync(Moment()));      // 1

            Assert.Equal(3, metrics.Utterances);
            Assert.Equal(2, metrics.TotalLlmCalls);
            const double legacyPerUtterance = 2.0; // 旧路径每次消费 ≥2 层（多轮更多）
            Assert.True(metrics.AvgLlmCallsPerUtterance <= legacyPerUtterance * 0.5 + 1e-9,
                $"平均调用 {metrics.AvgLlmCallsPerUtterance} 应 ≤ 旧路径({legacyPerUtterance}) 的 50%");
        }
    }
}
