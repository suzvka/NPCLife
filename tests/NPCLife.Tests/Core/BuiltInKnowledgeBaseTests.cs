using System.Collections.Generic;
using System.Linq;
using NPCLife.Core;
using NPCLife.Infrastructure.Knowledge;
using NPCLife.Tests.Helpers;
using Xunit;

namespace NPCLife.Tests.Core
{
    /// <summary>
    /// BuiltInKnowledgeBase 纯逻辑断言测试。
    /// 覆盖 CRUD、持久化往返、大小写不敏感、前缀/标签筛选与边界条件。
    /// </summary>
    public class BuiltInKnowledgeBaseTests
    {
        private static BuiltInKnowledgeBase Create(FakeCacheStore store = null, FakeLogger logger = null)
        {
            return new BuiltInKnowledgeBase(store ?? new FakeCacheStore(), logger ?? new FakeLogger());
        }

        private static KnowledgeEntry MakeEntry(
            string term, string definition = "def",
            string source = "Test")
        {
            return new KnowledgeEntry
            {
                Term = term,
                Definition = definition,
                Source = source
            };
        }

        // ================================================================
        // TryLookup
        // ================================================================

        [Fact]
        public void TryLookup_Empty_ReturnsFalse()
        {
            var kb = Create();
            Assert.False(kb.TryLookup("", out _));
            Assert.False(kb.TryLookup(null, out _));
        }

        [Fact]
        public void TryLookup_NotStored_ReturnsFalse()
        {
            var kb = Create();
            Assert.False(kb.TryLookup("nonexistent", out var entry));
            Assert.Null(entry);
        }

        [Fact]
        public void TryLookup_Stored_ReturnsTrue()
        {
            var kb = Create();
            kb.Store(MakeEntry("Raid", "An enemy attack"));

            Assert.True(kb.TryLookup("Raid", out var entry));
            Assert.Equal("Raid", entry.Term);
            Assert.Equal("An enemy attack", entry.Definition);
        }

        [Fact]
        public void TryLookup_CaseInsensitive()
        {
            var kb = Create();
            kb.Store(MakeEntry("Raid"));

            Assert.True(kb.TryLookup("raid", out _));
            Assert.True(kb.TryLookup("RAID", out _));
            Assert.True(kb.TryLookup("Raid", out _));
        }

        // ================================================================
        // Store
        // ================================================================

        [Fact]
        public void Store_NullOrEmpty_SilentlyIgnored()
        {
            var kb = Create();
            kb.Store(null);
            kb.Store(MakeEntry(""));
            kb.Store(MakeEntry(null));

            Assert.Empty(kb.ListAll());
        }

        [Fact]
        public void Store_OverwritesExisting()
        {
            var kb = Create();
            kb.Store(MakeEntry("Raid", "old definition"));
            kb.Store(MakeEntry("Raid", "new definition"));

            Assert.True(kb.TryLookup("Raid", out var entry));
            Assert.Equal("new definition", entry.Definition);
            Assert.Single(kb.ListAll());
        }

        // ================================================================
        // Delete
        // ================================================================

        [Fact]
        public void Delete_Existing_RemovesEntry()
        {
            var kb = Create();
            kb.Store(MakeEntry("Raid"));
            kb.Delete("Raid");

            Assert.False(kb.TryLookup("Raid", out _));
            Assert.Empty(kb.ListAll());
        }

        [Fact]
        public void Delete_Nonexistent_SilentlyReturns()
        {
            var kb = Create();
            kb.Delete("nonexistent"); // should not throw
            kb.Delete(null);
            kb.Delete("");
        }

        // ================================================================
        // ListAll
        // ================================================================

        [Fact]
        public void ListAll_Empty_ReturnsEmpty()
        {
            var kb = Create();
            Assert.Empty(kb.ListAll());
        }

        [Fact]
        public void ListAll_ReturnsSorted()
        {
            var kb = Create();
            kb.Store(MakeEntry("Zebra"));
            kb.Store(MakeEntry("Apple"));
            kb.Store(MakeEntry("Mango"));

            var all = kb.ListAll();
            Assert.Equal(3, all.Count);
            Assert.Equal("Apple", all[0].Term);
            Assert.Equal("Mango", all[1].Term);
            Assert.Equal("Zebra", all[2].Term);
        }

        // ================================================================
        // ListByPrefix
        // ================================================================

        [Fact]
        public void ListByPrefix_Empty_ReturnsAll()
        {
            var kb = Create();
            kb.Store(MakeEntry("A"));
            kb.Store(MakeEntry("B"));

            Assert.Equal(2, kb.ListByPrefix(null).Count);
            Assert.Equal(2, kb.ListByPrefix("").Count);
        }

        [Fact]
        public void ListByPrefix_MatchesCaseInsensitive()
        {
            var kb = Create();
            kb.Store(MakeEntry("心灵冲击"));
            kb.Store(MakeEntry("心灵低语"));
            kb.Store(MakeEntry("袭击"));

            var results = kb.ListByPrefix("心灵");
            Assert.Equal(2, results.Count);
            Assert.All(results, e => Assert.StartsWith("心灵", e.Term));
        }

        // ================================================================
        // Persistence Round-trip
        // ================================================================

        [Fact]
        public void Persistence_RoundTrip()
        {
            var store = new FakeCacheStore();
            var logger = new FakeLogger();

            // Write
            var kb1 = new BuiltInKnowledgeBase(store, logger);
            kb1.Store(MakeEntry("Raid", "An attack", "GameDef"));
            kb1.Store(MakeEntry("Blight", "Crop disease", "LLM"));

            // Read back from same store
            var kb2 = new BuiltInKnowledgeBase(store, logger);
            Assert.Equal(2, kb2.ListAll().Count);

            Assert.True(kb2.TryLookup("Raid", out var raid));
            Assert.Equal("An attack", raid.Definition);
            Assert.Equal("GameDef", raid.Source);

            Assert.True(kb2.TryLookup("Blight", out var blight));
            Assert.Equal("Crop disease", blight.Definition);
        }

        [Fact]
        public void Persistence_EmptyCache_NoError()
        {
            var store = new FakeCacheStore();
            var logger = new FakeLogger();

            // No data in store
            var kb = new BuiltInKnowledgeBase(store, logger);
            Assert.Empty(kb.ListAll());
            Assert.Empty(logger.Warnings);
        }
    }
}
