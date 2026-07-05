using NPCLife.Workspace;
using System;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 技能定义属性。标注在 IMcpHookProvider 实现类上，
    /// 声明该技能的 ID、名称、描述和默认授权角色。
    /// 
    /// SkillCatalog 通过反射扫描此属性聚合所有技能元数据，
    /// 替代硬编码数组。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class SkillDefinitionAttribute : Attribute
    {
        /// <summary>技能唯一 ID（如 "storyline_direction"）。</summary>
        public string Id { get; set; }

        /// <summary>技能显示名（如 "剧情分支管理"）。</summary>
        public string Name { get; set; }

        /// <summary>技能功能描述。</summary>
        public string Description { get; set; }

        /// <summary>默认授权的 Agent 角色集合。</summary>
        public WorkspaceRole[] DefaultRoles { get; set; }
    }
}
