using NPCLife.Framework.Mcp;

namespace NPCLife.Core
{
    /// <summary>
    /// Agent 的 per-role 配置，由适配层通过 <see cref="AgentConfigFactory"/> 提供。
    /// </summary>
    public class AgentConfig
    {
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
        /// 创建默认配置。
        /// </summary>
        public static AgentConfig CreateDefault()
        {
            return new AgentConfig();
        }
    }
}
