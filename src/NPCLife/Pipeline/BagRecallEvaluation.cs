using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// bag 召回基线：给定话语时刻，返回"应当进入材料袋"的关键事件 ID 集合。
    ///
    /// 这是**影子/回放评估的旁路件**，非热路径组件。可用可插拔的合成基线
    /// （如"与说话者相关的事件全集"）验证判别侧的高召回性质——召回率是
    /// 验收度量，不驱动任何在线逻辑。
    /// </summary>
    public interface IBagBaseline
    {
        IReadOnlyCollection<string> RelevantEventIds(UtteranceMoment m);
    }

    /// <summary>召回率评估报告。</summary>
    public sealed class RecallReport
    {
        public int BaselineCount;
        public int RecalledCount;
        /// <summary>|袋 ∩ 基线| / |基线|；基线为空时约定为 1（vacuous truth）。</summary>
        public float Recall;
    }

    /// <summary>
    /// "说话者参与即相关"的合成基线（机械、无需日志）：K_n 全集作为召回目标。
    /// 用于验证材料袋对说话者相关事件的高召回——预算充足时应全部命中。
    /// </summary>
    public sealed class SpeakerParticipantBaseline : IBagBaseline
    {
        private readonly IEventStream _stream;
        public SpeakerParticipantBaseline(IEventStream stream) { _stream = stream; }

        public IReadOnlyCollection<string> RelevantEventIds(UtteranceMoment m)
        {
            if (_stream == null || m == null || string.IsNullOrEmpty(m.SpeakerId))
                return System.Array.Empty<string>();
            return _stream.Query(new StreamQuery { ActorId = m.SpeakerId })
                .Select(e => e.EventId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
        }
    }

    /// <summary>
    /// bag 召回率计算器（离线 QA 用）。把材料袋的事件 ID 与基线集合求交，算召回。
    /// </summary>
    public static class BagRecallEvaluator
    {
        public static RecallReport Evaluate(MaterialBag bag, IBagBaseline baseline, UtteranceMoment m)
        {
            var relevant = baseline?.RelevantEventIds(m);
            var baselineIds = relevant != null ? new HashSet<string>(relevant) : new HashSet<string>();

            var bagIds = new HashSet<string>();
            if (bag != null)
                foreach (var item in bag.AllMaterials())
                    if (!string.IsNullOrEmpty(item.EventId)) bagIds.Add(item.EventId);

            int recalled = 0;
            foreach (var id in baselineIds)
                if (bagIds.Contains(id)) recalled++;

            float recall = baselineIds.Count == 0 ? 1f : (float)recalled / baselineIds.Count;
            return new RecallReport
            {
                BaselineCount = baselineIds.Count,
                RecalledCount = recalled,
                Recall = recall
            };
        }
    }
}
