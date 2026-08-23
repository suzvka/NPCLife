using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using NPCLife.Framework.PromptBlocks;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NPCLife.Agent
{
    /// <summary>
    /// Agent 运行状态枚举。
    /// </summary>
    public enum AgentRunState
    {
        Idle,
        DrainingEvents,
        BuildingRequest,
        CallingLlm,
        ExecutingTools,
        AppendingToolResults,
        Finishing,
        Error
    }

    /// <summary>
    /// Agent 循环。
    /// 通过订阅 IEventLog.OnThresholdReached 被动激活。
    ///
    /// 生命周期：
    /// 1. 池子通知阈值达到 → OnPoolChanged()
    /// 2. Drain → Prompt → LLM → 工具调用循环
    /// 3. 循环结束 → 重置状态，等待下次通知
    ///
    /// 运行时采用显式状态机，以 SemaphoreSlim 防重入，
    /// CancellationToken 贯穿整条链路，失败路径统一。
    /// </summary>
    internal class AgentLoop : IAgentLoop, IDisposable
    {
        private readonly IWorkspace _workspace;
        private readonly IEventLog _pool;
        private readonly ILlmService _llm;
        private readonly ICredentialStore _credentialStore;
        private readonly ILogger _logger;
        private readonly DefaultPromptBuilder _promptBuilder;
        private readonly int _maxRounds;
        private readonly ICardSerializer _serializer;
        private readonly Action _unsubscribe;
        private readonly Func<int, IReadOnlyList<LlmMessage>> _getRoundTransitionMessages;
        private readonly ISessionTraceRecorder _traceRecorder;
        private readonly AgentRole _agentRole;
        private string _currentModelName;
        private string _currentToolsJson;
        private volatile AgentRunState _state = AgentRunState.Idle;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private static long _runSeq;
        private string _currentRunId;
        private Task _currentRun;
        private string _metricsSessionId;

        private int _round;
        private List<LlmMessage> _messages;
        private IReadOnlyList<IGameEvent> _drained;

        /// <summary>
        /// 创建 AgentLoop 并自动订阅工作空间事件池的 OnThresholdReached 事件。
        /// 系统提示词由 PromptBlockRegistry 聚合所有激活的 ISkillModule 文本块生成，
        /// 工具由 McpSkillRegistry 从激活技能中统一收集（system 技能优先）。
        /// </summary>
        /// <param name="workspace">绑定的工作空间。Agent 从 ws.EventPool drain 事件，从 ws.SkillSlot 获取工具集，从 ws.ModelRefs 解析凭证。</param>
        /// <param name="deps">基础设施依赖（LLM 服务、凭证、日志等）与行为配置（最大轮数、温度）。由宿主统一注入。</param>
        public AgentLoop(
            IWorkspace workspace,
            AgentLoopDependencies deps)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _pool = workspace.EventPool ?? throw new ArgumentException("workspace.EventPool is null", nameof(workspace));
            _llm = deps.Llm ?? throw new ArgumentNullException(nameof(deps.Llm));
            _credentialStore = deps.CredentialStore ?? throw new ArgumentNullException(nameof(deps.CredentialStore));
            _logger = deps.Logger ?? throw new ArgumentNullException(nameof(deps.Logger));
            _promptBuilder = new DefaultPromptBuilder(_logger);
            _maxRounds = deps.MaxRounds > 0 ? deps.MaxRounds : 10;
            _serializer = deps.Serializer ?? CardSerializer.Default;
            _getRoundTransitionMessages = deps.GetRoundTransitionMessages;
            _traceRecorder = deps.TraceRecorder;
            _agentRole = (AgentRole)workspace.CreatedByRole;
            _pool.OnThresholdReached += OnPoolChanged;
            _unsubscribe = () => _pool.OnThresholdReached -= OnPoolChanged;
        }

        // ================================================================
        // 唯一入口
        // ================================================================

        /// <summary>
        /// 获取有效技能 ID 列表（含 system）。用于工具查询。
        /// system 技能始终包含且优先。
        /// </summary>
        private string[] GetEffectiveSkillIds()
        {
            var ids = new List<string> { McpSkillRegistry.SystemSkillId };
            var activeIds = _workspace.SkillSlot?.ActiveSkillIds;
            if (activeIds != null && activeIds.Count > 0)
                ids.AddRange(activeIds);
            return ids.ToArray();
        }

        private void OnPoolChanged()
        {
            if (_state != AgentRunState.Idle) return;
            if (_pool.PendingCount == 0) return;

            // 非阻塞获取信号量
            if (!_gate.Wait(0)) return;

            try
            {
                _currentRun = RunOnceAsync(_disposeCts.Token);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        // ================================================================
        // 公共触发入口
        // ================================================================

        /// <summary>
        /// 外部主动触发 Agent 运行。若当前非空闲则立即返回。
        /// </summary>
        public Task TriggerAsync(CancellationToken ct = default)
        {
            if (_state != AgentRunState.Idle) return Task.CompletedTask;
            if (!_gate.Wait(0)) return Task.CompletedTask;

            var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            try
            {
                _currentRun = RunOnceAsync(linked.Token);
                return _currentRun;
            }
            catch
            {
                linked.Dispose();
                _gate.Release();
                throw;
            }
        }

        // ================================================================
        // Agent Loop — 显式状态机主循环
        // ================================================================

        private async Task RunOnceAsync(CancellationToken ct)
        {
            string runId = $"run-{Interlocked.Increment(ref _runSeq)}";
            _currentRunId = runId;
            _round = 0;

            try
            {
                // --- DrainingEvents ---
                _state = AgentRunState.DrainingEvents;
                ErrorHandler.BeginTrace();
                EventBus.Publish(FrameworkEvents.AgentActivated, EventArg.WithPayload(
                    ("runId", runId),
                    ("pendingCount", _pool.PendingCount.ToString()),
                    ("totalImportance", _pool.TotalImportance.ToString())
                ));

                _drained = _pool.DrainPending();
                if (_drained.Count == 0)
                {
                    _state = AgentRunState.Idle;
                    return;
                }

                _logger.Message($"[NPCLife.Agent] Activated with {_drained.Count} events (importance={_pool.TotalImportance}, runId={runId})");

                // --- BuildingRequest ---
                _state = AgentRunState.BuildingRequest;
                string userMessage = BuildUserMessage(_drained);

                // 由 DefaultPromptBuilder 聚合 PromptBlockRegistry 中的文本块
                // （知识上下文由 KnowledgePreQueryBlock 作为 IToolPreQueryBlock 自动注入，无需管线拦截器）
                var buildResult = _promptBuilder.Build(_workspace, _drained);

                // 拼接动态上下文（来自各 ISkillModule 的 GetDynamicContext）
                var systemPrompt = BuildSystemPromptWithDynamicContext(buildResult);

                // 工具由 McpSkillRegistry 统一收集（system 优先，自动去重）
                _currentToolsJson = McpSkillRegistry.GetActiveToolsJson(GetEffectiveSkillIds());

                _logger.Message($"[NPCLife.Agent.DIAG] Build result: systemPromptLen={systemPrompt?.Length ?? 0}, toolsJsonLen={_currentToolsJson.Length}, toolsPreview={(_currentToolsJson.Length > 150 ? _currentToolsJson.Substring(0, 150) : _currentToolsJson)}, primingMsgCount={buildResult.PrimingMessages?.Count ?? 0}, preQueriedMsgCount={buildResult.PreQueriedMessages?.Count ?? 0}");

                _messages = new List<LlmMessage>
                {
                    LlmMessage.System(systemPrompt),
                };
                if (buildResult.PrimingMessages != null && buildResult.PrimingMessages.Count > 0)
                    _messages.AddRange(buildResult.PrimingMessages);
                _messages.Add(LlmMessage.User(userMessage));
                if (buildResult.PreQueriedMessages != null && buildResult.PreQueriedMessages.Count > 0)
                    _messages.AddRange(buildResult.PreQueriedMessages);

                // 会话追踪录制：开始记录
                _traceRecorder?.BeginRun(runId, _drained, userMessage);

                // 运行时度量：开始新会话
                _metricsSessionId = RuntimeMetrics.BeginSession(_agentRole);

                // 解析凭证：仅使用当前选中模型，不进行回退
                var credentials = ResolveCredentials();
                if (credentials.Count == 0)
                {
                    _logger.Warning($"[NPCLife.Agent] No model configured for workspace '{_workspace.Id}'. Skipping round — drained events discarded. (runId={runId})");
                    _drained = null;
                    _state = AgentRunState.Idle;
                    return;
                }

                _currentModelName = (credentials[0].ModelNames != null && credentials[0].ModelNames.Count > 0) 
                    ? credentials[0].ModelNames[0] : "";

                // --- LLM + Tool 循环 ---
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Transcript 验证：每轮 LLM 调用前检查消息历史结构
                    var validation = TranscriptValidator.Validate(_messages);
                    if (!validation.IsValid)
                        throw new InvalidOperationException(
                            $"Transcript validation failed: {validation.Reason}");

                    // --- CallingLlm ---
                    _state = AgentRunState.CallingLlm;
                    var request = BuildLlmRequest();

                    EventBus.Publish(FrameworkEvents.LlmRequestSent, EventArg.WithPayload(
                        ("runId", runId),
                        ("round", _round.ToString()),
                        ("messageCount", _messages.Count.ToString())
                    ));

                    var response = await _llm.ChatAsync(request, credentials, ct);

                    if (response == null || !response.IsSuccess)
                        throw new InvalidOperationException(response?.Error ?? "null response");

                    // 会话追踪：记录本轮 LLM 交互
                    _traceRecorder?.RecordLlmRound(runId, _round, _messages, response);

                    EventBus.Publish(FrameworkEvents.LlmResponseReceived, EventArg.WithPayload(
                        ("runId", runId),
                        ("hasToolCalls", response.HasToolCalls.ToString()),
                        ("contentLength", (response.Content?.Length ?? 0).ToString()),
                        ("inputTokens", (response.UsageInputTokens?.ToString() ?? "")),
                        ("outputTokens", (response.UsageOutputTokens?.ToString() ?? "")),
                        ("cacheReadTokens", (response.UsageCacheReadTokens?.ToString() ?? "")),
                        ("model", response.Model ?? "")
                    ));

                    if (!response.HasToolCalls)
                    {
                        // 纯文本回复：追加唯一 assistant 消息，结束循环
                        _messages = AppendAssistantTurn(_messages, response, null).ToList();
                        break;
                    }

                    _round++;
                    if (_round >= _maxRounds)
                    {
                        _logger.Warning($"[NPCLife.Agent] Reached max rounds ({_maxRounds}). Ending loop.");
                        // 达到上限时仍追加 assistant（保持 transcript 完整性），然后退出
                        _messages = AppendAssistantTurn(_messages, response, null).ToList();
                        break;
                    }

                    // --- ExecutingTools ---
                    _state = AgentRunState.ExecutingTools;
                    var toolResults = new List<(string id, string result)>();
                    var aborted = false;

                    McpSkillRegistry.CurrentWorkspaceId.Value = _pool.WorkspaceId;
                    McpSkillRegistry.RoundFinished.Value = false;
                    try
                    {
                        foreach (var tc in response.ToolCalls)
                        {
                            _logger.Message($"[NPCLife.Agent] [run-{runId}][round-{_round}] Tool call: {tc.Name}({tc.Arguments})");

                            EventBus.Publish(FrameworkEvents.ToolInvoking, EventArg.WithPayload(
                                ("runId", runId),
                                ("toolName", tc.Name), ("round", _round.ToString())
                            ));

                            string result = McpSkillRegistry.InvokeTool(GetEffectiveSkillIds(), tc.Name, tc.Arguments);
                            bool toolSuccess = !IsErrorResult(result);

                            // 运行时度量：记录工具调用
                            RuntimeMetrics.RecordToolCall(_metricsSessionId, tc.Name, toolSuccess);

                            // 会话追踪：记录工具调用
                            _traceRecorder?.RecordToolCall(runId, _round, tc.Name, tc.Arguments, result, cancelled: false);

                            EventBus.Publish(FrameworkEvents.ToolInvoked, EventArg.WithPayload(
                                ("runId", runId),
                                ("toolName", tc.Name), ("resultLength", (result?.Length ?? 0).ToString())
                            ));

                            toolResults.Add((tc.Id, result));

                            _logger.Message($"[NPCLife.Agent] [run-{runId}][round-{_round}] Tool result ({tc.Name}): {TruncateResult(result)}");

                            if (McpSkillRegistry.AbortRequested.Value)
                            {
                                _logger.Message($"[NPCLife.Agent] Abort requested by tool '{tc.Name}'. Stopping.");
                                aborted = true;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        McpSkillRegistry.CurrentWorkspaceId.Value = null;
                        McpSkillRegistry.AbortRequested.Value = false;
                        McpSkillRegistry.RoundFinished.Value = false;
                    }

                    // —— AppendingToolResults ——
                    // 每个 LLM 响应产生一条 assistant 消息（含 content 和 tool_calls），
                    // 后跟各 tool 的返回结果消息。确保单轮多工具调用时结构正确。
                    _state = AgentRunState.AppendingToolResults;
                    _messages = AppendAssistantTurn(_messages, response, toolResults).ToList();

                    // 轮间过渡钩子：注入轮间消息（如 "别开新对话" 警告）
                    if (_getRoundTransitionMessages != null)
                    {
                        var transitionMsgs = _getRoundTransitionMessages(_round);
                        if (transitionMsgs != null && transitionMsgs.Count > 0)
                            _messages.AddRange(transitionMsgs);
                    }

                    EventBus.Publish(FrameworkEvents.AgentRoundComplete, EventArg.WithPayload(
                        ("runId", runId),
                        ("round", _round.ToString()),
                        ("toolCallCount", response.ToolCalls.Count.ToString())
                    ));

                    if (aborted) break;
                    if (McpSkillRegistry.RoundFinished.Value) break;
                }

                // --- Finishing ---
                FinishOnce(runId, normalCompletion: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                FailAndRequeue(runId, "cancelled");
            }
            catch (Exception ex)
            {
                FailAndRequeue(runId, ex.Message);
            }
            finally
            {
                _currentRunId = null;
                _state = AgentRunState.Idle;
                _gate.Release();

                // 兜底：执行期间可能有事件到达但 OnThresholdReached 通知被丢弃，
                // 主动检查并重试激活。释放 gate 后再检查，确保 OnPoolChanged 能获取信号量。
                if (_pool.PendingCount > 0)
                    OnPoolChanged();
            }
        }

        // ================================================================
        // 统一成功/失败路径
        // ================================================================

        private void FinishOnce(string runId, bool normalCompletion)
        {
            _state = AgentRunState.Finishing;
            int count = _drained?.Count ?? 0;
            int rounds = _round;
            _drained = null;
            _messages = null;

            _logger.Message($"[NPCLife.Agent] Loop complete. {count} events processed. (runId={runId})");

            // 运行时度量：记录循环完成
            RuntimeMetrics.RecordLoopFinished(rounds, count, normalCompletion, _agentRole);
            RuntimeMetrics.EndSession(_metricsSessionId);
            _metricsSessionId = null;

            // 会话追踪：结束录制
            _traceRecorder?.EndRun(runId, rounds, count, normalCompletion);

            ErrorHandler.EndTrace();
            EventBus.Publish(FrameworkEvents.AgentLoopFinished, EventArg.WithPayload(
                ("runId", runId),
                ("rounds", rounds.ToString()),
                ("eventsProcessed", count.ToString()),
                ("normalCompletion", normalCompletion.ToString())
            ));
        }

        private void FailAndRequeue(string runId, string error)
        {
            _state = AgentRunState.Error;
            _logger.Warning($"[NPCLife.Agent] LLM error: {error}. Drained events discarded to prevent retry loop. (runId={runId})");
            ErrorHandler.ReportError("AgentLoop", error, new Dictionary<string, string>
            {
                {"runId", runId},
                {"round", _round.ToString()},
                {"drainedCount", (_drained?.Count ?? 0).ToString()}
            });

            int count = _drained?.Count ?? 0;
            _drained = null;
            _messages = null;

            // 运行时度量：记录失败
            RuntimeMetrics.RecordLoopFinished(_round, count, normalCompletion: false, _agentRole);
            RuntimeMetrics.EndSession(_metricsSessionId);
            _metricsSessionId = null;

            // 会话追踪：结束录制
            _traceRecorder?.EndRun(runId, _round, count, normalCompletion: false);

            ErrorHandler.EndTrace();
            EventBus.Publish(FrameworkEvents.AgentLoopFinished, EventArg.WithPayload(
                ("runId", runId),
                ("rounds", _round.ToString()),
                ("error", error ?? "unknown")
            ));
        }

        // ================================================================
        // Transcript 追加（纯函数，同一 response 只生成一条 assistant）
        // ================================================================

        /// <summary>
        /// 将一个完整的 assistant turn 追加到消息历史，返回新列表。
        /// 约束：同一个 <paramref name="response"/> 只生成一条 assistant message。
        /// 若有 <paramref name="toolResults"/>，assistant 带 tool_calls，后跟每条 tool 结果。
        /// </summary>
        private static IReadOnlyList<LlmMessage> AppendAssistantTurn(
            IReadOnlyList<LlmMessage> history,
            LlmResponse response,
            IReadOnlyList<(string id, string result)> toolResults)
        {
            var result = new List<LlmMessage>(history.Count + 1 + (toolResults?.Count ?? 0));
            result.AddRange(history);

            if (toolResults != null && toolResults.Count > 0)
            {
                // 有工具调用：一条 assistant（content + tool_calls）+ N 条 tool 结果
                result.Add(new LlmMessage
                {
                    Role = "assistant",
                    Content = response.Content ?? "",
                    ToolCalls = response.ToolCalls,
                    ReasoningContent = response.ReasoningContent,
                    ThinkingBlocks = response.ThinkingBlocks
                });

                foreach (var (id, toolResult) in toolResults)
                    result.Add(LlmMessage.ToolResult(id, toolResult));
            }
            else
            {
                // 纯文本回复：一条 assistant（仅 content，无 tool_calls）
                result.Add(new LlmMessage
                {
                    Role = "assistant",
                    Content = response.Content ?? "",
                    ReasoningContent = response.ReasoningContent,
                    ThinkingBlocks = response.ThinkingBlocks
                });
            }

            return result;
        }

        // ================================================================
        // 请求构建
        // ================================================================

        private LlmRequest BuildLlmRequest()
        {
            return new LlmRequest
            {
                Model = _currentModelName ?? "",
                Messages = _messages,
                ToolsJson = _currentToolsJson ?? "[]"
            };
        }

        // ================================================================
        // Prompt 构造
        // ================================================================

        private string BuildUserMessage(IReadOnlyList<IGameEvent> events)
        {
            var sb = new StringBuilder();

            sb.AppendLine("## 待处理事件");
            sb.AppendLine();
            sb.AppendLine(_serializer.SerializeEventList(events));

            return sb.ToString();
        }

        /// <summary>
        /// 拼接 DefaultPromptBuilder 产出的 SystemPrompt 与各 ISkillModule 的动态上下文。
        /// </summary>
        private string BuildSystemPromptWithDynamicContext(PromptBuildResult buildResult)
        {
            var sb = new StringBuilder(buildResult.SystemPrompt ?? "");

            var effectiveIds = GetEffectiveSkillIds();
            var modules = McpSkillRegistry.GetActiveSkillModules(effectiveIds);
            if (modules != null && modules.Count > 0)
            {
                foreach (var module in modules)
                {
                    try
                    {
                        var dynamicCtx = module.GetDynamicContext(_workspace, _drained);
                        if (!string.IsNullOrEmpty(dynamicCtx))
                        {
                            sb.AppendLine();
                            sb.AppendLine("---");
                            sb.AppendLine();
                            sb.Append(dynamicCtx);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning($"[NPCLife.Agent] GetDynamicContext failed for skill '{module.Id}': {ex.Message}");
                    }
                }
            }

            return sb.ToString();
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static string TruncateResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return "(empty)";
            return result.Length > 200 ? result.Substring(0, 200) + "..." : result;
        }

        private static bool IsErrorResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return false;
            string trimmed = result.TrimStart();
            return trimmed.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("{\"error:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>获取当前运行状态。</summary>
        public AgentRunState State => _state;

        // ================================================================
        // IDisposable
        // ================================================================

        // ================================================================
        // 凭证解析
        // ================================================================

        /// <summary>
        /// 解析凭证。读取工作空间的实时 CurrentModel，
        /// 从凭证的 ModelNames 列表中定位目标模型。
        /// </summary>
        private IReadOnlyList<LlmCredential> ResolveCredentials()
        {
            var modelJson = _workspace.CurrentModel;
            var current = ParseSingleRef(modelJson);
            if (current == null)
                return Array.Empty<LlmCredential>();

            var resolved = _credentialStore.Resolve(current.Value.Cred, null);
            if (resolved == null)
                return Array.Empty<LlmCredential>();

            // 从凭证的模型列表中复制指定模型名
            resolved.ModelNames = new List<string> { current.Value.Model };
            return new[] { resolved };
        }

        /// <summary>
        /// 解析模型引用 JSON 字符串为 (凭证名, 模型名) 列表。
        /// 格式: [{"cred":"primary","model":"gpt-4o"},...]
        /// </summary>
        private static List<(string Cred, string Model)> ParseModelRefs(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                var objects = JsonParser.ParseObjectArray(json);
                if (objects == null || objects.Count == 0) return null;

                var result = new List<(string, string)>();
                foreach (var obj in objects)
                {
                    var cred = obj.TryGetValue("cred", out var c) ? c : null;
                    var model = obj.TryGetValue("model", out var m) ? m : null;
                    if (!string.IsNullOrEmpty(cred) && !string.IsNullOrEmpty(model))
                        result.Add((cred, model));
                }
                return result.Count > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析单个模型引用 JSON 字符串。
        /// 格式: {"cred":"primary","model":"gpt-4o"}
        /// </summary>
        private static (string Cred, string Model)? ParseSingleRef(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var dict = JsonParser.ParseDict(json);
                var cred = dict.TryGetValue("cred", out var c) ? c : null;
                var model = dict.TryGetValue("model", out var m) ? m : null;
                if (!string.IsNullOrEmpty(cred) && !string.IsNullOrEmpty(model))
                    return (cred, model);
            }
            catch { }
            return null;
        }

        /// <summary>取消事件订阅、cancel 正在运行的任务并等待安全结束。</summary>
        public void Dispose()
        {
            _unsubscribe?.Invoke();

            // 发送取消信号
            try { _disposeCts.Cancel(); } catch { }

            // 同步等待当前运行结束（有超时保护）
            var run = _currentRun;
            if (run != null)
            {
                try { run.Wait(TimeSpan.FromSeconds(5)); } catch { }
            }

            _disposeCts.Dispose();
            _gate.Dispose();
            _drained = null;
            _messages = null;
            _state = AgentRunState.Idle;
        }
    }
}
