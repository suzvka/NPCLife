using NPCLife.Core;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Agent
{
    /// <summary>
    /// <see cref="IAgentOrchestrator"/> 的默认实现。
    /// 
    /// 线程安全：所有公共方法使用细粒度锁保护缓存字典。
    /// 内部 workspace 创建委托给 <see cref="IWorkspaceManager"/>。
    /// </summary>
    public class AgentOrchestrator : IAgentOrchestrator
    {
        private readonly IWorkspaceManager _workspaces;

        // 注册的工厂委托，按角色索引
        private readonly Dictionary<WorkspaceRole, AgentFactory> _factories
            = new Dictionary<WorkspaceRole, AgentFactory>();

        // 按角色缓存的 Agent（Director / Improviser）
        private readonly Dictionary<WorkspaceRole, AgentLoop> _roleAgents
            = new Dictionary<WorkspaceRole, AgentLoop>();
        private readonly object _roleLock = new object();

        // 按工作空间 ID 缓存的 Agent（Screenwriter）
        private readonly Dictionary<string, AgentLoop> _wsAgents
            = new Dictionary<string, AgentLoop>();
        private readonly object _wsLock = new object();

        public AgentOrchestrator(IWorkspaceManager workspaces)
        {
            _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        }

        // ================================================================
        // 注册
        // ================================================================

        public void Register(WorkspaceRole role, AgentFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_factories.ContainsKey(role))
                throw new InvalidOperationException($"AgentFactory for role '{role}' is already registered.");
            _factories[role] = factory;
        }

        // ================================================================
        // 工作空间（不创建 Agent）
        // ================================================================

        public IWorkspace GetOrCreateWorkspace(WorkspaceRole role)
        {
            var actives = _workspaces.GetActive();
            foreach (var ws in actives)
            {
                if (ws.CreatedByRole == role)
                    return ws;
            }
            return _workspaces.Create(null, role);
        }

        // ================================================================
        // Agent 获取（角色维度）
        // ================================================================

        public AgentLoop GetAgent(WorkspaceRole role)
        {
            // 快速路径：已缓存
            lock (_roleLock)
            {
                if (_roleAgents.TryGetValue(role, out var cached) && cached != null)
                    return cached;
            }

            if (!_factories.TryGetValue(role, out var factory))
                return null;

            var ws = GetOrCreateWorkspace(role);
            if (ws == null) return null;

            var agent = factory(ws, _workspaces);
            if (agent == null) return null;

            lock (_roleLock)
            {
                // 重检：并发调用可能已创建
                if (_roleAgents.TryGetValue(role, out var existing) && existing != null)
                {
                    agent.Dispose();
                    return existing;
                }
                _roleAgents[role] = agent;
                return agent;
            }
        }

        // ================================================================
        // Agent 获取（工作空间维度）
        // ================================================================

        public AgentLoop GetAgent(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return null;

            // 快速路径：已缓存
            lock (_wsLock)
            {
                if (_wsAgents.TryGetValue(workspaceId, out var cached) && cached != null)
                    return cached;
            }

            var ws = _workspaces.Get(workspaceId);
            if (ws == null) return null;

            if (!_factories.TryGetValue(ws.CreatedByRole, out var factory))
                return null;

            var agent = factory(ws, _workspaces);
            if (agent == null) return null;

            lock (_wsLock)
            {
                if (_wsAgents.TryGetValue(workspaceId, out var existing) && existing != null)
                {
                    agent.Dispose();
                    return existing;
                }
                _wsAgents[workspaceId] = agent;
                return agent;
            }
        }

        // ================================================================
        // 生命周期回调
        // ================================================================

        public void OnWorkspaceReady(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;
            var ws = _workspaces.Get(workspaceId);
            if (ws == null) return;

            switch (ws.CreatedByRole)
            {
                case WorkspaceRole.Director:
                case WorkspaceRole.Improviser:
                    GetAgent(ws.CreatedByRole);
                    break;
                default:
                    GetAgent(workspaceId);
                    break;
            }
        }

        // ================================================================
        // 销毁
        // ================================================================

        public void DisposeAgent(string workspaceId)
        {
            if (string.IsNullOrEmpty(workspaceId)) return;

            lock (_wsLock)
            {
                if (_wsAgents.TryGetValue(workspaceId, out var agent))
                {
                    agent?.Dispose();
                    _wsAgents.Remove(workspaceId);
                }
            }

            // 同时检查角色缓存（Screenwriter 角色维度无缓存，仅 Director/Improviser 有）
            lock (_roleLock)
            {
                var toRemove = new List<WorkspaceRole>();
                foreach (var kv in _roleAgents)
                {
                    if (kv.Value != null)
                    {
                        // AgentLoop 内部持有 workspace 引用，通过反射无法直接获取 workspaceId，
                        // 但 Director/Improviser 各只有一个，按角色清理即可。
                        // 此处仅在 RebuildAll/DisposeAll 中批量清理时触发，
                        // 单点清理由 DisposeAgent(wsId) 处理 Screenwriter。
                    }
                }
            }
        }

        public void RebuildAll()
        {
            // Phase 1: 销毁全部
            lock (_roleLock)
            {
                foreach (var kv in _roleAgents)
                    kv.Value?.Dispose();
                _roleAgents.Clear();
            }

            lock (_wsLock)
            {
                foreach (var kv in _wsAgents)
                    kv.Value?.Dispose();
                _wsAgents.Clear();
            }

            // Phase 2: 为活跃工作空间重建 Agent
            var actives = _workspaces.GetActive();
            foreach (var ws in actives)
                OnWorkspaceReady(ws.Id);
        }

        public void DisposeAll()
        {
            lock (_roleLock)
            {
                foreach (var kv in _roleAgents)
                    kv.Value?.Dispose();
                _roleAgents.Clear();
            }

            lock (_wsLock)
            {
                foreach (var kv in _wsAgents)
                    kv.Value?.Dispose();
                _wsAgents.Clear();
            }
        }
    }
}
