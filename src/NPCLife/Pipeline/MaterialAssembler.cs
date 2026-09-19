using System.Collections.Generic;
using System.Linq;
using NPCLife.Cards;
using NPCLife.Core;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 材料装配器接口：为话语时刻装配材料袋。能力需求 = 机械 + 判别。
    /// </summary>
    public interface IMaterialAssembler
    {
        MaterialBag Assemble(UtteranceMoment moment,
            CharacterCard persona = null, IReadOnlyList<KnowledgeEntry> anchors = null);
    }

    /// <summary>
    /// 默认材料装配器。装配流程：
    /// 1. 视锥过滤 → 三区候选 ID；
    /// 2. 判别器逐候选打分 → 阈值淘汰 → 区内显著度降序；
    /// 3. 预算截断（可选：条数上限 + 三区各保底）→ 材料袋。
    ///
    /// 全程零 LLM；Persona / Anchors 为可空注入槽位，本装配器不强依赖宿主数据。
    /// 候选为空 → 空袋（调用方降级到环境脉冲 / Tier 0 模板）。
    /// </summary>
    public sealed class MaterialAssembler : IMaterialAssembler
    {
        private readonly IEventStream _stream;
        private readonly IConeFilter _coneFilter;
        private readonly ISalienceDiscriminator _discriminator;
        private readonly IUtteranceMemory _memory;
        private readonly IOpenLoopProvider _openLoops;   // 可空：接入则填充 bag.OpenLoops
        private readonly SalienceConfig _cfg;

        public MaterialAssembler(
            IEventStream stream,
            IConeFilter coneFilter,
            ISalienceDiscriminator discriminator,
            SalienceConfig config = null,
            IUtteranceMemory memory = null,
            IOpenLoopProvider openLoops = null)
        {
            _stream = stream ?? throw new System.ArgumentNullException(nameof(stream));
            _coneFilter = coneFilter ?? throw new System.ArgumentNullException(nameof(coneFilter));
            _discriminator = discriminator ?? throw new System.ArgumentNullException(nameof(discriminator));
            _cfg = config ?? SalienceConfig.CreateDefault();
            _memory = memory; // 可空：影响 RecentUtterances 与判别器未提及度
            _openLoops = openLoops; // 可空：为 null 时 bag.OpenLoops 为空
        }

        public MaterialBag Assemble(UtteranceMoment moment,
            CharacterCard persona = null, IReadOnlyList<KnowledgeEntry> anchors = null)
        {
            if (moment == null)
                return new MaterialBag(null, null, null, null, null, persona, anchors);

            var proj = _coneFilter.Project(moment);

            var shared = RankRegion(proj.SharedExperience, MaterialRegion.SharedExperience, moment);
            var npcOnly = RankRegion(proj.NpcOnly, MaterialRegion.NpcOnly, moment);
            var playerOnly = RankRegion(proj.PlayerOnly, MaterialRegion.PlayerOnly, moment);

            Truncate(shared, npcOnly, playerOnly, _cfg, out var selShared, out var selNpc, out var selPlayer);

            var recent = _memory != null
                ? _memory.RecentFor(moment.SpeakerId, moment.ListenerId, _cfg.RecentUtteranceLimit)
                : System.Array.Empty<UtteranceRecord>();

            var openLoops = _openLoops != null
                ? _openLoops.OpenLoops(moment)
                : System.Array.Empty<OpenLoop>();

            return new MaterialBag(moment, selShared, selNpc, selPlayer, recent, persona, anchors, openLoops);
        }

        /// <summary>逐候选打分 → 阈值淘汰 → 显著度降序（同分按事件流时间序稳定）。</summary>
        private List<MaterialItem> RankRegion(IReadOnlyList<string> ids, MaterialRegion region, UtteranceMoment m)
        {
            var items = new List<MaterialItem>();
            if (ids == null) return items;
            foreach (var id in ids)
            {
                var evt = _stream.GetById(id);
                if (evt == null) continue;
                float score = _discriminator.Score(evt, region, m);
                if (score >= _cfg.SalienceThreshold)
                    items.Add(new MaterialItem(evt, score, region));
            }
            // OrderByDescending 稳定排序：同分保留输入（时间）顺序
            return items.OrderByDescending(i => i.Salience).ToList();
        }

        /// <summary>
        /// 预算截断：三区各先保底 MinPerRegion 条（非空区），剩余名额按全局显著度降序补齐至 MaxBagItems。
        /// 保底优先于上限——确保任一区不被全局截断饿死（保三区语义完整）。
        /// </summary>
        private static void Truncate(
            List<MaterialItem> shared, List<MaterialItem> npcOnly, List<MaterialItem> playerOnly,
            SalienceConfig cfg,
            out List<MaterialItem> selShared, out List<MaterialItem> selNpc, out List<MaterialItem> selPlayer)
        {
            // MaxBagItems <= 0 → 不做预算控制，保留全部过阈材料（截断机制待集成测试标定后启用）。
            if (cfg.MaxBagItems <= 0)
            {
                selShared = shared;
                selNpc = npcOnly;
                selPlayer = playerOnly;
                return;
            }

            var chosen = new HashSet<string>();
            int guaranteed = 0;

            // 1) 三区各保底
            guaranteed += TakeFloor(shared, cfg.MinPerRegion, chosen);
            guaranteed += TakeFloor(npcOnly, cfg.MinPerRegion, chosen);
            guaranteed += TakeFloor(playerOnly, cfg.MinPerRegion, chosen);

            // 2) 剩余名额：合并各区未选材料，按显著度降序补至 MaxBagItems
            int slots = cfg.MaxBagItems - guaranteed;
            if (slots > 0)
            {
                var pool = new List<MaterialItem>();
                AddUnchosen(shared, chosen, pool);
                AddUnchosen(npcOnly, chosen, pool);
                AddUnchosen(playerOnly, chosen, pool);
                pool = pool.OrderByDescending(i => i.Salience).ToList();
                for (int i = 0; i < pool.Count && i < slots; i++)
                    if (!string.IsNullOrEmpty(pool[i].EventId)) chosen.Add(pool[i].EventId);
            }

            // 3) 按选集回填各区（保持降序）
            selShared = Filter(shared, chosen);
            selNpc = Filter(npcOnly, chosen);
            selPlayer = Filter(playerOnly, chosen);
        }

        private static int TakeFloor(List<MaterialItem> region, int floor, HashSet<string> chosen)
        {
            int taken = 0;
            for (int i = 0; i < region.Count && taken < floor; i++)
            {
                if (string.IsNullOrEmpty(region[i].EventId)) continue;
                if (chosen.Add(region[i].EventId)) taken++;
            }
            return taken;
        }

        private static void AddUnchosen(List<MaterialItem> region, HashSet<string> chosen, List<MaterialItem> pool)
        {
            for (int i = 0; i < region.Count; i++)
                if (!string.IsNullOrEmpty(region[i].EventId) && !chosen.Contains(region[i].EventId))
                    pool.Add(region[i]);
        }

        private static List<MaterialItem> Filter(List<MaterialItem> region, HashSet<string> chosen)
        {
            var result = new List<MaterialItem>();
            for (int i = 0; i < region.Count; i++)
                if (!string.IsNullOrEmpty(region[i].EventId) && chosen.Contains(region[i].EventId))
                    result.Add(region[i]);
            return result;
        }
    }
}
