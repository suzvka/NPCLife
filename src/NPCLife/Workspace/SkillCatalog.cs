using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NPCLife.Workspace
{
    /// <summary>
    /// 技能目录 —— 所有业务技能的单一信息来源。
    /// 
    /// 通过反射扫描 [SkillDefinition] 属性聚合技能元数据，
    /// 替代硬编码数组。新增技能只需在 Provider 类上标注属性，无需修改本文件。
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

        private static Entry[] _cachedAllSkills;

        /// <summary>
        /// 全部业务技能的定义。从当前程序集中扫描所有带 [SkillDefinition] 属性的类型。
        /// system 技能由框架层隐式管理，不在此列。
        /// </summary>
        public static Entry[] AllSkills
        {
            get
            {
                if (_cachedAllSkills != null) return _cachedAllSkills;

                var entries = new List<Entry>();

                // 扫描所有已加载的程序集，发现带 [SkillDefinition] 的类型
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch { continue; }

                    foreach (var type in types)
                    {
                        try
                        {
                            var attr = type.GetCustomAttribute<SkillDefinitionAttribute>();
                            if (attr == null) continue;

                            entries.Add(new Entry(
                                attr.Id,
                                attr.Name,
                                attr.Description,
                                attr.DefaultRoles ?? Array.Empty<WorkspaceRole>()));
                        }
                        catch
                        {
                            // 跳过无法加载的类型
                        }
                    }
                }

                _cachedAllSkills = entries.ToArray();
                return _cachedAllSkills;
            }
        }

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
