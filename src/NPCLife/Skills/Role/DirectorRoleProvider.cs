using NPCLife.Driver;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;

namespace NPCLife.Skills
{
    /// <summary>
    /// 导演角色的身份设定 Skill。纯提示词注入，无工具。
    /// 对所有 Director 工作空间自动激活。
    /// </summary>
    [SkillDefinition(
        Id = "role_director",
        Name = "导演身份",
        Description = "导演 Agent 的角色设定、任务与原则",
        DefaultRoles = new[] { WorkspaceRole.Director })]
    public class DirectorRoleProvider : IMcpHookProvider
    {
        public string HookId => "role_director";
        public string HookName => "导演身份";
        public string HookDescription => "导演 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultDirectorPrompt;
        public IReadOnlyList<McpTool> GetTools() => Array.Empty<McpTool>();
    }
}
