using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 事件流的内存实现。线程模型沿用框架约定：
    /// 由宿主在主线程（或 MainThreadDispatcher 序列化后）单线访问，不加锁。
    ///
    /// 索引结构：
    /// - _byId：EventId → 事件（幂等去重 + 精确查找）；
    /// - _events：写入顺序列表（时间序，全扫描降级路径 & Offset/Limit 基准）；
    /// - _byActor / _byLocation / _byFingerprint / _byProvenance：值 → 在 _events 中的下标列表（升序）。
    ///
    /// 索引缺失（无对应过滤键）时退化为 _events 全扫描——仅性能损失，结果不变。
    /// </summary>
    public sealed class InMemoryEventStream : IEventStream
    {
        private readonly List<AnnotatedEvent> _events = new List<AnnotatedEvent>();
        private readonly Dictionary<string, int> _byId = new Dictionary<string, int>();
        private readonly Dictionary<string, List<int>> _byActor = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, List<int>> _byLocation = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, List<int>> _byFingerprint = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, List<int>> _byProvenance = new Dictionary<string, List<int>>();

        public int TotalCount => _events.Count;

        public AnnotatedEvent Latest =>
            _events.Count > 0 ? _events[_events.Count - 1] : null;

        public bool TryAppend(AnnotatedEvent evt)
        {
            if (evt == null || evt.Raw == null) return false;
            string id = evt.EventId;
            // 无 EventId 的事件无法参与幂等去重/精确查找——采集层应保证 Raw 非空，
            // 但空 ID 仍写入（不拒绝摄入），只是不进 _byId 索引。
            if (!string.IsNullOrEmpty(id) && _byId.ContainsKey(id))
                return false; // 幂等：同一事件已写入

            int index = _events.Count;
            _events.Add(evt);
            if (!string.IsNullOrEmpty(id))
                _byId[id] = index;

            if (evt.ActorIds != null)
                foreach (var actor in evt.ActorIds)
                    if (!string.IsNullOrEmpty(actor))
                        AddToIndex(_byActor, actor, index);

            AddToIndex(_byLocation, evt.LocationKey, index);
            AddToIndex(_byFingerprint, evt.Fingerprint, index);
            AddToIndex(_byProvenance, evt.Provenance, index);
            return true;
        }

        public AnnotatedEvent GetById(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            return _byId.TryGetValue(eventId, out var idx) ? _events[idx] : null;
        }

        public IReadOnlyList<AnnotatedEvent> Query(StreamQuery query)
        {
            if (query == null) query = StreamQuery.All;

            var candidates = ResolveCandidates(query);
            var result = new List<AnnotatedEvent>();
            foreach (int idx in candidates)
            {
                if (Matches(_events[idx], query)) result.Add(_events[idx]);
            }

            if (query.Offset > 0)
            {
                if (query.Offset >= result.Count) return System.Array.Empty<AnnotatedEvent>();
                result.RemoveRange(0, query.Offset);
            }
            if (query.Limit > 0 && result.Count > query.Limit)
                result.RemoveRange(query.Limit, result.Count - query.Limit);
            return result;
        }

        public int Count(StreamQuery query)
        {
            if (query == null) return _events.Count;
            int n = 0;
            foreach (int idx in ResolveCandidates(query))
                if (Matches(_events[idx], query)) n++;
            return n;
        }

        // ================================================================
        // 内部：索引选择 + 谓词过滤
        // ================================================================

        /// <summary>
        /// 选取最具选择性的单一索引作为候选集（下标升序，保证时间序输出）；
        /// 无任何索引键命中时返回全部下标（全扫描降级）。
        /// </summary>
        private List<int> ResolveCandidates(StreamQuery q)
        {
            if (!string.IsNullOrEmpty(q.ActorId) && _byActor.TryGetValue(q.ActorId, out var byActor))
                return byActor;
            if (!string.IsNullOrEmpty(q.LocationKey) && _byLocation.TryGetValue(q.LocationKey, out var byLoc))
                return byLoc;
            if (!string.IsNullOrEmpty(q.Fingerprint) && _byFingerprint.TryGetValue(q.Fingerprint, out var byFp))
                return byFp;
            if (!string.IsNullOrEmpty(q.Provenance) && _byProvenance.TryGetValue(q.Provenance, out var byProv))
                return byProv;

            // 全扫描降级：返回升序全部下标
            var all = new List<int>(_events.Count);
            for (int i = 0; i < _events.Count; i++) all.Add(i);
            return all;
        }

        private static bool Matches(AnnotatedEvent e, StreamQuery q)
        {
            if (!string.IsNullOrEmpty(q.ActorId)
                && (e.ActorIds == null || IndexOf(e.ActorIds, q.ActorId) < 0))
                return false;
            if (!string.IsNullOrEmpty(q.LocationKey)
                && !System.StringComparer.Ordinal.Equals(e.LocationKey, q.LocationKey))
                return false;
            if (!string.IsNullOrEmpty(q.Fingerprint)
                && !System.StringComparer.Ordinal.Equals(e.Fingerprint, q.Fingerprint))
                return false;
            if (!string.IsNullOrEmpty(q.Provenance)
                && !System.StringComparer.Ordinal.Equals(e.Provenance, q.Provenance))
                return false;
            if (q.TickFrom != long.MinValue && e.GameTick < q.TickFrom) return false;
            if (q.TickTo != long.MinValue && e.GameTick > q.TickTo) return false;
            return true;
        }

        private static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (System.StringComparer.Ordinal.Equals(list[i], value)) return i;
            return -1;
        }

        private static void AddToIndex(Dictionary<string, List<int>> index, string key, int position)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<int>();
                index[key] = list;
            }
            list.Add(position); // 追加即升序
        }
    }
}
