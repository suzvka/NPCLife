using System;
using System.Collections.Generic;
using NPCLife.Core;
using NPCLife.Tests.Helpers;
using Xunit;

namespace NPCLife.Tests.Core
{
    /// <summary>
    /// KnowledgeService 纯逻辑断言测试。
    /// 覆盖 Lookup（缓存 + 外部源聚合）、Store/Delete/List 代理行为与边界条件。
    /// </summary>
    public class KnowledgeServiceTests
    {
        private static KnowledgeService Create(
            IKnowledgeBase cache = null,
            IReadOnlyList<IExternalKnowledgeSource> externals = null)
        {
            return new KnowledgeService(
                cache ?? new FakeKnowledgeBase(),
                externals);
        }

        private static KnowledgeEntry MakeEntry(
            string term, string definition = "def",
            string source = "Test")
        {
            return new KnowledgeEntry
            {
                Term = term,
                Definition = definition,
                Source = source,
                ContextTags = new List<string>()
            };
        }

        // ================================================================
        // Lookup
        // ================================================================

        [Fact]
        public void Lookup_Empty_ReturnsEmpty()
        {
            var svc = Create();
            Assert.Empty(svc.Lookup(""));
            Assert.Empty(svc.Lookup(null));
        }

        [Fact]
        public void Lookup_CacheHit_ReturnsFromCache()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(MakeEntry("Raid", "attack", "Cache"));
            var svc = Create(cache);

            var results = svc.Lookup("Raid");
            Assert.Single(results);
            Assert.Equal("Cache", results[0].Source);
        }

        [Fact]
        public void Lookup_ExternalHit_ReturnsFromExternal()
        {
            var external = new FakeExternalSource("GameDef", MakeEntry("Raid", "external def", "GameDef"));
            var svc = Create(externals: new List<IExternalKnowledgeSource> { external });

            var results = svc.Lookup("Raid");
            Assert.Single(results);
            Assert.Equal("GameDef", results[0].Source);
        }

        [Fact]
        public void Lookup_CacheAndExternalHit_ReturnsBoth()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(MakeEntry("Raid", "cache def", "Cache"));
            var external = new FakeExternalSource("GameDef", MakeEntry("Raid", "external def", "GameDef"));
            var svc = Create(cache, new List<IExternalKnowledgeSource> { external });

            var results = svc.Lookup("Raid");
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Source == "Cache");
            Assert.Contains(results, r => r.Source == "GameDef");
        }

        [Fact]
        public void Lookup_MultipleExternals_AggregatesAll()
        {
            var ext1 = new FakeExternalSource("Source1", MakeEntry("Raid", "def1", "Source1"));
            var ext2 = new FakeExternalSource("Source2", MakeEntry("Raid", "def2", "Source2"));
            var svc = Create(externals: new List<IExternalKnowledgeSource> { ext1, ext2 });

            var results = svc.Lookup("Raid");
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void Lookup_NoHit_ReturnsEmpty()
        {
            var svc = Create();
            Assert.Empty(svc.Lookup("nonexistent"));
        }

        // ================================================================
        // Store / Delete / List* — delegates to cache
        // ================================================================

        [Fact]
        public void Store_DelegatesToCache()
        {
            var cache = new FakeKnowledgeBase();
            var svc = Create(cache);
            svc.Store(MakeEntry("Raid"));

            Assert.True(cache.TryLookup("Raid", out _));
        }

        [Fact]
        public void Delete_DelegatesToCache()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(MakeEntry("Raid"));
            var svc = Create(cache);

            svc.Delete("Raid");
            Assert.False(cache.TryLookup("Raid", out _));
        }

        [Fact]
        public void ListAll_DelegatesToCache()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(MakeEntry("A"));
            cache.Store(MakeEntry("B"));
            var svc = Create(cache);

            Assert.Equal(2, svc.ListAll().Count);
        }

        [Fact]
        public void ListByTags_DelegatesToCache()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(new KnowledgeEntry
            {
                Term = "Sword", Definition = "weapon", Source = "Test",
                ContextTags = new List<string> { "Weapon" }
            });
            cache.Store(new KnowledgeEntry
            {
                Term = "Meal", Definition = "food", Source = "Test",
                ContextTags = new List<string> { "Food" }
            });
            var svc = Create(cache);

            var weapons = svc.ListByTags(new List<string> { "Weapon" });
            Assert.Single(weapons);
        }

        [Fact]
        public void ListByPrefix_DelegatesToCache()
        {
            var cache = new FakeKnowledgeBase();
            cache.Store(MakeEntry("Apple"));
            cache.Store(MakeEntry("Avocado"));
            cache.Store(MakeEntry("Banana"));
            var svc = Create(cache);

            var results = svc.ListByPrefix("A");
            Assert.Equal(2, results.Count);
        }

        // ================================================================
        // Fake helpers
        // ================================================================

        private class FakeKnowledgeBase : IKnowledgeBase
        {
            private readonly Dictionary<string, KnowledgeEntry> _data
                = new Dictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

            public bool TryLookup(string term, out KnowledgeEntry entry)
            {
                entry = null;
                if (string.IsNullOrEmpty(term)) return false;
                return _data.TryGetValue(term, out entry);
            }

            public void Store(KnowledgeEntry entry)
            {
                if (entry?.Term == null) return;
                if (entry.ContextTags == null) entry.ContextTags = new List<string>();
                _data[entry.Term] = entry;
            }

            public void Delete(string term)
            {
                if (!string.IsNullOrEmpty(term)) _data.Remove(term);
            }

            public IReadOnlyList<KnowledgeEntry> ListAll() => new List<KnowledgeEntry>(_data.Values);
            public IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags)
            {
                if (tags == null || tags.Count == 0) return ListAll();
                var result = new List<KnowledgeEntry>();
                foreach (var e in _data.Values)
                {
                    if (e.ContextTags != null)
                        foreach (var t in tags)
                            if (e.ContextTags.Contains(t))
                            { result.Add(e); break; }
                }
                return result;
            }
            public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
            {
                if (string.IsNullOrEmpty(prefix)) return ListAll();
                var result = new List<KnowledgeEntry>();
                foreach (var e in _data.Values)
                    if (e.Term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add(e);
                return result;
            }
        }

        private class FakeExternalSource : IExternalKnowledgeSource
        {
            private readonly Dictionary<string, KnowledgeEntry> _data
                = new Dictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

            public string SourceName { get; }

            public FakeExternalSource(string name, params KnowledgeEntry[] entries)
            {
                SourceName = name;
                foreach (var e in entries) _data[e.Term] = e;
            }

            public IReadOnlyList<KnowledgeEntry> QueryExact(string term)
            {
                if (_data.TryGetValue(term ?? "", out var entry))
                    return new List<KnowledgeEntry> { entry };
                return Array.Empty<KnowledgeEntry>();
            }
        }
    }
}
