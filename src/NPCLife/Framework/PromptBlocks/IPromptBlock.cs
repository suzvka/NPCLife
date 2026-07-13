using NPCLife.Cards;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using System.Collections.Generic;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// 提示词块基接口。每个块有唯一 Id 和自定义分段标题（Header）。
    /// Skill 是 IPromptBlock 的一种实现——每个 Skill 可以同时贡献文本、工具定义和预查询消息。
    /// 
    /// 四种子类型覆盖 LLM 请求的全部静态组成部分：
    ///   ITextPromptBlock      → system prompt 文本
    ///   IToolProviderBlock    → tools 字段（JSON）
    ///   IToolPreQueryBlock    → 模拟已执行的工具调用链（assistant + tool 消息）
    ///   IDialoguePrimingBlock → 模拟历史对话发言（约束模型行为）
    /// </summary>
    public interface IPromptBlock
    {
        /// <summary>块唯一标识。</summary>
        string Id { get; }

        /// <summary>
        /// 分段标题。在拼接 System Prompt 时作为 "## Header" 标题行。
        /// 返回 null 或空字符串表示不产生标题行（如纯工具块）。
        /// </summary>
        string Header { get; }
    }

    /// <summary>
    /// 纯文本提示词块。产出文本内容，拼接到 System Prompt 中。
    /// </summary>
    public interface ITextPromptBlock : IPromptBlock
    {
        /// <summary>获取提示词文本。返回 null 或空字符串表示无文本贡献。</summary>
        string GetContent();
    }

    /// <summary>
    /// 工具提供块。产出一组 MCP 工具定义，序列化后注入 LLM 请求的 tools 字段。
    /// </summary>
    public interface IToolProviderBlock : IPromptBlock
    {
        /// <summary>获取该块提供的全部工具定义。</summary>
        IReadOnlyList<McpToolDefinition> GetTools();
    }

    /// <summary>
    /// 预查询工具块。产出一组 LlmMessage（assistant 含 tool_calls + tool 结果），
    /// 模拟 LLM 已经执行过的工具调用链。注入到 user 消息之后，使模型跳过冗余调用。
    /// </summary>
    public interface IToolPreQueryBlock : IPromptBlock
    {
        /// <summary>
        /// 获取模拟的工具调用消息序列。
        /// </summary>
        /// <param name="events">本轮待处理事件列表，供需要扫描事件内容的块使用（如知识标签查询）。</param>
        IReadOnlyList<LlmMessage> GetMessages(IReadOnlyList<IGameEvent> events);
    }

    /// <summary>
    /// 对话注入块。产出 assistant 角色的历史发言，模拟 LLM 在之前轮次中做出的决策声明。
    /// 注入到 system prompt 之后、user 消息之前。模型对自己历史发言的遵循度远高于 system prompt。
    /// </summary>
    public interface IDialoguePrimingBlock : IPromptBlock
    {
        /// <summary>
        /// 获取模拟的历史对话消息。
        /// </summary>
        /// <param name="events">本轮待处理事件列表，供需要感知事件的块使用。</param>
        IReadOnlyList<LlmMessage> GetMessages(IReadOnlyList<IGameEvent> events);
    }
}
