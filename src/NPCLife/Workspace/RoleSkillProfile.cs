using System.Collections.Generic;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 角色 Skill Profile 预设。从 SkillCatalog 派生，定义每种 Agent 角色
    /// 创建工作空间时默认激活的技能集。
    ///
    /// 新增角色技能只需在 SkillCatalog.AllSkills 的 DefaultRoles 中添加角色，
    /// 无需修改本类。
    /// </summary>
    public static class RoleSkillProfile
    {
        /// <summary>
        /// 获取指定角色的默认技能 ID 列表。
        /// </summary>
        public static IReadOnlyList<string> GetDefaultSkillIds(WorkspaceRole role)
        {
            return SkillCatalog.GetDefaultSkillIds(role);
        }
    }
}
