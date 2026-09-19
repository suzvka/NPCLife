using NPCLife.Cards;
using NPCLife.Framework.Llm;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 会话追踪录制器。管线在各关键节点调用此接口记录
    /// 运行全文（事件、消息、工具调用），用于离线分析和 Dashboard 展示。
    ///
    /// 注入方式：由宿主组合根在装配管线时注入。
    /// null 时跳过所有录制调用（零开销）。
    /// </summary>
    public interface ISessionTraceRecorder
    {
        /// <summary>Run 开始时调用。在事件 drain 后、prompt 构建前。</summary>
        void BeginRun(string runId, IReadOnlyList<IGameEvent> events, string userMessage);

        /// <summary>每轮 LLM 调用完成后调用。包含请求消息和响应。</summary>
        void RecordLlmRound(string runId, int round, IReadOnlyList<LlmMessage> requestMessages,
            LlmResponse response);

        /// <summary>每个工具调用完成后调用。</summary>
        void RecordToolCall(string runId, int round, string toolName, string arguments,
            string result, bool cancelled);

        /// <summary>Run 结束时调用（正常完成或异常终止）。</summary>
        void EndRun(string runId, int rounds, int eventsProcessed, bool normalCompletion);
    }
}
