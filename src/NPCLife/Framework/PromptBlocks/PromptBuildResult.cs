using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// 提示词构建结果。包含 LLM 请求的全部静态组成部分。
    /// 一次 Build() 调用产出 System Prompt、注入消息和工具定义 JSON。
    /// </summary>
    public class PromptBuildResult
    {
        /// <summary>拼接完成的 System Prompt 文本。</summary>
        public string SystemPrompt { get; set; } = "";

        /// <summary>
        /// 对话注入消息（IDialoguePrimingBlock 产出）。在 system 之后、user 之前注入。
        /// </summary>
        public IReadOnlyList<LlmMessage> PrimingMessages { get; set; } = System.Array.Empty<LlmMessage>();

        /// <summary>
        /// 预查询工具消息（IToolPreQueryBlock 产出）。在 user 消息之后注入。
        /// </summary>
        public IReadOnlyList<LlmMessage> PreQueriedMessages { get; set; } = System.Array.Empty<LlmMessage>();

        /// <summary>
        /// [已废弃] 工具定义 JSON 字符串。
        /// AgentLoop 不再读取此字段，工具统一从 McpSkillRegistry 获取。
        /// 此字段保留仅为编译兼容。
        /// </summary>
        [Obsolete("Tools are now managed by AgentLoop via McpSkillRegistry. This field is ignored.")]
        public string ToolsJson { get; set; } = "[]";
    }
}
