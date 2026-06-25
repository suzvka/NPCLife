using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 只读的外部知识源抽象。实现者可以是 GameDef 数据库、Wiki、RAG 等。
    /// 框架不关心内部实现（精确匹配或模糊匹配均由外部源自行决定）。
    /// </summary>
    public interface IExternalKnowledgeSource
    {
        /// <summary>来源名（如 "GameDef"、"Wiki"、"RAG"），用于标注查询结果的出处。</summary>
        string SourceName { get; }

        /// <summary>
        /// 精确查询词条。返回命中列表（可能为空）。
        /// 返回的 KnowledgeEntry.Source 字段应与 SourceName 一致。
        /// </summary>
        IReadOnlyList<KnowledgeEntry> QueryExact(string term);
    }
}
