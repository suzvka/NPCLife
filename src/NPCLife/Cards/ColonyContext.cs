using System.Collections.Generic;

namespace NPCLife.Cards
{
    /// <summary>
    /// 世界全局上下文 (World Context)：所有卡片的共享时间/状态环境。
    /// 纯 DTO，由宿主填充数据。
    /// </summary>
    public class ColonyContext : IExtensibleCard
    {
        // ================================================================
        // 时间
        // ================================================================

        /// <summary>
        /// 当前游戏时间的人类可读字符串。由游戏侧全权负责格式化，
        /// 框架原样透传给 LLM。不同游戏可自由定义格式：
        ///  RimWorld: "第2年·夏季·第5天·14h"
        ///  科幻题材: "Mission Day 42, 14:00 UTC"
        ///  修仙题材: "星历 4523.7"
        /// </summary>
        public string Timestamp;

        /// <summary>当前游戏 tick（仅供游戏侧自动化系统内部使用，不序列化给 LLM）。</summary>
        public int CurrentTick;

        /// <summary>"Spring" / "Summer" / "Fall" / "Winter"</summary>
        public string Season;

        /// <summary>"Dawn" / "Day" / "Dusk" / "Night"</summary>
        public string TimeOfDay;

        /// <summary>游戏年份。</summary>
        public int Year;

        /// <summary>当前小时 (0~23)。</summary>
        public int Hour;

        // ================================================================
        // 殖民地状态
        // ================================================================

        /// <summary>存活人口数量。</summary>
        public int PopulationAlive;

        /// <summary>角色摘要列表（轻量）。</summary>
        public IReadOnlyList<ColonistSummary> Colonists;

        /// <summary>与其他派系的关系。</summary>
        public IReadOnlyList<FactionStanding> FactionRelations;

        /// <summary>"Abundant" / "Adequate" / "Low" / "Famine" / "Starving"</summary>
        public string FoodStatus;

        /// <summary>"Stable" / "Adequate" / "Strained" / "Blackout"</summary>
        public string PowerStatus;

        /// <summary>平均士气 (0~1)。</summary>
        public float MoraleAverage;

        /// <summary>平均士气语义标签。</summary>
        public string MoraleTier;

        /// <summary>当前未解决的威胁摘要列表。</summary>
        public IReadOnlyList<string> ActiveThreats;

        // ================================================================
        // 难度
        // ================================================================

        /// <summary>难度等级。</summary>
        public string Difficulty;

        // ================================================================
        // 生命周期
        // ================================================================

        /// <summary>会话开始 tick（仅供游戏侧自动化系统内部使用，不序列化给 LLM）。</summary>
        public int ColonyStartTick;

        // ================================================================
        // 科技
        // ================================================================

        /// <summary>当前科技等级。</summary>
        public string TechLevel;

        /// <summary>扩展字段。</summary>
        public Dictionary<string, string> ExtensionFields { get; set; }
    }
}
