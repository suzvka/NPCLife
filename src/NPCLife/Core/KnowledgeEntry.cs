using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 知识库中的单条知识条目。纯 DTO，零外部依赖。
    /// Term 作为主键索引。
    /// </summary>
    public class KnowledgeEntry
    {
        /// <summary>词条名（索引键，大小写不敏感）。</summary>
        public string Term;

        /// <summary>释义文本。null 或空字符串表示该词条已建立索引但尚未学习释义。</summary>
        public string Definition;

        /// <summary>知识来源名称（如 "LLM"、"GameDef"、"AgentDeduction"、"Wiki"、"RAG"）。</summary>
        public string Source;

    }
}
