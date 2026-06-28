namespace NPCLife.Framework
{
    /// <summary>
    /// 运行时度量记录器接口。将静态 RuntimeMetrics 抽象为可注入实例。
    /// </summary>
    public interface IMetricsRecorder
    {
        /// <summary>记录一次 LLM 请求的 Token 消耗。</summary>
        void RecordTokenUsage(string sessionId, int inputTokens, int outputTokens, int cacheReadTokens, string model);

        /// <summary>记录一次工作空间操作。</summary>
        void RecordWorkspaceOperation(string operation);

        /// <summary>记录一次知识库词条查询结果。</summary>
        void RecordKnowledgeLookup(string term, int hitLayer, string sessionId);

        /// <summary>获取当前度量快照。</summary>
        MetricsSnapshot GetSnapshot();

        /// <summary>重置所有度量。</summary>
        void Reset();
    }
}
