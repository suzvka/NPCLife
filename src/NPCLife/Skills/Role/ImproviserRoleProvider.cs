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
    [SkillDefinition(
        Id = "role_improviser",
        Name = "即兴编剧身份",
        Description = "即兴编剧 Agent 的角色设定、任务与原则",
        DefaultRoles = new[] { WorkspaceRole.Improviser })]
    public class ImproviserRoleProvider : IMcpHookProvider, ISkillModule
    {
        // IMcpHookProvider
        public string HookId => "role_improviser";
        public string HookName => "即兴编剧身份";
        public string HookDescription => "即兴编剧 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultImproviserPrompt;
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
