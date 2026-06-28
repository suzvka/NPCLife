namespace NPCLife.Framework
{
    /// <summary>
    /// Agent 管道拦截器管理器接口。将静态 AgentPipeline 抽象为可注入实例。
    /// </summary>
    public interface IAgentPipeline
    {
        /// <summary>添加拦截器。priority 越小越先执行。</summary>
        void AddInterceptor(IAgentInterceptor interceptor, int priority = 0);

        /// <summary>注入日志接口。</summary>
        ILogger Logger { set; }
    }
}
