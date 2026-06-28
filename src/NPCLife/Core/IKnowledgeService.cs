using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 知识服务公共接口。框架组件（AgentLoop、KnowledgeMcpProvider）通过此接口消费知识库能力，
    /// 不关心背后的知识源组织和存储方式。
    ///
    /// 框架提供默认实现 KnowledgeService（内部聚合 BuiltInKnowledgeBase + IExternalKnowledgeSource），
    /// 第三方可自行实现此接口，按需组织知识库架构（单库、多层、分布式等）。
    /// </summary>
    public interface IKnowledgeService
    {
        /// <summary>
        /// 查询词条。返回所有来源的命中结果列表（可能为空）。
        /// 调用方根据 KnowledgeEntry.Source 区分出处。
        /// </summary>
        IReadOnlyList<KnowledgeEntry> Lookup(string term);

        /// <summary>存储/覆盖知识条目。</summary>
        void Store(KnowledgeEntry entry);

        /// <summary>删除指定词条。不存在时静默返回。</summary>
        void Delete(string term);

        /// <summary>列出全部词条。</summary>
        IReadOnlyList<KnowledgeEntry> ListAll();

        /// <summary>按前缀列举词条。</summary>
        IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix);
    }
}
