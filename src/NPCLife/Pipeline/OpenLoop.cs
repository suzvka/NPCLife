using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 未结线索（open loop）：NPC 认知视锥内、尚未被言说的高显著度事件——
    /// 可作为伏笔回收（callback）或补叙（plant）的材料。是"机械 open-loops 清单"的条目。
    /// 不可变值对象。
    /// </summary>
    public sealed class OpenLoop
    {
        public AnnotatedEvent Event { get; }
        public float Salience { get; }
        public MaterialRegion Region { get; }

        public OpenLoop(AnnotatedEvent evt, float salience, MaterialRegion region)
        {
            Event = evt;
            Salience = salience;
            Region = region;
        }

        public string EventId => Event != null ? Event.EventId : null;
    }

    /// <summary>
    /// open-loops 提供者：为话语时刻给出未结线索清单。默认实现是纯机械投影，
    /// 可选离线规划器（<see cref="IForeshadowPlanner"/>）作为额外来源合并进来。
    /// </summary>
    public interface IOpenLoopProvider
    {
        IReadOnlyList<OpenLoop> OpenLoops(UtteranceMoment m);
    }

    /// <summary>
    /// 机械 open-loops 清单——默认伏笔机制，零模型、零 LLM、无独立状态。
    ///
    /// 定义：说话者 K_n 视锥（共同经历 + NPC 独知两区）内、Importance 达阈值、
    /// 且尚未被该说话者在话语中引用过的事件，即"埋下而未回收"的线索。
    /// "已提及即闭合"——一旦被写手引用并计入话语记忆，事件自动从清单消失，
    /// 因此无需维护单独的生命周期状态，纯为 (事件流 + 话语记忆) 的投影。
    /// </summary>
    public sealed class MechanicalOpenLoopTracker : IOpenLoopProvider
    {
        private readonly IEventStream _stream;
        private readonly IConeFilter _coneFilter;
        private readonly IUtteranceMemory _memory;
        private readonly IForeshadowPlanner _planner;   // 可空：默认关闭
        private readonly float _minImportance;
        private readonly int _maxLoops;
        private readonly float _importanceSaturation;

        /// <param name="minImportance">计入未结线索的最低事件重要度。</param>
        /// <param name="maxLoops">单次话语注入的未结线索上限。</param>
        public MechanicalOpenLoopTracker(
            IEventStream stream,
            IConeFilter coneFilter,
            IUtteranceMemory memory,
            IForeshadowPlanner planner = null,
            float minImportance = 8f,
            int maxLoops = 4,
            float importanceSaturation = 10f)
        {
            _stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
            _coneFilter = coneFilter ?? throw new System.ArgumentNullException(nameof(coneFilter));
            _memory = memory;
            _planner = planner;
            _minImportance = minImportance;
            _maxLoops = maxLoops;
            _importanceSaturation = importanceSaturation > 0f ? importanceSaturation : 1f;
        }

        public IReadOnlyList<OpenLoop> OpenLoops(UtteranceMoment m)
        {
            if (m == null) return System.Array.Empty<OpenLoop>();

            var result = new List<OpenLoop>();
            var seen = new HashSet<string>();

            // 1) 机械投影：说话者可知（K∩V / K\V）且未言过的高重要度事件。
            var proj = _coneFilter.Project(m);
            Accumulate(proj.SharedExperience, MaterialRegion.SharedExperience, m, result, seen);
            Accumulate(proj.NpcOnly, MaterialRegion.NpcOnly, m, result, seen);

            // 2) 可选规划器：合并外部（跨场景）线索；默认 _planner 为 null 时跳过。
            if (_planner != null)
                foreach (var loop in _planner.PlanOpenLoops(m))
                    if (loop != null && !string.IsNullOrEmpty(loop.EventId) && seen.Add(loop.EventId))
                        result.Add(loop);

            // 显著度降序、同分新近优先，截断到 maxLoops。
            result.Sort((a, b) =>
            {
                int c = b.Salience.CompareTo(a.Salience);
                if (c != 0) return c;
                long ta = a.Event != null ? a.Event.GameTick : 0;
                long tb = b.Event != null ? b.Event.GameTick : 0;
                return tb.CompareTo(ta);
            });
            if (_maxLoops > 0 && result.Count > _maxLoops)
                result.RemoveRange(_maxLoops, result.Count - _maxLoops);
            return result;
        }

        private void Accumulate(IReadOnlyList<string> ids, MaterialRegion region, UtteranceMoment m,
            List<OpenLoop> result, HashSet<string> seen)
        {
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                var evt = _stream.GetById(id);
                if (evt?.Raw == null || evt.Raw.Importance < _minImportance) continue;
                // 已提及即闭合
                if (_memory != null && _memory.MentionCount(m.SpeakerId, id) > 0) continue;
                float imp = evt.Raw.Importance;
                float salience = imp / (imp + _importanceSaturation); // [0,1) 饱和归一
                result.Add(new OpenLoop(evt, salience, region));
            }
        }
    }

    /// <summary>
    /// 可选离线伏笔规划器钩子：产出跨场景的未结线索，供 <see cref="MechanicalOpenLoopTracker"/> 合并。
    /// 默认关闭——不提供实现时管线等价于纯机械清单，仍完整可用。
    /// 它是全系统唯一可能引入第二个 LLM 的位置；是否接入由宿主决定（本库不含该实现）。
    /// </summary>
    public interface IForeshadowPlanner
    {
        IEnumerable<OpenLoop> PlanOpenLoops(UtteranceMoment m);
    }
}
