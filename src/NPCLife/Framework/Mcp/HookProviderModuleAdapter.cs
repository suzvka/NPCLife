using NPCLife.Core;
using NPCLife.Framework.Mcp;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;

namespace NPCLife.Framework.Mcp
{
    /// <summary>
    /// 将旧 IMcpHookProvider 适配为 ISkillModule。内部使用，用于向后兼容。
    /// </summary>
    internal class HookProviderModuleAdapter : ISkillModule
    {
        private readonly IMcpHookProvider _provider;

        public HookProviderModuleAdapter(IMcpHookProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public string Id => _provider.HookId;
        public string Name => _provider.HookName;
        public string Description => _provider.HookDescription;
        public WorkspaceRole[] DefaultRoles => Array.Empty<WorkspaceRole>();
        public string PromptInstruction => _provider.PromptInstruction;
        public string GetDynamicContext(Workspace.IWorkspace workspace, IReadOnlyList<Cards.IGameEvent> events) => null;
        public IReadOnlyList<McpTool> GetTools() => _provider.GetTools();
    }
}
