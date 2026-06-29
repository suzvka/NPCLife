using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM 对话请求。内部统一格式，适配器负责转换为 API 特定格式。
    /// 零外部依赖。
    /// </summary>
    public class LlmRequest
    {
        /// <summary>模型名称。</summary>
        public string Model { get; set; } = "";

        /// <summary>对话消息列表。</summary>
        public List<LlmMessage> Messages { get; set; } = new List<LlmMessage>();

        /// <summary>工具定义 JSON 数组字符串。MCP 标准 tools 格式。</summary>
        public string ToolsJson { get; set; }

        /// <summary>采样温度，0~2。null 表示使用 API 默认值。</summary>
        public float? Temperature { get; set; }

        /// <summary>
        /// 是否允许模型在一次响应中发起多个并行工具调用。
        /// 启用后，互不依赖的查询类工具（如 get_colony_overview + get_recent_events）
        /// 可在同一轮中一并返回，大幅减少上下文重放次数。
        /// 对应 OpenAI API 的 parallel_tool_calls 参数。
        /// </summary>
        public bool ParallelToolCalls { get; set; } = true;

        /// <summary>
        /// 验证请求是否合法。
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Model)
                && Messages != null
                && Messages.Count > 0;
        }

        /// <summary>
        /// 快捷构造：单条 user 消息请求。
        /// </summary>
        public static LlmRequest SinglePrompt(string model, string userMessage)
        {
            return new LlmRequest
            {
                Model = model ?? "",
                Messages = new List<LlmMessage> { LlmMessage.User(userMessage) }
            };
        }
    }
}
