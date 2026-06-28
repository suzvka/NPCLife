using System;
using System.Collections.Generic;
using System.Linq;
using NPCLife.Core;
using NPCLife.Framework;

namespace NPCLife.Infrastructure.Knowledge
{
    /// <summary>
    /// 内置知识库（内部缓存）。唯一可写的知识库实现，Store 直接覆盖。
    /// 通过 ICacheStore 持久化，无容量限制、无 LRU 淘汰。
    /// </summary>
    public class BuiltInKnowledgeBase : IKnowledgeBase
    {
        private readonly ILogger _logger;

        private const string StoreKey = "rimlife_knowledge";

        private readonly Dictionary<string, KnowledgeEntry> _entries
            = new Dictionary<string, KnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly ICacheStore _store;

        public BuiltInKnowledgeBase(ICacheStore store, ILogger logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            LoadFromStore();
        }

        // ================================================================
        // IKnowledgeBase
        // ================================================================

        /// <summary>
        /// 查询词条。O(1) 字典查找，大小写不敏感。
        /// </summary>
        public bool TryLookup(string term, out KnowledgeEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(term)) return false;
            return _entries.TryGetValue(term, out entry);
        }

        /// <summary>
        /// 存储词条。若 Term 已存在则直接覆盖。
        /// </summary>
        public void Store(KnowledgeEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Term)) return;

            _entries[entry.Term] = entry;
            SaveToStore();
        }

        /// <summary>
        /// 删除指定词条。
        /// </summary>
        public void Delete(string term)
        {
            if (string.IsNullOrEmpty(term)) return;
            if (_entries.Remove(term))
                SaveToStore();
        }

        /// <summary>
        /// 按前缀列举词条（大小写不敏感）。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return ListAll();

            return _entries.Values
                .Where(e => e.Term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Term)
                .ToList();
        }

        /// <summary>
        /// 列出全部词条。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> ListAll()
        {
            return _entries.Values.OrderBy(e => e.Term).ToList();
        }

        // ================================================================
        // 持久化
        // ================================================================

        private void LoadFromStore()
        {
            try
            {
                var json = _store.FetchCache<string>(StoreKey, null);
                if (string.IsNullOrEmpty(json) || json == "[]")
                    return;

                var dicts = JsonParser.ParseObjectArray(json);
                foreach (var dict in dicts)
                {
                    var entry = DeserializeEntry(dict);
                    if (entry != null && !string.IsNullOrEmpty(entry.Term))
                        _entries[entry.Term] = entry;
                }

                _logger.Message($"[NPCLife.Knowledge] Loaded {_entries.Count} knowledge entries from cache.");
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.Knowledge] Failed to load knowledge: {e.Message}");
            }
        }

        private void SaveToStore()
        {
            try
            {
                var entryJsons = new List<string>(_entries.Count);
                foreach (var entry in _entries.Values)
                    entryJsons.Add(SerializeEntry(entry));

                var sb = new System.Text.StringBuilder(entryJsons.Count * 128 + 4);
                sb.Append('[');
                for (int i = 0; i < entryJsons.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(entryJsons[i]);
                }
                sb.Append(']');

                _store.Cache(StoreKey, sb.ToString());
            }
            catch (Exception e)
            {
                _logger.Warning($"[NPCLife.Knowledge] Failed to save knowledge: {e.Message}");
            }
        }

        // ================================================================
        // 序列化
        // ================================================================

        private static string SerializeEntry(KnowledgeEntry entry)
        {
            var w = new JsonWriter(256);
            w.Prop("term", entry.Term ?? "");
            w.Prop("definition", entry.Definition ?? "");
            w.Prop("source", entry.Source ?? "");

            return w.Close();
        }

        private static KnowledgeEntry DeserializeEntry(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0) return null;

            var entry = new KnowledgeEntry
            {
                Term = data.TryGetValue("term", out var v) ? v : null,
                Definition = data.TryGetValue("definition", out v) ? v : "",
                Source = data.TryGetValue("source", out v) ? v : "LegacyCache"
            };

            return entry;
        }
    }
}
