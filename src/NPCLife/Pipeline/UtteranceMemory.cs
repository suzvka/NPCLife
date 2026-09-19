using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 话语记录：一次"谁对谁说过什么、涉及哪些材料"的流水条目。
    /// 只是话语流水 + 引用关系，不携带叙事语义。
    /// </summary>
    public sealed class UtteranceRecord
    {
        public string UtteranceId;
        public string SpeakerId;
        public string ListenerId;
        public long Tick;

        /// <summary>本次话语引用（说出）的材料事件 ID 集合——供"未提及度"防重复。</summary>
        public IReadOnlyList<string> ReferencedEventIds;

        /// <summary>说出的台词文本（可选，供连续性；机械装配不强依赖）。</summary>
        public string Text;
    }

    /// <summary>
    /// 话语记忆抽象：记录话语流水，供两处消费——
    /// ① 判别器的"未提及度"特征（防重复）；② 随袋注入写手的最近话语（连续性）。
    /// 实现：<see cref="InMemoryUtteranceMemory"/>。
    /// </summary>
    public interface IUtteranceMemory
    {
        /// <summary>记录一条话语。</summary>
        void Record(UtteranceRecord record);

        /// <summary>某事件被提及的总次数（跨所有说话者）。用于全局未提及度。</summary>
        int MentionCount(string eventId);

        /// <summary>某事件被指定说话者提及的次数。用于 per-speaker 未提及度。</summary>
        int MentionCount(string speakerId, string eventId);

        /// <summary>取某对话对（speaker↔listener）最近的话语，按时间升序，最多 limit 条。</summary>
        IReadOnlyList<UtteranceRecord> RecentFor(string speakerId, string listenerId, int limit);

        /// <summary>已记录话语总数。</summary>
        int TotalRecorded { get; }
    }

    /// <summary>
    /// 话语记忆的内存实现。话语流水按容量环形裁剪（只影响 <see cref="RecentFor"/>）；
    /// 提及计数单调不减（只增），作为稳定的"未提及度"信号，不随流水裁剪回退。
    /// </summary>
    public sealed class InMemoryUtteranceMemory : IUtteranceMemory
    {
        private readonly int _maxRecords;
        private readonly List<UtteranceRecord> _records = new List<UtteranceRecord>();
        private readonly Dictionary<string, int> _globalMentions = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _speakerMentions = new Dictionary<string, int>();

        /// <param name="maxRecords">流水保留上限（≤0 表示不限）。计数不受此限。</param>
        public InMemoryUtteranceMemory(int maxRecords = 512)
        {
            _maxRecords = maxRecords;
        }

        public int TotalRecorded => _records.Count;

        public void Record(UtteranceRecord record)
        {
            if (record == null) return;
            _records.Add(record);
            if (_maxRecords > 0 && _records.Count > _maxRecords)
                _records.RemoveAt(0); // 裁剪最旧流水

            if (record.ReferencedEventIds != null)
            {
                foreach (var id in record.ReferencedEventIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    Bump(_globalMentions, id);
                    if (!string.IsNullOrEmpty(record.SpeakerId))
                        Bump(_speakerMentions, CompositeKey(record.SpeakerId, id));
                }
            }
        }

        public int MentionCount(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return 0;
            return _globalMentions.TryGetValue(eventId, out var c) ? c : 0;
        }

        public int MentionCount(string speakerId, string eventId)
        {
            if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(eventId)) return 0;
            return _speakerMentions.TryGetValue(CompositeKey(speakerId, eventId), out var c) ? c : 0;
        }

        public IReadOnlyList<UtteranceRecord> RecentFor(string speakerId, string listenerId, int limit)
        {
            if (limit <= 0 || _records.Count == 0) return System.Array.Empty<UtteranceRecord>();
            var result = new List<UtteranceRecord>(limit);
            // 从最新往回扫描，凑满 limit 后反转为时间升序返回
            for (int i = _records.Count - 1; i >= 0 && result.Count < limit; i--)
            {
                var r = _records[i];
                bool matchesSpeaker = string.IsNullOrEmpty(speakerId) || r.SpeakerId == speakerId;
                bool matchesListener = string.IsNullOrEmpty(listenerId) || r.ListenerId == listenerId;
                if (matchesSpeaker && matchesListener) result.Add(r);
            }
            result.Reverse();
            return result;
        }

        private static void Bump(Dictionary<string, int> dict, string key)
        {
            dict.TryGetValue(key, out var c);
            dict[key] = c + 1;
        }

        private static string CompositeKey(string speaker, string eventId) => speaker + "\u0001" + eventId;
    }
}
