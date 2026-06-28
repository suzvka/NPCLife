using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using NPCLife.Framework.Mcp;
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
    public class AgentLoop : IDisposable
    {
        private readonly IWorkspace _workspace;
        private readonly IEventLog _pool;
        private readonly ILlmService _llm;
        private readonly ICredentialStore _credentialStore;
        private readonly ILogger _logger;
        private readonly string _systemPrompt;
        private readonly string[] _skillIds;
        private readonly int _maxRounds;
        private readonly ICardSerializer _serializer;
        private readonly Action _unsubscribe; // 取消事件订阅的委托
        private readonly Func<string> _contextProvider;
        private readonly float _temperature;
        private readonly List<(string Cred, string Model)> _modelRefs;
        private readonly string _currentModelJson;

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
        /// 创建 AgentLoop 并自动订阅工作空间事件池的 OnThresholdReached 事件。
        /// pool、skillIds、modelRefs 均从 IWorkspace 推导，无需手动传入。
        /// </summary>
        /// <param name="workspace">绑定的工作空间。Agent 从 ws.EventPool drain 事件，从 ws.SkillSlot 获取工具集，从 ws.ModelRefs 解析凭证。</param>
        /// <param name="deps">基础设施依赖（LLM 服务、凭证、日志等）与行为配置（最大轮数、温度）。由宿主统一注入。</param>
        /// <param name="systemPrompt">系统提示词。由宿主根据角色 + 游戏附加指令构建。</param>
        /// <param name="contextProvider">动态上下文提供者（可选）。每次激活时调用，返回值追加到用户消息末尾。</param>
        public AgentLoop(
            IWorkspace workspace,
            AgentLoopDependencies deps,
            string systemPrompt,
            Func<string> contextProvider = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _pool = workspace.EventPool ?? throw new ArgumentException("workspace.EventPool is null", nameof(workspace));
            _llm = deps.Llm ?? throw new ArgumentNullException(nameof(deps.Llm));
            _credentialStore = deps.CredentialStore ?? throw new ArgumentNullException(nameof(deps.CredentialStore));
            _logger = deps.Logger ?? throw new ArgumentNullException(nameof(deps.Logger));
            _systemPrompt = systemPrompt ?? "";
            _skillIds = DeriveSkillIds(workspace);
            _maxRounds = deps.MaxRounds > 0 ? deps.MaxRounds : 10;
            _serializer = deps.Serializer ?? CardSerializer.Default;
            _contextProvider = contextProvider;
            _temperature = deps.Temperature > 0 ? deps.Temperature : 0.7f;
            _modelRefs = ParseModelRefs(workspace.ModelRefs);
            _currentModelJson = workspace.CurrentModel;

            // 订阅池子事件——唯一激活路径
            _pool.OnThresholdReached += OnPoolChanged;
            _unsubscribe = () => _pool.OnThresholdReached -= OnPoolChanged;
        }

        /// <summary>
        /// 从工作空间的 SkillSlot 提取活跃技能 ID 列表。
        /// 工作空间创建时已按角色注册了默认技能集（SkillCatalog），此处直接读取。
        /// </summary>
        private static string[] DeriveSkillIds(IWorkspace ws)
        {
            var activeIds = ws.SkillSlot?.ActiveSkillIds;
            if (activeIds != null && activeIds.Count > 0)
                return activeIds.ToArray();

            // 回退：使用角色默认技能配置
            var defaults = SkillCatalog.GetDefaultSkillIds(ws.CreatedByRole);
            return defaults?.ToArray() ?? Array.Empty<string>();
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

                // 解析凭证：优先使用模型引用列表，回退到全局激活凭证
                var credentials = ResolveCredentials();
                if (credentials.Count == 0)
                    throw new InvalidOperationException("No active credentials configured");

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
                    try
                    {
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

                            toolResults.Add((tc.Id, result));

                            _logger.Message($"[NPCLife.Agent] Tool result ({tc.Name}): {TruncateResult(result)}");

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
                    }

                    // —— AppendingToolResults ——
                    // 每个 LLM 响应产生一条 assistant 消息（含 content 和 tool_calls），
                    // 后跟各 tool 的返回结果消息。确保单轮多工具调用时结构正确。
                    _state = AgentRunState.AppendingToolResults;
                    _messages = AppendAssistantTurn(_messages, response, toolResults).ToList();

                    EventBus.Publish(FrameworkEvents.AgentRoundComplete, EventArg.WithPayload(
                        ("runId", runId),
                        ("round", _round.ToString()),
                        ("toolCallCount", response.ToolCalls.Count.ToString())
                    ));

                    if (aborted) break;
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
                    ToolCalls = response.ToolCalls
                });

                foreach (var (id, toolResult) in toolResults)
                    result.Add(LlmMessage.ToolResult(id, toolResult));
            }
            else
            {
                // 纯文本回复：一条 assistant（仅 content，无 tool_calls）
                result.Add(LlmMessage.Assistant(response.Content ?? ""));
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

            sb.AppendLine();
            sb.AppendLine("请审查事件列表，挑选值得发展的事件，使用 create_workspace / branch_workspace 等工具创建剧情线工作空间。");
            sb.AppendLine("如果工作空间上下文中包含 focusCharacterIds（导演指定的聚焦角色），请优先围绕这些角色展开叙事。");

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

        // ================================================================
        // IDisposable
        // ================================================================

        // ================================================================
        // 凭证解析
        // ================================================================

        /// <summary>
        /// 解析凭证列表。优先使用模型引用列表，回退到全局激活凭证。
        /// 当前选中模型会被捧到列表首位。
        /// </summary>
        private IReadOnlyList<LlmCredential> ResolveCredentials()
        {
            if (_modelRefs != null && _modelRefs.Count > 0)
            {
                // 解析当前模型（名称匹配）
                var current = ParseSingleRef(_currentModelJson);

                var result = new List<LlmCredential>();
                LlmCredential currentCred = null;

                foreach (var (cred, model) in _modelRefs)
                {
                    var resolved = _credentialStore.Resolve(cred, model);
                    if (resolved == null) continue;

                    // 检查是否为当前选中模型
                    if (current.HasValue
                        && string.Equals(cred, current.Value.Cred, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(model, current.Value.Model, StringComparison.OrdinalIgnoreCase))
                    {
                        currentCred = resolved;
                    }
                    else
                    {
                        result.Add(resolved);
                    }
                }

                // 当前模型捧到首位
                if (currentCred != null)
                    result.Insert(0, currentCred);

                if (result.Count > 0)
                    return result;
            }

            // 回退：无模型引用或全部解析失败时，使用全局激活凭证
            return _credentialStore.GetActiveCredentials();
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
