using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// Anthropic Claude Extended Thinking 的思考内容块。
    /// 包含思考文本和签名，后续请求中必须原样传回。
    /// </summary>
    public class ThinkingBlock
    {
        /// <summary>思考过程的文本内容。</summary>
        public string Thinking { get; set; } = "";

        /// <summary>API 返回的签名，用于验证思考完整性。</summary>
        public string Signature { get; set; } = "";
    }

    /// <summary>
    /// LLM 对话消息。内部统一格式，适配器负责转换为 API 特定格式。
    /// </summary>
    public class LlmMessage
    {
        /// <summary>角色：system / user / assistant / tool。</summary>
        public string Role { get; set; } = "user";

        /// <summary>消息文本内容。</summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// 推理/思考内容（thinking mode）。从 API 响应中解析，序列化时原样传回。
        /// 某些模型（如 DeepSeek-R1）要求后续请求中必须包含此字段。
        /// </summary>
        public string ReasoningContent { get; set; }

        /// <summary>
        /// Anthropic Claude Extended Thinking 的思考块列表。
        /// 从 content 数组中的 thinking 块解析，序列化时插入到 content 数组。
        /// </summary>
        public List<ThinkingBlock> ThinkingBlocks { get; set; }

        /// <summary>工具调用 ID（tool 角色时使用）。</summary>
        public string ToolCallId { get; set; }

        /// <summary>工具调用列表（assistant 角色请求工具时使用）。</summary>
        public List<LlmToolCall> ToolCalls { get; set; }

        /// <summary>快捷构造 user 消息。</summary>
        public static LlmMessage User(string content)
        {
            return new LlmMessage { Role = "user", Content = content ?? "" };
        }

        /// <summary>快捷构造 assistant 消息。</summary>
        public static LlmMessage Assistant(string content)
        {
            return new LlmMessage { Role = "assistant", Content = content ?? "" };
        }

        /// <summary>快捷构造 system 消息。</summary>
        public static LlmMessage System(string content)
        {
            return new LlmMessage { Role = "system", Content = content ?? "" };
        }

        /// <summary>快捷构造 tool 结果消息。</summary>
        public static LlmMessage ToolResult(string toolCallId, string content)
        {
            return new LlmMessage
            {
                Role = "tool",
                Content = content ?? "",
                ToolCallId = toolCallId
            };
        }

        /// <summary>快捷构造 assistant 消息（含工具调用请求）。</summary>
        public static LlmMessage AssistantWithTools(List<LlmToolCall> toolCalls)
        {
            return new LlmMessage
            {
                Role = "assistant",
                Content = "",
                ToolCalls = toolCalls
            };
        }
    }
}
