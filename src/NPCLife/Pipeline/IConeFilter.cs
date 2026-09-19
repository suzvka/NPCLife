using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 三区投影结果：给定话语时刻，从事件流解析出的三区候选事件 ID 列表。仅机械集合运算，零语义判断。
    /// </summary>
    public sealed class ConeProjection
    {
        /// <summary>K ∩ V：说话 NPC 与玩家共同经历 → 回响 / callback 素材。</summary>
        public IReadOnlyList<string> SharedExperience { get; }

        /// <summary>K \ V：NPC 独知（玩家不知道）→ 信息投放素材。</summary>
        public IReadOnlyList<string> NpcOnly { get; }

        /// <summary>V \ K：玩家独知（NPC 不知道）→ 认知边界（防全知幻觉硬机制）。</summary>
        public IReadOnlyList<string> PlayerOnly { get; }

        public ConeProjection(IReadOnlyList<string> sharedExperience,
            IReadOnlyList<string> npcOnly, IReadOnlyList<string> playerOnly)
        {
            SharedExperience = sharedExperience ?? System.Array.Empty<string>();
            NpcOnly = npcOnly ?? System.Array.Empty<string>();
            PlayerOnly = playerOnly ?? System.Array.Empty<string>();
        }

        /// <summary>三区是否全空（材料装配据此走空袋 + 环境脉冲兜底）。</summary>
        public bool IsEmpty =>
            SharedExperience.Count == 0 && NpcOnly.Count == 0 && PlayerOnly.Count == 0;
    }

    /// <summary>
    /// 视锥解析器：给定认知主体（NPC / 玩家 ID），返回其"感知到"的事件 ID 集合。
    /// 这是视锥过滤的可插拔机械规则入口——粗视锥（参与即感知）为默认实现，
    /// 替换实现可叠加地理目击 / 听闻传播而不改调用方。
    /// </summary>
    public interface IConeResolver
    {
        /// <summary>返回主体 subjectId 在时刻 m 感知到的事件 ID 集合（不含 null/空 ID）。</summary>
        IReadOnlyCollection<string> PerceivedEvents(string subjectId, UtteranceMoment m);
    }

    /// <summary>
    /// 视锥过滤器接口：从事件流机械解析 K_n 与 V_p，输出三区候选。
    /// </summary>
    public interface IConeFilter
    {
        ConeProjection Project(UtteranceMoment m);
    }
}
