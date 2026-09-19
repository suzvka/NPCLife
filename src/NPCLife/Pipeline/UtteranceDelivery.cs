using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Script;
using System;
using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 管线产出的台词（<see cref="PipelineResult.Lines"/>）经本接缝解析占位符后，
    /// 由 <see cref="MainThreadDispatcher"/> 投递到游戏侧 <see cref="IScriptConsumer"/>。
    /// 游戏侧消费契约（<see cref="IScriptConsumer.OnScriptLinesReady"/> 签名、主线程投递时机）保持不变（不变量 I1）；
    /// workspaceId 位填说话者上下文键，roundSeq 位填递增序号。
    /// </summary>
    public sealed class UtteranceDelivery
    {
        private readonly Func<IScriptConsumer> _getConsumer;
        private readonly IScriptLineResolver _resolver;
        private readonly ILogger _logger;
        private int _seq;

        public UtteranceDelivery(Func<IScriptConsumer> getConsumer, IScriptLineResolver resolver, ILogger logger = null)
        {
            _getConsumer = getConsumer ?? throw new ArgumentNullException(nameof(getConsumer));
            _resolver = resolver;   // 可空：无解析器则跳过占位符解析
            _logger = logger;
        }

        /// <summary>投递一次管线生成结果。空台词直接跳过（不占用序号、不打扰消费端）。</summary>
        public void Deliver(PipelineResult result)
        {
            if (result == null) return;
            Deliver(result.Moment, result.Lines);
        }

        /// <summary>投递一批台词。moment 提供说话者上下文键；lines 为已解析语义的台词行。</summary>
        public void Deliver(UtteranceMoment moment, IReadOnlyList<ScriptLine> lines)
        {
            if (lines == null || lines.Count == 0) return;

            try { _resolver?.Resolve(lines); }
            catch (Exception ex) { _logger?.Warning($"[NPCLife.UtteranceDelivery] Resolve failed: {ex.Message}"); }

            var consumer = _getConsumer();
            if (consumer == null)
            {
                _logger?.Warning("[NPCLife.UtteranceDelivery] IScriptConsumer not registered.");
                return;
            }

            int seq = ++_seq;
            string key = !string.IsNullOrEmpty(moment?.SpeakerId) ? moment.SpeakerId : "";
            MainThreadDispatcher.Enqueue(() =>
            {
                try { consumer.OnScriptLinesReady(key, seq, lines); }
                catch (Exception ex) { _logger?.Warning($"[NPCLife.UtteranceDelivery] Consumer error: {ex.Message}"); }
            });
        }
    }
}
