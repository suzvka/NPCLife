using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 工作空间内部事件池。实现 IEventLog。
    ///
    /// 双层结构：
    /// - pending 缓冲区：存储在 WorkspaceState 的 KV 字段中（EventCache/PendingEventIds/PendingImportance），
    ///   随 WorkspaceState 持久化到存档。Agent drain 时清空。
    /// - recent 历史缓冲：仅维护在内存中（零持久化），保留近期事件供 Query/GetById 查询。
    ///
    /// 阈值检测在每次 Append 后评估，达到时触发 OnThresholdReached。
    ///
    /// 去重机制：基于内容指纹（DefName + Tick + sorted Payload）自动拦截重复事件。
    /// 同一指纹在 pending 缓冲区内只允许存在一次，DrainPending 后指纹集合清空。
    /// </summary>
    internal class WorkspaceEventPool : IEventLog
    {
        private readonly WorkspaceState _ws;
        private readonly DriverConfig _config;
        private readonly ICardSerializer _serializer;

        private readonly List<IGameEvent> _recent = new List<IGameEvent>();
        private readonly HashSet<string> _pendingFingerprints = new HashSet<string>();
        private int _totalAppended;

        public event Action OnThresholdReached;

        public WorkspaceEventPool(
            WorkspaceState ws,
            DriverConfig config,
            ICardSerializer serializer)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
            _config = config ?? DriverConfig.CreateDefault();
            _serializer = serializer ?? CardSerializer.Default;

            if (_ws.EventCache == null) _ws.EventCache = new Dictionary<string, string>();
            if (_ws.PendingEventIds == null) _ws.PendingEventIds = new List<string>();
            _pendingFingerprints.Clear();
        }

        // ================================================================
        // IEventLog: 写入
        // ================================================================

        public void Append(IGameEvent evt)
        {
            if (evt == null) return;

            // 内容指纹去重：同一指纹在 pending 缓冲区内只允许存在一次
            string fingerprint = ComputeFingerprint(evt);
            if (!_pendingFingerprints.Add(fingerprint))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[NPCLife.EventPool] Duplicate event skipped: fingerprint={fingerprint}");
                return;
            }

            // 写入 pending 缓冲区（持久化到 WorkspaceState）
            string eventJson = _serializer.SerializeEvent(evt);
            _ws.EventCache[evt.EventID] = eventJson;
            _ws.PendingEventIds.Add(evt.EventID);
            _ws.PendingImportance += evt.Importance;

            // 写入 recent 历史缓冲（仅内存）
            _recent.Add(evt);
            while (_recent.Count > _config.RecentHistoryCapacity)
            {
                int removeIdx = 0;
                float minImportance = _recent[0].Importance;
                for (int i = 1; i < _recent.Count; i++)
                {
                    if (_recent[i].Importance < minImportance)
                    {
                        minImportance = _recent[i].Importance;
                        removeIdx = i;
                    }
                }
                _recent.RemoveAt(removeIdx);
            }

            _totalAppended++;

            CheckThreshold();
        }

        private void CheckThreshold()
        {
            int effectiveCount = _config.GetEffectiveCountThreshold(_ws.CreatedByRole);
            float effectiveImportance = _config.GetEffectiveImportanceThreshold(_ws.CreatedByRole);
            if (PendingCount >= effectiveCount
                || TotalImportance >= effectiveImportance)
            {
                OnThresholdReached?.Invoke();
            }
        }

        // ================================================================
        // IEventLog: 查询（查 recent 历史缓冲）
        // ================================================================

        public IReadOnlyList<IGameEvent> Query(EventQuery query)
        {
            if (query == null) query = EventQuery.All;

            IEnumerable<IGameEvent> result = _recent;

            if (query.SinceTick.HasValue)
                result = result.Where(e => e.Tick >= query.SinceTick.Value);
            if (query.UntilTick.HasValue)
                result = result.Where(e => e.Tick < query.UntilTick.Value);
            if (!string.IsNullOrEmpty(query.ActorId))
                result = result.Where(e => e.Actors != null && e.Actors.Any(a => a.ID == query.ActorId));
            if (query.MinImportance.HasValue)
                result = result.Where(e => e.Importance >= query.MinImportance.Value);

            result = result.OrderBy(e => e.Tick);

            int offset = query.Offset ?? 0;
            if (offset > 0)
                result = result.Skip(offset);
            if (query.Limit.HasValue)
                result = result.Take(query.Limit.Value);

            return result.ToList();
        }

        public int Count(EventQuery query)
        {
            if (query == null) return _recent.Count;
            var q = new EventQuery
            {
                SinceTick = query.SinceTick,
                UntilTick = query.UntilTick,
                ActorId = query.ActorId,
                MinImportance = query.MinImportance,
                Limit = null,
                Offset = null
            };
            return Query(q).Count;
        }

        public IGameEvent Latest => _recent.Count > 0 ? _recent[_recent.Count - 1] : null;

        public IGameEvent GetById(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_recent[i].EventID, eventId, StringComparison.Ordinal))
                    return _recent[i];
            }
            return null;
        }

        public int TotalAppended => _totalAppended;

        public string WorkspaceId => _ws.Id;

        // ================================================================
        // IEventLog: 池激活语义（操作 WorkspaceState 的 pending 字段）
        // ================================================================

        public int PendingCount => _ws.PendingEventIds?.Count ?? 0;

        public bool IsPendingEmpty => PendingCount == 0;

        public float TotalImportance => _ws.PendingImportance;

        public IReadOnlyList<IGameEvent> DrainPending()
        {
            var events = new List<IGameEvent>();
            if (_ws.PendingEventIds != null && _ws.EventCache != null)
            {
                foreach (var eventId in _ws.PendingEventIds)
                {
                    if (_ws.EventCache.TryGetValue(eventId, out var json))
                    {
                        var evt = _serializer.DeserializeEvent(json);
                        if (evt != null) events.Add(evt);
                    }
                }
                _ws.PendingEventIds.Clear();
            }
            _ws.PendingImportance = 0;
            _pendingFingerprints.Clear();
            return events;
        }

        // ================================================================
        // 内容指纹计算
        // ================================================================

        /// <summary>
        /// 计算事件的内容指纹，用于去重。
        /// 指纹 = DefName + Tick + sorted Payload KV pairs。
        /// </summary>
        private static string ComputeFingerprint(IGameEvent evt)
        {
            var sb = new StringBuilder();
            sb.Append(evt.DefName ?? "").Append('|').Append(evt.Tick);
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                foreach (var kv in evt.Payload.OrderBy(kv => kv.Key))
                    sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value ?? "");
            }
            return sb.ToString();
        }
    }
}
