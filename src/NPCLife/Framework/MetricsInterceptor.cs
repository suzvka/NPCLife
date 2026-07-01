using System;

namespace NPCLife.Framework
{
    /// <summary>
    /// 运行时度量拦截器。实现 IAgentInterceptor，在 Agent 循环的关键拦截点采集：
    /// - MCP 工具调用频率和成功率
    /// - Agent 循环统计（轮数、事件数、角色）
    ///
    /// Token 消耗通过 EventBus 订阅 llm.response_received 独立采集。
    /// 框架侧纯逻辑组件，零外部依赖。
    /// </summary>
    public class MetricsInterceptor : AgentInterceptorBase
    {
        private readonly AgentRole _role;

        // ---- 核心修复：3x 重复计数 ----
        // 管线为 Director/Screenwriter/Improviser 各注册一个 MetricsInterceptor 实例，
        // 但对每个 Agent 的事件统一调用所有拦截器。
        // 若所有实例都记录工具调用，同一调用会被计数 3 次。
        //
        // 解决方案：用 [ThreadStatic] 引用跟踪"当前线程的拥有者拦截器"。
        // 只有 _activeOwner == this 的拦截器才执行实际记录，其余实例静默跳过。
        [ThreadStatic]
        private static MetricsInterceptor _activeOwner;

        [ThreadStatic]
        private static string _activeSessionId;

        [ThreadStatic]
        private static int _llmCallCountInSession;

        /// <summary>
        /// 获取当前线程上活跃拦截器的会话 ID（供外部观察者读取）。
        /// 仅供 Token 消耗采集、知识服务记录等非核心度量场景使用。
        /// </summary>
        public static string CurrentSessionId => _activeSessionId;

        /// <summary>
        /// 创建度量拦截器。
        /// </summary>
        /// <param name="role">所属 Agent 角色。</param>
        public MetricsInterceptor(AgentRole role)
        {
            _role = role;
        }

        // ================================================================
        // IAgentInterceptor
        // ================================================================

        /// <summary>
        /// 在每个 LLM 请求发送前调用。检测是否需要开始新会话。
        /// 仅第一个拦截器实例获得"拥有者"身份，其余实例跳过。
        /// </summary>
        public override void OnBeforeLlm(LlmContext ctx)
        {
            // 抢占拥有者：同一线程上只有第一个到达的拦截器生效
            if (_activeOwner == null)
            {
                _activeOwner = this;
                _activeSessionId = RuntimeMetrics.BeginSession(_role);
                _llmCallCountInSession = 0;
            }
            if (_activeOwner != this) return; // 非拥有者，跳过

            _llmCallCountInSession++;
        }

        /// <summary>
        /// 工具调用前记录工具名。实际计数在 OnAfterToolCall 中统一完成（需判断成功/失败）。
        /// </summary>
        public override void OnBeforeToolCall(ToolCallContext ctx)
        {
        }

        /// <summary>
        /// 工具调用后：记录调用次数和成功/失败。仅拥有者拦截器执行。
        /// </summary>
        public override void OnAfterToolCall(ToolCallContext ctx)
        {
            if (_activeOwner != this || _activeSessionId == null || ctx?.ToolName == null) return;

            bool success = !IsErrorResult(ctx.Result);
            RuntimeMetrics.RecordToolCall(_activeSessionId, ctx.ToolName, success);
        }

        /// <summary>
        /// Agent 循环结束：记录统计并结束会话。仅拥有者拦截器清理状态。
        /// </summary>
        public override void OnLoopFinished(LoopContext ctx)
        {
            // 所有拦截器都记录循环统计（角色维度）
            RuntimeMetrics.RecordLoopFinished(
                ctx.Rounds,
                ctx.EventsProcessed,
                ctx.NormalCompletion,
                _role);

            if (_activeOwner == this && _activeSessionId != null)
            {
                RuntimeMetrics.EndSession(_activeSessionId);
                _activeSessionId = null;
                _llmCallCountInSession = 0;
                _activeOwner = null;
            }
        }

        // ================================================================
        // 辅助
        // ================================================================

        private static bool IsErrorResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return false;
            // 检查是否包含 error JSON 标记
            string trimmed = result.TrimStart();
            return trimmed.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("{\"error:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取当前会话 ID（调试用）。
        /// </summary>
        public static string SessionId => _activeSessionId;

        /// <summary>
        /// 获取当前角色（调试用）。
        /// </summary>
        public AgentRole Role => _role;
    }
}
