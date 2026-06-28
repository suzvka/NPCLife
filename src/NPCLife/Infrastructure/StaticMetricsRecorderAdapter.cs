using NPCLife.Framework;

namespace NPCLife.Infrastructure
{
    /// <summary>委托到静态 RuntimeMetrics 的 IMetricsRecorder 实现。</summary>
    internal class StaticMetricsRecorderAdapter : IMetricsRecorder
    {
        public void RecordTokenUsage(string sessionId, int inputTokens, int outputTokens, int cacheReadTokens, string model)
            => RuntimeMetrics.RecordTokenUsage(sessionId, inputTokens, outputTokens, cacheReadTokens, model);

        public void RecordWorkspaceOperation(string operation)
            => RuntimeMetrics.RecordWorkspaceOperation(operation);

        public void RecordKnowledgeLookup(string term, int hitLayer, string sessionId)
            => RuntimeMetrics.RecordKnowledgeLookup(term, hitLayer, sessionId);

        public MetricsSnapshot GetSnapshot() => RuntimeMetrics.GetSnapshot();

        public void Reset() => RuntimeMetrics.Reset();
    }
}
