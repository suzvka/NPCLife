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

        /// <summary>信心度 (0.0~1.0)。GameDef 来源天然为 1.0，LLM 来源通常 0.6~0.9。</summary>
        public float Confidence;

        /// <summary>关联的语义标签（如 Combat、Faction、Lore）。用于按领域过滤。</summary>
        public List<string> ContextTags;
    }
}
