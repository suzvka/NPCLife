using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using System.Reflection;

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
    public class DirectorRoleProvider : IMcpHookProvider, ISkillModule
    {
        // IMcpHookProvider
        public string HookId => "role_director";
        public string HookName => "导演身份";
        public string HookDescription => "导演 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultDirectorPrompt;
        public IReadOnlyList<McpTool> GetTools() => Array.Empty<McpTool>();

        // ISkillModule
        string ISkillModule.Id => HookId;
        string ISkillModule.Name => HookName;
        string ISkillModule.Description => HookDescription;
        WorkspaceRole[] ISkillModule.DefaultRoles => GetType().GetCustomAttribute<SkillDefinitionAttribute>()?.DefaultRoles ?? Array.Empty<WorkspaceRole>();
        string ISkillModule.PromptInstruction => PromptInstruction;
        string ISkillModule.GetDynamicContext(IWorkspace workspace, IReadOnlyList<IGameEvent> events) => null;
        IReadOnlyList<McpTool> ISkillModule.GetTools() => Array.Empty<McpTool>();
    }
}
