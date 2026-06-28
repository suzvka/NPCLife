using NPCLife.Framework;

namespace NPCLife.Infrastructure
{
    /// <summary>委托到静态 AgentPipeline 的 IAgentPipeline 实现。</summary>
    internal class StaticAgentPipelineAdapter : IAgentPipeline
    {
        public void AddInterceptor(IAgentInterceptor interceptor, int priority = 0)
            => AgentPipeline.AddInterceptor(interceptor, priority);

        public ILogger Logger
        {
            set => AgentPipeline.Logger = value;
        }
    }
}
