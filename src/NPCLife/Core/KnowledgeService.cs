using System;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 知识服务聚合器。组合内部缓存（IKnowledgeBase）与外部只读源（IExternalKnowledgeSource），
    /// 并行查询所有源，一次性返回同名词条的全部释义。
    /// Store / Delete 仅操作内部缓存，外部源不受影响。
    /// </summary>
    public class KnowledgeService
    {
        private readonly IKnowledgeBase _cache;
        private readonly IReadOnlyList<IExternalKnowledgeSource> _externals;

        public KnowledgeService(IKnowledgeBase cache, IReadOnlyList<IExternalKnowledgeSource> externals = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _externals = externals ?? Array.Empty<IExternalKnowledgeSource>();
        }

        /// <summary>
        /// 精确查询词条：并行查询内部缓存 + 所有外部源，返回全部命中结果。
        /// 若全部无命中则返回空列表。
        /// </summary>
        public IReadOnlyList<KnowledgeEntry> Lookup(string term)
        {
            if (string.IsNullOrEmpty(term))
                return Array.Empty<KnowledgeEntry>();

            var results = new List<KnowledgeEntry>();

            // 内部缓存
            if (_cache.TryLookup(term, out var cacheEntry))
                results.Add(cacheEntry);

            // 外部源
            foreach (var src in _externals)
            {
                var entries = src.QueryExact(term);
                if (entries != null && entries.Count > 0)
                    results.AddRange(entries);
            }

            return results;
        }

        /// <summary>存储知识到内部缓存。</summary>
        public void Store(KnowledgeEntry entry) => _cache.Store(entry);

        /// <summary>从内部缓存删除词条。</summary>
        public void Delete(string term) => _cache.Delete(term);

        /// <summary>列出内部缓存中的全部词条。</summary>
        public IReadOnlyList<KnowledgeEntry> ListAll() => _cache.ListAll();

        /// <summary>按语义标签筛选内部缓存中的词条。</summary>
        public IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags) => _cache.ListByTags(tags);

        /// <summary>按前缀列举内部缓存中的词条。</summary>
        public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix) => _cache.ListByPrefix(prefix);
    }
}
