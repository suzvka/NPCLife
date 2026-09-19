using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 粗视锥解析器（无宿主可见性数据时的机械基线）——
    /// "参与即感知"：主体出现在事件 actor 集合中，即视为其视锥含该事件。
    /// 走事件流 actor 索引，单次解析 O(命中数)，零 LLM、零全扫描。
    ///
    /// 粒度损失可接受：真实目击半径 / UI 暴露 / 听闻传播属可选富化，
    /// 替换 <see cref="IConeResolver"/> 实现即可叠加，不改过滤器与调用方。
    /// </summary>
    public sealed class ParticipantConeResolver : IConeResolver
    {
        private readonly IEventStream _stream;

        public ParticipantConeResolver(IEventStream stream)
        {
            _stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
        }

        public IReadOnlyCollection<string> PerceivedEvents(string subjectId, UtteranceMoment m)
        {
            if (string.IsNullOrEmpty(subjectId)) return System.Array.Empty<string>();
            var slice = _stream.Query(new StreamQuery { ActorId = subjectId });
            var ids = new List<string>(slice.Count);
            foreach (var e in slice)
            {
                var id = e.EventId;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }
    }

    /// <summary>
    /// 粗视锥过滤器：解析说话者 K_n 与听众 V_p 两视锥，做三区集合运算。
    /// 全机械、零生成，输出顺序稳定（按事件流写入序 → 时间序）。
    /// </summary>
    public sealed class CoarseConeFilter : IConeFilter
    {
        private readonly IEventStream _stream;
        private readonly IConeResolver _resolver;

        public CoarseConeFilter(IEventStream stream, IConeResolver resolver = null)
        {
            _stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
            _resolver = resolver ?? new ParticipantConeResolver(stream);
        }

        public ConeProjection Project(UtteranceMoment m)
        {
            if (m == null) return new ConeProjection(null, null, null);

            var npc = _resolver.PerceivedEvents(m.SpeakerId, m);    // K_n
            var player = _resolver.PerceivedEvents(m.ListenerId, m); // V_p

            // 三区按事件流时间序输出：以流内顺序遍历并归入对应区域，保证确定性。
            var shared = new List<string>();
            var npcOnly = new List<string>();
            var playerOnly = new List<string>();

            // 先按时间序枚举所有出现过的候选（K ∪ V），避免依赖 resolver 返回顺序。
            var npcSet = new HashSet<string>(npc);
            var playerSet = new HashSet<string>(player);
            var union = new HashSet<string>(npcSet);
            union.UnionWith(playerSet);
            if (union.Count == 0)
                return new ConeProjection(shared, npcOnly, playerOnly);

            foreach (var e in _stream.Query(StreamQuery.All))
            {
                var id = e.EventId;
                if (string.IsNullOrEmpty(id) || !union.Contains(id)) continue;
                bool inNpc = npcSet.Contains(id);
                bool inPlayer = playerSet.Contains(id);
                if (inNpc && inPlayer) shared.Add(id);
                else if (inNpc) npcOnly.Add(id);
                else playerOnly.Add(id);
            }

            return new ConeProjection(shared, npcOnly, playerOnly);
        }
    }
}
