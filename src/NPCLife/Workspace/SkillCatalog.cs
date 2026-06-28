using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 技能目录 —— 所有业务技能的单一信息来源。
    /// 
    /// 解决了新增 Agent 角色时需在 InitializeDefaults / RoleSkillProfile 多处注册的问题：
    /// 新增技能只需在 AllSkills 数组中添加一条定义，InitializeDefaults 和
    /// RoleSkillProfile 均从此派生。
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>
        /// 单个技能的定义：ID、名称、描述、默认授权的角色集合。
        /// </summary>
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly string Name;
            public readonly string Description;
            public readonly WorkspaceRole[] DefaultRoles;

            public Entry(string id, string name, string description, params WorkspaceRole[] defaultRoles)
            {
                Id = id;
                Name = name;
                Description = description;
                DefaultRoles = defaultRoles;
            }
        }

        /// <summary>
        /// 全部业务技能的唯一定义数组。system 技能由框架层隐式管理，不在此列。
        /// </summary>
        public static readonly Entry[] AllSkills =
        {
            new("colony_overview", "殖民地全局",
                "殖民地概览、近期事件、活跃目标、资源库存",
                WorkspaceRole.Director, WorkspaceRole.Screenwriter),

            new("character_query", "角色查询",
                "获取角色完整人物卡、按条件筛选殖民者、列出全部角色",
                WorkspaceRole.Director, WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),

            new("relationship_query", "关系网络",
                "查询角色社交关系、交互历史流水",
                WorkspaceRole.Screenwriter),

            new("event_query", "事件回溯",
                "多维事件历史查询（标签、时间、Actor、严重度）",
                WorkspaceRole.Director, WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),

            new("environment_query", "环境感知",
                "查询角色当前所处的环境信息（室内外、温光、天气、房间）",
                WorkspaceRole.Screenwriter),

            new("knowledge_management", "知识管理",
                "词条查询、学习、列表、删除、统计",
                WorkspaceRole.Director, WorkspaceRole.Screenwriter),

            new("workspace_direction", "工作空间(导演)",
                "剧情线工作空间的创建、分支、合并、生命周期管理。导演专用。",
                WorkspaceRole.Director),

            new("workspace_writing", "工作空间(编剧)",
                "推送叙事回合、上报推进状态信号、事件路由。上下文由 prompt 自动注入。编剧专用。",
                WorkspaceRole.Screenwriter),

            new("workspace_improviser", "工作空间(即兴编剧)",
                "处理突发、独立事件的叙事输出。逐句台词推送与编剧同构。即兴编剧专用。",
                WorkspaceRole.Improviser),
        };

        /// <summary>
        /// 获取指定角色的默认技能 ID 列表。
        /// </summary>
        public static IReadOnlyList<string> GetDefaultSkillIds(WorkspaceRole role)
        {
            return AllSkills
                .Where(e => e.DefaultRoles.Contains(role))
                .Select(e => e.Id)
                .ToArray();
        }
    }
}
