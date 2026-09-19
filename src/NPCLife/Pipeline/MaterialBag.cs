using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>材料所属的三区之一。</summary>
    public enum MaterialRegion
    {
        /// <summary>K ∩ V：共同经历。</summary>
        SharedExperience = 0,
        /// <summary>K \ V：NPC 独知（信息投放）。</summary>
        NpcOnly = 1,
        /// <summary>V \ K：玩家独知（认知边界，写手不得让 NPC 言及）。</summary>
        PlayerOnly = 2,
    }

    /// <summary>
    /// 材料项：入选材料袋的单条材料 = 一个标注事件 + 判别器显著度 + 归属区。不可变值对象。
    /// </summary>
    public sealed class MaterialItem
    {
        /// <summary>底层标注事件。</summary>
        public AnnotatedEvent Event { get; }

        /// <summary>判别器给出的显著度 [0,1]（越大越该说）。</summary>
        public float Salience { get; }

        /// <summary>所属三区。</summary>
        public MaterialRegion Region { get; }

        public MaterialItem(AnnotatedEvent evt, float salience, MaterialRegion region)
        {
            Event = evt;
            Salience = salience;
            Region = region;
        }

        /// <summary>事件 ID 便捷访问。</summary>
        public string EventId => Event != null ? Event.EventId : null;
    }

    /// <summary>
    /// 材料袋：话语时刻所需材料的装配结果，写手的唯一输入。
    /// 三区材料由装配器机械装配；Persona / Anchors 为可空注入槽位，由调用方按需填充。
    /// 不可变：装配完成后作为快照供审计。
    /// </summary>
    public sealed class MaterialBag
    {
        /// <summary>本袋服务的话语时刻。</summary>
        public UtteranceMoment Moment { get; }

        /// <summary>K ∩ V 材料（显著度降序）。</summary>
        public IReadOnlyList<MaterialItem> SharedExperience { get; }

        /// <summary>K \ V 材料（显著度降序）。</summary>
        public IReadOnlyList<MaterialItem> NpcOnly { get; }

        /// <summary>V \ K 材料（认知边界，显著度降序）。</summary>
        public IReadOnlyList<MaterialItem> PlayerOnly { get; }

        /// <summary>话语记忆：最近说过什么（防重复 / 连续性）。</summary>
        public IReadOnlyList<UtteranceRecord> RecentUtterances { get; }

        /// <summary>说话者人格卡（可空）。</summary>
        public Cards.CharacterCard Persona { get; }

        /// <summary>专名 / 知识锚点（可空，防专名误解）。</summary>
        public IReadOnlyList<Core.KnowledgeEntry> Anchors { get; }

        /// <summary>机械维护的未结线索清单（伏笔/回调候选，供写手顺带收放）。可空。</summary>
        public IReadOnlyList<OpenLoop> OpenLoops { get; }

        public MaterialBag(UtteranceMoment moment,
            IReadOnlyList<MaterialItem> sharedExperience,
            IReadOnlyList<MaterialItem> npcOnly,
            IReadOnlyList<MaterialItem> playerOnly,
            IReadOnlyList<UtteranceRecord> recentUtterances,
            Cards.CharacterCard persona = null,
            IReadOnlyList<Core.KnowledgeEntry> anchors = null,
            IReadOnlyList<OpenLoop> openLoops = null)
        {
            Moment = moment;
            SharedExperience = sharedExperience ?? System.Array.Empty<MaterialItem>();
            NpcOnly = npcOnly ?? System.Array.Empty<MaterialItem>();
            PlayerOnly = playerOnly ?? System.Array.Empty<MaterialItem>();
            RecentUtterances = recentUtterances ?? System.Array.Empty<UtteranceRecord>();
            Persona = persona;
            Anchors = anchors;
            OpenLoops = openLoops ?? System.Array.Empty<OpenLoop>();
        }

        /// <summary>袋内全部材料（三区拼接）。空袋时返回空序列（走环境脉冲兜底）。</summary>
        public IEnumerable<MaterialItem> AllMaterials()
        {
            for (int i = 0; i < SharedExperience.Count; i++) yield return SharedExperience[i];
            for (int i = 0; i < NpcOnly.Count; i++) yield return NpcOnly[i];
            for (int i = 0; i < PlayerOnly.Count; i++) yield return PlayerOnly[i];
        }

        /// <summary>三区是否全空。</summary>
        public bool IsEmpty => SharedExperience.Count == 0 && NpcOnly.Count == 0 && PlayerOnly.Count == 0;

        /// <summary>袋内材料总数。</summary>
        public int Count => SharedExperience.Count + NpcOnly.Count + PlayerOnly.Count;
    }
}
