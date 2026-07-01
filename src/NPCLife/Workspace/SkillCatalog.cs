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
            new("colony_overview", "游戏全局状态",
                "局势概览、近期事件、活跃目标、资源库存",
                WorkspaceRole.Director),

            new("character_query", "角色与环境查询",
                "获取角色人物卡、按条件筛选殖民者、列出全部角色、查询角色当前所处环境（室内外、温光、天气、房间）",
                WorkspaceRole.Director, WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),

            new("relationship_query", "关系网络",
                "查询角色社交关系、交互历史流水",
                WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),

            new("knowledge_management", "知识管理",
                "词条查询、学习、列表、删除、统计",
                WorkspaceRole.Director,WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),

            new("storyline_direction", "剧情分支管理",
                "剧情线的创建、分支、合并、生命周期管理",
                WorkspaceRole.Director),

            new("storyline_writing", "写作工具集",
                "用于创作具体台词脚本的工具",
                WorkspaceRole.Screenwriter, WorkspaceRole.Improviser),
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
