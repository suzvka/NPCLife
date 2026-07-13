using NPCLife.Agent;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Core
{
    /// <summary>
    /// Agent 循环的只读观测句柄。
    /// 
    /// 调用方通过此接口观测 Agent 运行时状态、主动触发运行，
    /// 但无法接触 AgentLoop 内部的信号量、状态机、LLM 请求构建等实现细节。
    /// 
    /// 实例由 <see cref="IAgentOrchestrator.GetAgent(Workspace.WorkspaceRole)"/>
    /// 或 <see cref="IAgentOrchestrator.GetAgent(string)"/> 返回。
    /// 生命周期由 <see cref="IAgentOrchestrator"/> 管理，调用方不应直接 Dispose。
    /// </summary>
    public interface IAgentLoop
    {
        /// <summary>获取当前 Agent 运行状态。</summary>
        AgentRunState State { get; }

        /// <summary>
        /// 外部主动触发 Agent 运行。若当前非空闲则立即返回 <see cref="Task.CompletedTask"/>。
        /// </summary>
        Task TriggerAsync(CancellationToken ct = default);
    }
}
