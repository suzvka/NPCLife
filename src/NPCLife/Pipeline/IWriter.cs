using NPCLife.Framework.Script;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 写手配置。单次生成的采样与访问参数（手工常量，可被宿主覆盖）。
    /// </summary>
    public sealed class WriterConfig
    {
        /// <summary>指定模型名。留空则用首个凭证的第一个模型名。</summary>
        public string Model;

        /// <summary>采样温度。null 用 API 默认。</summary>
        public float? Temperature = 0.9f;

        /// <summary>系统提示词覆盖。留空用内置 WriterPrompt。</summary>
        public string SystemPromptOverride;
    }

    /// <summary>
    /// 写手生成结果。Lines 为台词脚本（投递车道消费）；失败/超时不抛异常，
    /// 由调用方（<see cref="UtterancePipeline"/>）降级到 Tier 0。
    /// </summary>
    public sealed class WriteResult
    {
        /// <summary>是否成功（false 表示需降级到 Tier 0 模板）。</summary>
        public bool Success;

        /// <summary>
        /// 是否实际发起了 LLM 调用。凭证缺失/无模型等前置失败时为 false（未产生成本），
        /// 供管线精确核算 LlmCalls（成本 ∝ 消费）。
        /// </summary>
        public bool AttemptedLlm;

        /// <summary>生成的台词行（成功时非空；失败时为空列表）。</summary>
        public IReadOnlyList<ScriptLine> Lines = System.Array.Empty<ScriptLine>();

        /// <summary>写手自报引用的材料事件 ID（当前可为空，供防重复/审计）。</summary>
        public IReadOnlyList<string> ReferencedEventIds = System.Array.Empty<string>();

        /// <summary>本次调用的 token 消耗（用于成本归因；无则为 null）。</summary>
        public int? InputTokens;
        public int? OutputTokens;
        public int? TotalTokens;

        /// <summary>失败原因（成功时为 null）。</summary>
        public string Error;

        public static WriteResult Failed(string error) => new WriteResult { Success = false, Error = error };
    }

    /// <summary>
    /// 写手接口：全系统唯一热路径 LLM。材料袋 → 台词脚本，单次调用。
    /// 职责边界：可脑补袋内因果、过滤噪声、保持人格一致；不可引入袋外事实。
    /// </summary>
    public interface IWriter
    {
        Task<WriteResult> WriteAsync(MaterialBag bag, CancellationToken ct = default);
    }
}
