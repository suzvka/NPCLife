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
    /// 单一事件池结构：
    /// - EventCache：所有事件的持久存储（KV），事件写入后长期保留，供 Query/GetById/route_events 查询。
    /// - PendingEventIds + PendingImportance：跟踪待处理事件，Agent drain 时清空。
    ///
    /// 阈值检测在每次 Append 后评估，达到时触发 OnThresholdReached。
    ///
    /// 去重机制：基于内容指纹（DefName + sorted Payload）自动拦截重复事件。
    /// 同一指纹在 pending 缓冲区内只允许存在一次，DrainPending 后指纹集合清空。
    /// </summary>
    internal class WorkspaceEventPool : IEventLog
    {
        private readonly WorkspaceState _ws;
        private readonly DriverConfig _config;
        private readonly ICardSerializer _serializer;

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

            // 写入事件池（持久化到 WorkspaceState）
            string eventJson = _serializer.SerializeEvent(evt);
            _ws.EventCache[evt.EventID] = eventJson;
            _ws.PendingEventIds.Add(evt.EventID);
            _ws.PendingImportance += evt.Importance;

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
        // IEventLog: 查询（从 EventCache 读取）
        // ================================================================

        public IReadOnlyList<IGameEvent> Query(EventQuery query)
        {
            if (query == null) query = EventQuery.All;

            IEnumerable<IGameEvent> result = DeserializeAllCached();

            if (!string.IsNullOrEmpty(query.ActorId))
                result = result.Where(e => e.Actors != null && e.Actors.Any(a => a.ID == query.ActorId));
            if (query.MinImportance.HasValue)
                result = result.Where(e => e.Importance >= query.MinImportance.Value);

            int offset = query.Offset ?? 0;
            if (offset > 0)
                result = result.Skip(offset);
            if (query.Limit.HasValue)
                result = result.Take(query.Limit.Value);

            return result.ToList();
        }

        public int Count(EventQuery query)
        {
            if (query == null) return _ws.EventCache?.Count ?? 0;
            var q = new EventQuery
            {
                ActorId = query.ActorId,
                MinImportance = query.MinImportance,
                Limit = null,
                Offset = null
            };
            return Query(q).Count;
        }

        public IGameEvent Latest
        {
            get
            {
                if (_ws.EventCache == null || _ws.EventCache.Count == 0) return null;
                // EventCache 是 Dictionary，无顺序保证；取 PendingEventIds 最后一条或全量最后一条
                var keys = _ws.EventCache.Keys;
                string lastKey = null;
                foreach (var k in keys) lastKey = k;
                return lastKey != null && _ws.EventCache.TryGetValue(lastKey, out var json)
                    ? _serializer.DeserializeEvent(json) : null;
            }
        }

        public IGameEvent GetById(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            if (_ws.EventCache != null && _ws.EventCache.TryGetValue(eventId, out var json))
                return _serializer.DeserializeEvent(json);
            return null;
        }

        /// <summary>从 EventCache 反序列化所有事件。</summary>
        private IEnumerable<IGameEvent> DeserializeAllCached()
        {
            if (_ws.EventCache == null) yield break;
            foreach (var kv in _ws.EventCache)
            {
                var evt = _serializer.DeserializeEvent(kv.Value);
                if (evt != null) yield return evt;
            }
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

        public void ClearCache()
        {
            _ws.EventCache?.Clear();
        }

        public bool HasPendingExcept(string excludeDefName)
        {
            if (_ws.PendingEventIds == null || _ws.EventCache == null) return false;
            foreach (var eventId in _ws.PendingEventIds)
            {
                if (_ws.EventCache.TryGetValue(eventId, out var json))
                {
                    var evt = _serializer.DeserializeEvent(json);
                    if (evt != null && !string.Equals(evt.DefName, excludeDefName, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        // ================================================================
        // 内容指纹计算
        // ================================================================

        /// <summary>
        /// 计算事件的内容指纹，用于去重。
        /// 指纹 = DefName + sorted Payload KV pairs。
        /// </summary>
        private static string ComputeFingerprint(IGameEvent evt)
        {
            var sb = new StringBuilder();
            sb.Append(evt.DefName ?? "");
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                foreach (var kv in evt.Payload.OrderBy(kv => kv.Key))
                    sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value ?? "");
            }
            return sb.ToString();
        }
    }
}
