using NPCLife.Driver;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;

namespace NPCLife.Skills
{
    /// <summary>
    /// 编剧角色的身份设定 Skill。纯提示词注入，无工具。
    /// 对所有 Screenwriter 工作空间自动激活。
    /// </summary>
    [SkillDefinition(
        Id = "role_screenwriter",
        Name = "编剧身份",
        Description = "编剧 Agent 的角色设定、任务与原则",
        DefaultRoles = new[] { WorkspaceRole.Screenwriter })]
    public class ScreenwriterRoleProvider : IMcpHookProvider
    {
        public string HookId => "role_screenwriter";
        public string HookName => "编剧身份";
        public string HookDescription => "编剧 Agent 的角色设定、任务与原则";
        public string PromptInstruction => PromptConfig.DefaultScreenwriterPrompt;
        public IReadOnlyList<McpTool> GetTools() => Array.Empty<McpTool>();
    }
}
