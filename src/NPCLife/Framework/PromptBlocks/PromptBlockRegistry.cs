using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// PromptBlock 注册表。管理全局块和技能关联块的注册与查询。
    /// 
    /// 三种注册来源：
    ///   1. 全局块（RegisterGlobal）：始终生效，如 RoleIdentityBlock
    ///   2. 技能关联块（RegisterLinked）：随 Skill 激活/反激活动态生效，如 SkillPromptBlock
    ///   3. McpSkillRegistry 注册联动（RegisterLinkedFromProvider）：Provider 注册时自动创建 SkillPromptBlock
    /// 
    /// 查询时（GetActiveBlocks），返回全局块 + 所有激活技能对应的关联块。
    /// </summary>
    public static class PromptBlockRegistry
    {
        private static readonly List<IPromptBlock> _globalBlocks = new List<IPromptBlock>();
        private static readonly Dictionary<string, List<IPromptBlock>> _linkedBlocks
            = new Dictionary<string, List<IPromptBlock>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        /// <summary>
        /// 注册全局块。对所有 workspace 始终生效。
        /// </summary>
        public static void RegisterGlobal(IPromptBlock block)
        {
            if (block == null) return;
            lock (_lock)
            {
                _globalBlocks.Add(block);
            }
        }

        /// <summary>
        /// 注册技能关联块。当 skillId 对应的 Skill 被激活时生效。
        /// </summary>
        public static void RegisterLinked(string skillId, IPromptBlock block)
        {
            if (string.IsNullOrEmpty(skillId) || block == null) return;
            lock (_lock)
            {
                if (!_linkedBlocks.TryGetValue(skillId, out var list))
                {
                    list = new List<IPromptBlock>();
                    _linkedBlocks[skillId] = list;
                }
                list.Add(block);
            }
        }

        /// <summary>
        /// 从 IMcpHookProvider 注册 SkillPromptBlock。
        /// 由 McpSkillRegistry.RegisterFromProvider 联动调用。
        /// </summary>
        internal static void RegisterLinkedFromProvider(IMcpHookProvider provider)
        {
            if (provider == null) return;
            RegisterLinked(provider.HookId, new SkillPromptBlock(provider));
        }

        /// <summary>
        /// 查询当前应生效的全部 PromptBlock。
        /// </summary>
        /// <param name="activeSkillIds">当前工作空间激活的技能 ID 集合。</param>
        public static IReadOnlyList<IPromptBlock> GetActiveBlocks(IEnumerable<string> activeSkillIds)
        {
            lock (_lock)
            {
                var result = new List<IPromptBlock>(_globalBlocks);

                if (activeSkillIds != null)
                {
                    foreach (var skillId in activeSkillIds)
                    {
                        if (string.IsNullOrEmpty(skillId)) continue;
                        if (_linkedBlocks.TryGetValue(skillId, out var blocks))
                            result.AddRange(blocks);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 清除所有注册（测试用）。
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _globalBlocks.Clear();
                _linkedBlocks.Clear();
            }
        }
    }
}
