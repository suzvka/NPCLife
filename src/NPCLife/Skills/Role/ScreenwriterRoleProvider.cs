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
        Id = "role_screenwriter",
        Name = "编剧身份",
        Description = "编剧 Agent 的角色设定、任务与原则",
        DefaultRoles = new[] { WorkspaceRole.Screenwriter })]
    public class ScreenwriterRoleProvider : IMcpHookProvider, ISkillModule
    {
        // IMcpHookProvider
        public string HookId => "role_screenwriter";
        public string HookName => "编剧身份";
        public string HookDescription => "编剧 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultScreenwriterPrompt;
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
