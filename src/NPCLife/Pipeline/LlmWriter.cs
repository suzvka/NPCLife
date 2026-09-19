using NPCLife.Core;
using NPCLife.Prompts;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Script;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 基于 LLM 的写手：全系统唯一热路径 LLM。**单次** <see cref="ILlmService.ChatAsync"/> 调用，
    /// 无工具、无多轮——成本 ∝ 消费。
    ///
    /// 复用既有基础设施：凭证经 <see cref="ICredentialStore"/>（fallback 链由服务内部处理）、
    /// 请求/响应经 <see cref="ILlmService"/>、台词解析经 <see cref="ScriptFormat"/>（与游戏侧消费契约一致）。
    /// 失败/空输出不抛异常，返回失败结果，由 <see cref="UtterancePipeline"/> 降级到 Tier 0。
    /// </summary>
    public sealed class LlmWriter : IWriter
    {
        private readonly ILlmService _llm;
        private readonly ICredentialStore _credentials;
        private readonly WriterConfig _cfg;

        public LlmWriter(ILlmService llm, ICredentialStore credentials, WriterConfig config = null)
        {
            _llm = llm ?? throw new System.ArgumentNullException(nameof(llm));
            _credentials = credentials ?? throw new System.ArgumentNullException(nameof(credentials));
            _cfg = config ?? new WriterConfig();
        }

        public async Task<WriteResult> WriteAsync(MaterialBag bag, CancellationToken ct = default)
        {
            if (bag == null) return WriteResult.Failed("empty bag");

            var creds = _credentials.GetActiveCredentials();
            if (creds == null || creds.Count == 0)
                return WriteResult.Failed("no active LLM credential");

            string model = !string.IsNullOrEmpty(_cfg.Model) ? _cfg.Model
                : (creds[0].ModelNames != null && creds[0].ModelNames.Count > 0 ? creds[0].ModelNames[0] : "");
            if (string.IsNullOrEmpty(model))
                return WriteResult.Failed("no model configured");

            var request = new LlmRequest
            {
                Model = model,
                Temperature = _cfg.Temperature,
                ParallelToolCalls = false, // 无工具
                Messages = new List<LlmMessage>
                {
                    LlmMessage.System(_cfg.SystemPromptOverride ?? PromptConfig.DefaultWriterPrompt),
                    LlmMessage.User(MaterialBagPrompt.BuildUserMessage(bag))
                }
            };

            LlmResponse response;
            try
            {
                response = await _llm.ChatAsync(request, creds, ct).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                return new WriteResult { Success = false, AttemptedLlm = true, Error = "cancelled" };
            }
            catch (System.Exception e)
            {
                return new WriteResult { Success = false, AttemptedLlm = true, Error = "chat exception: " + e.Message };
            }

            if (response == null || !response.IsSuccess)
                return new WriteResult { Success = false, AttemptedLlm = true, Error = response?.Error ?? "null response" };

            var lines = ScriptFormat.Parse(response.Content);
            if (lines == null || lines.Count == 0)
                return new WriteResult { Success = false, AttemptedLlm = true, Error = "no parsable script lines" };

            return new WriteResult
            {
                Success = true,
                AttemptedLlm = true,
                Lines = lines,
                InputTokens = response.UsageInputTokens,
                OutputTokens = response.UsageOutputTokens,
                TotalTokens = response.UsageTotalTokens
            };
        }
    }
}
