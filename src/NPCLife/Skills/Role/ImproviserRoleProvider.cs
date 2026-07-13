using NPCLife.Driver;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;

namespace NPCLife.Skills
{
    /// <summary>
    /// 即兴编剧角色的身份设定 Skill。纯提示词注入，无工具。
    /// 对所有 Improviser 工作空间自动激活。
    /// </summary>
    [SkillDefinition(
        Id = "role_improviser",
        Name = "即兴编剧身份",
        Description = "即兴编剧 Agent 的角色设定、任务与原则",
        DefaultRoles = new[] { WorkspaceRole.Improviser })]
    public class ImproviserRoleProvider : IMcpHookProvider
    {
        public string HookId => "role_improviser";
        public string HookName => "即兴编剧身份";
        public string HookDescription => "即兴编剧 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultImproviserPrompt;
        public IReadOnlyList<McpTool> GetTools() => Array.Empty<McpTool>();
    }
}
