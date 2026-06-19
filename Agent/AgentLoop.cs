using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
using System;
using System.Collections.Generic;
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
    /// Agent 循环。纯逻辑组件，零游戏引擎依赖。
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
    public class AgentLoop : IDisposable
    {
        private readonly IEventLog _pool;
        private readonly ILlmService _llm;
        private readonly ICredentialRegistry _credentialRegistry;
        private readonly ILogger _logger;
        private readonly string _systemPrompt;
        private readonly string[] _skillIds;
        private readonly int _maxRounds;
        private readonly ICardSerializer _serializer;
        private readonly Action _unsubscribe; // 取消事件订阅的委托
        private readonly Func<string> _contextProvider;
        private readonly IKnowledgeBase _knowledgeBase;
        private readonly float _temperature;
        private readonly string _modelAlias; // 关联的模型代号

        // 状态机字段
        private volatile AgentRunState _state = AgentRunState.Idle;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private static long _runSeq;
        private string _currentRunId;
        private Task _currentRun;

        private int _round;
        private List<LlmMessage> _messages;
        private IReadOnlyList<IGameEvent> _drained;

        /// <summary>
        /// 创建 AgentLoop 并自动订阅池子的 OnThresholdReached 事件。
        /// </summary>
        /// <param name="pool">事件池。Agent 从该池 drain 事件。</param>
        /// <param name="llm">LLM 异步对话服务（无状态）。</param>
        /// <param name="credentialRegistry">凭证注册表。提供当前可用的 API 凭证列表。</param>
        /// <param name="systemPrompt">系统提示词。</param>
        /// <param name="skillIds">激活的 Skill ID 列表（MCP 工具集）。</param>
        /// <param name="maxRounds">最大工具调用轮数（防死循环）。</param>
        /// <param name="logger">日志接口。</param>
        /// <param name="serializer">Card 序列化器（可选，默认使用 CardSerializer.Default）。</param>
        /// <param name="contextProvider">动态上下文提供者（可选）。每次激活时调用，返回值追加到用户消息末尾。</param>
        /// <param name="knowledgeBase">知识库（可选）。Agent 激活时收集事件关键词去重后批量查询，命中结果注入提示词。</param>
        /// <param name="temperature">LLM 采样温度（0~2），默认 0.7。</param>
        /// <param name="modelAlias">关联的模型代号（如 "primary"），用于从 Registry 解析凭证。默认 "primary"。</param>
        public AgentLoop(
            IEventLog pool,
            ILlmService llm,
            ICredentialRegistry credentialRegistry,
            string systemPrompt,
            string[] skillIds,
            int maxRounds,
            ILogger logger,
            ICardSerializer serializer = null,
            Func<string> contextProvider = null,
            IKnowledgeBase knowledgeBase = null,
            float temperature = 0.7f,
            string modelAlias = "primary")
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _credentialRegistry = credentialRegistry ?? throw new ArgumentNullException(nameof(credentialRegistry));
            _systemPrompt = systemPrompt ?? "";
            _skillIds = skillIds ?? Array.Empty<string>();
            _maxRounds = maxRounds;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serializer = serializer ?? CardSerializer.Default;
            _contextProvider = contextProvider;
            _knowledgeBase = knowledgeBase;
            _temperature = temperature;
            _modelAlias = modelAlias ?? "primary";

            // 订阅池子事件——唯一激活路径
            _pool.OnThresholdReached += OnPoolChanged;
            _unsubscribe = () => _pool.OnThresholdReached -= OnPoolChanged;
        }

        // ================================================================
        // 唯一入口
        // ================================================================

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
                _messages = new List<LlmMessage>
                {
                    LlmMessage.System(_systemPrompt),
                    LlmMessage.User(BuildUserMessage(_drained))
                };

                // 获取凭证
                var credentials = _credentialRegistry.GetActiveCredentials();
                if (credentials.Count == 0)
                    throw new InvalidOperationException("No active credentials configured");

                // --- LLM + Tool 循环 ---
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // --- CallingLlm ---
                    _state = AgentRunState.CallingLlm;
                    var request = BuildLlmRequest();

                    // 管道拦截：LLM 请求前
                    var llmCtx = new LlmContext { Request = request };
                    AgentPipeline.RunBeforeLlm(llmCtx);

                    EventBus.Publish(FrameworkEvents.LlmRequestSent, EventArg.WithPayload(
                        ("runId", runId),
                        ("round", _round.ToString()),
                        ("messageCount", _messages.Count.ToString())
                    ));

                    var response = await _llm.ChatAsync(llmCtx.Request, credentials, ct);

                    if (response == null || !response.IsSuccess)
                        throw new InvalidOperationException(response?.Error ?? "null response");

                    // 将 LLM 回复加入消息历史
                    if (!string.IsNullOrEmpty(response.Content))
                        _messages.Add(LlmMessage.Assistant(response.Content));

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
                        break; // 无工具调用 → 完成

                    _round++;
                    if (_round >= _maxRounds)
                    {
                        _logger.Warning($"[NPCLife.Agent] Reached max rounds ({_maxRounds}). Ending loop.");
                        break;
                    }

                    // --- ExecutingTools ---
                    _state = AgentRunState.ExecutingTools;
                    var toolCallsForMessage = new List<LlmToolCall>();
                    var toolResults = new List<(string id, string result)>();

                    foreach (var tc in response.ToolCalls)
                    {
                        _logger.Message($"[NPCLife.Agent] Tool call: {tc.Name}({tc.Arguments})");

                        // 管道拦截：工具调用前
                        var toolCtx = new ToolCallContext { ToolName = tc.Name, Arguments = tc.Arguments };
                        AgentPipeline.RunBeforeToolCall(toolCtx);

                        EventBus.Publish(FrameworkEvents.ToolInvoking, EventArg.WithPayload(
                            ("runId", runId),
                            ("toolName", tc.Name), ("round", _round.ToString())
                        ));

                        string result;
                        if (toolCtx.Cancelled)
                        {
                            result = "{\"error\":\"cancelled by interceptor\"}";
                        }
                        else
                        {
                            result = McpSkillRegistry.InvokeTool(_skillIds, tc.Name, tc.Arguments);
                            toolCtx.Result = result;
                        }

                        // 管道拦截：工具调用后
                        AgentPipeline.RunAfterToolCall(toolCtx);

                        EventBus.Publish(FrameworkEvents.ToolInvoked, EventArg.WithPayload(
                            ("runId", runId),
                            ("toolName", tc.Name), ("resultLength", (result?.Length ?? 0).ToString())
                        ));

                        toolCallsForMessage.Add(tc);
                        toolResults.Add((tc.Id, result));

                        _logger.Message($"[NPCLife.Agent] Tool result ({tc.Name}): {TruncateResult(result)}");
                    }

                    // --- AppendingToolResults ---
                    _state = AgentRunState.AppendingToolResults;

                    // 添加含 tool_calls 的 assistant 消息
                    var assistantMsg = new LlmMessage
                    {
                        Role = "assistant",
                        Content = response.Content ?? "",
                        ToolCalls = toolCallsForMessage
                    };
                    _messages.Add(assistantMsg);

                    // 为每个工具调用添加 tool 结果消息
                    foreach (var (id, result) in toolResults)
                    {
                        _messages.Add(LlmMessage.ToolResult(id, result));
                    }

                    EventBus.Publish(FrameworkEvents.AgentRoundComplete, EventArg.WithPayload(
                        ("runId", runId),
                        ("round", _round.ToString()),
                        ("toolCallCount", response.ToolCalls.Count.ToString())
                    ));
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

            // 管道拦截：循环结束
            AgentPipeline.RunLoopFinished(new LoopContext
            {
                Rounds = rounds,
                EventsProcessed = count,
                NormalCompletion = normalCompletion
            });

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
            _logger.Warning($"[NPCLife.Agent] LLM error: {error}. Events remain in pool for retry. (runId={runId})");
            ErrorHandler.ReportError("AgentLoop", error, new Dictionary<string, string>
            {
                {"runId", runId},
                {"round", _round.ToString()},
                {"drainedCount", (_drained?.Count ?? 0).ToString()}
            });

            // 将已 drain 的事件回灌
            if (_drained != null)
            {
                foreach (var evt in _drained)
                    _pool.Append(evt);
            }
            _drained = null;
            _messages = null;

            ErrorHandler.EndTrace();
            EventBus.Publish(FrameworkEvents.AgentLoopFinished, EventArg.WithPayload(
                ("runId", runId),
                ("rounds", _round.ToString()),
                ("error", error ?? "unknown")
            ));
        }

        // ================================================================
        // 请求构建
        // ================================================================

        private LlmRequest BuildLlmRequest()
        {
            return new LlmRequest
            {
                Messages = new List<LlmMessage>(_messages),
                ToolsJson = McpSkillRegistry.GetActiveToolsJson(_skillIds),
                Temperature = _temperature
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

            // 收集所有事件的关键词，去重后批量查询知识库
            if (_knowledgeBase != null)
            {
                var allKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var evt in events)
                {
                    if (evt.Keywords != null)
                    {
                        foreach (var kw in evt.Keywords)
                        {
                            if (!string.IsNullOrEmpty(kw))
                                allKeywords.Add(kw);
                        }
                    }
                }

                if (allKeywords.Count > 0)
                {
                    var hits = new List<KnowledgeEntry>();
                    foreach (var kw in allKeywords)
                    {
                        if (_knowledgeBase.TryLookup(kw, out var entry))
                            hits.Add(entry);
                    }

                    if (hits.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("## 相关知识");
                        sb.AppendLine();
                        foreach (var entry in hits)
                        {
                            sb.Append("- **");
                            sb.Append(entry.Term ?? "");
                            sb.Append("**: ");
                            sb.AppendLine(entry.Definition ?? "");
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("请审查事件列表，挑选值得发展的事件，使用 create_workspace / branch_workspace 等工具创建剧情线工作空间。");

            if (_contextProvider != null)
            {
                sb.AppendLine();
                sb.AppendLine(_contextProvider());
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

        /// <summary>获取当前运行状态。</summary>
        public AgentRunState State => _state;

        /// <summary>获取当前处理状态（调试用，兼容旧接口）。</summary>
        public bool IsProcessing => _state != AgentRunState.Idle;

        /// <summary>获取当前 Agent 轮数（调试用）。</summary>
        public int CurrentRound => _round;

        /// <summary>获取当前运行 ID（调试用）。</summary>
        public string CurrentRunId => _currentRunId;

        // ================================================================
        // IDisposable
        // ================================================================

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
