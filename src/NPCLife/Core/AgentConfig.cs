using NPCLife.Framework.Mcp;

namespace NPCLife.Core
{
    /// <summary>
    /// Agent 的 per-role 配置，由适配层通过 <see cref="AgentConfigFactory"/> 提供。
    /// 
    /// 包含提示词策略和可选的运行时参数覆盖。
    /// 全局基础设施依赖（ILlmService、ICredentialStore、ILogger）不在其中——
    /// 这些由 <see cref="IAgentOrchestrator"/> 构造时统一注入。
    /// </summary>
    public class AgentConfig
    {
        /// <summary>系统提示词构建器。必填。</summary>
        public IPromptBuilder PromptBuilder { get; set; }

        /// <summary>
        /// Agent 多轮工具调用最大轮数（防死循环）。
        /// 若为 null，使用 <see cref="Agent.AgentLoopDependencies.MaxRounds"/> 的全局默认值。
        /// </summary>
        public int? MaxRounds { get; set; }

        /// <summary>
        /// Card 序列化器。若为 null，使用 <see cref="Agent.AgentLoopDependencies.Serializer"/> 的全局默认值。
        /// </summary>
        public ICardSerializer Serializer { get; set; }

        /// <summary>
        /// 以最小配置创建：仅指定 PromptBuilder，其余使用全局默认。
        /// </summary>
        public static AgentConfig FromPromptBuilder(IPromptBuilder promptBuilder)
        {
            return new AgentConfig { PromptBuilder = promptBuilder };
        }
    }
}
