using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;

namespace NPCLife.Agent
{
    /// <summary>
    /// AgentLoop 的基础设施依赖与行为配置。
    /// 将原 14 参数构造函数中的全局不变项收归一处，由宿主统一注入。
    /// </summary>
    public struct AgentLoopDependencies
    {
        /// <summary>LLM 异步对话服务（无状态）。必填。</summary>
        public ILlmService Llm;

        /// <summary>凭证存储。提供当前可用的 API 凭证列表。必填。</summary>
        public ICredentialStore CredentialStore;

        /// <summary>日志接口。必填。</summary>
        public ILogger Logger;

        /// <summary>Card 序列化器。可选，默认 CardSerializer.Default。</summary>
        public ICardSerializer Serializer;

        /// <summary>Agent 多轮工具调用最大轮数（防死循环）。默认 10。</summary>
        public int MaxRounds;

        /// <summary>
        /// 轮间过渡消息回调。在每轮工具调用完成后、下一轮 LLM 请求前调用。
        /// 参数为当前 round（1-based），返回要注入的消息列表。
        /// 返回 null 或空列表表示不注入。null = 不启用此功能。
        /// </summary>
        public Func<int, IReadOnlyList<LlmMessage>> GetRoundTransitionMessages;

        /// <summary>
        /// 会话追踪录制器。null 时跳过所有录制调用（零开销）。
        /// </summary>
        public ISessionTraceRecorder TraceRecorder;

        /// <summary>
        /// 创建生产环境默认配置。宿主只需填充 Llm、CredentialStore、Logger 即可工作。
        /// </summary>
        public static AgentLoopDependencies CreateDefault(
            ILlmService llm,
            ICredentialStore credentialStore,
            ILogger logger)
        {
            return new AgentLoopDependencies
            {
                Llm = llm,
                CredentialStore = credentialStore,
                Logger = logger,
                Serializer = CardSerializer.Default,
                MaxRounds = 10
            };
        }
    }
}
