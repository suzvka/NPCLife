using System.Collections.Generic;
using NPCLife.Core;
using NPCLife.Infrastructure.Knowledge;
using NPCLife.Skills;
using NPCLife.Tests.Helpers;
using Xunit;

namespace NPCLife.Tests.Core
{
    /// <summary>
    /// KnowledgeMcpProvider 断言测试。
    /// 通过直接调用 Provider 方法验证 5 个 MCP 工具的逻辑正确性。
    /// </summary>
    public class KnowledgeMcpProviderTests
    {
        private readonly FakeLogger _logger = new FakeLogger();
        private readonly FakeCacheStore _store = new FakeCacheStore();

        private KnowledgeMcpProvider CreateProvider(out BuiltInKnowledgeBase kb)
        {
            kb = new BuiltInKnowledgeBase(_store, _logger);
            var svc = new KnowledgeService(kb);
            return new KnowledgeMcpProvider(() => svc, _logger);
        }

        private KnowledgeMcpProvider CreateProvider(IKnowledgeService svc)
        {
            return new KnowledgeMcpProvider(() => svc, _logger);
        }

        // ================================================================
        // HookId / Metadata
        // ================================================================

        [Fact]
        public void Metadata_IsCorrect()
        {
            var provider = CreateProvider(out _);
            Assert.Equal("knowledge_management", provider.HookId);
            Assert.Equal(2, provider.GetTools().Count); // LookupTerm + LearnTerm
        }

        // ================================================================
        // LookupTerm
        // ================================================================

        [Fact]
        public void LookupTerm_Empty_ReturnsError()
        {
            var provider = CreateProvider(out _);
            var result = provider.LookupTerm("");
            Assert.Contains("\"error\"", result);
        }

        [Fact]
        public void LookupTerm_NotFound_ReturnsMiss()
        {
            var provider = CreateProvider(out _);
            var result = provider.LookupTerm("nonexistent");
            Assert.Contains("\"hit\":false", result);
        }

        [Fact]
        public void LookupTerm_Found_ReturnsHit()
        {
            var provider = CreateProvider(out var kb);
            kb.Store(new KnowledgeEntry
            {
                Term = "Raid", Definition = "An attack", Source = "Test"
            });

            var result = provider.LookupTerm("Raid");
            Assert.Contains("\"hit\":true", result);
            Assert.Contains("\"term\":\"Raid\"", result);
            Assert.Contains("\"definition\":\"An attack\"", result);
        }

        [Fact]
        public void LookupTerm_NullService_ReturnsUnavailable()
        {
            var provider = new KnowledgeMcpProvider(() => null, _logger);
            var result = provider.LookupTerm("test");
            Assert.Contains("unavailable", result);
        }

        // ================================================================
        // LearnTerm
        // ================================================================

        [Fact]
        public void LearnTerm_NewEntry_StoresAndReturnsHit()
        {
            var provider = CreateProvider(out var kb);
            var result = provider.LearnTerm("Raid", "An attack", "LLM");

            Assert.Contains("\"hit\":true", result);
            Assert.True(kb.TryLookup("Raid", out var entry));
            Assert.Equal("An attack", entry.Definition);
            Assert.Equal("LLM", entry.Source);
        }

        [Fact]
        public void LearnTerm_Existing_Overwrites()
        {
            var provider = CreateProvider(out var kb);
            provider.LearnTerm("Raid", "old");
            provider.LearnTerm("Raid", "new");

            Assert.True(kb.TryLookup("Raid", out var entry));
            Assert.Equal("new", entry.Definition);
        }

        [Fact]
        public void LearnTerm_EmptyTerm_ReturnsError()
        {
            var provider = CreateProvider(out _);
            var result = provider.LearnTerm("", "def");
            Assert.Contains("\"error\"", result);
        }

        // ================================================================
        // ListKnownTerms — removed in v1.1; listing is now a host-side concern.
        // If re-added as an MCP tool, reinstate the tests below.
        // ================================================================
    }
}
