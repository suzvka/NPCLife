using System;

namespace NPCLife.Framework.PromptBlocks
{
    /// <summary>
    /// 角色身份提示词块。纯文本，自定义分段标题，无工具。
    /// 替代原先 role_* Skill 的 PromptInstruction 注入方式。
    /// </summary>
    public class RoleIdentityBlock : ITextPromptBlock
    {
        private readonly string _content;

        /// <param name="id">块唯一标识（如 "role_director"）。</param>
        /// <param name="header">分段标题（如 "导演身份"）。</param>
        /// <param name="content">角色身份提示词文本（从 .txt 嵌入资源加载）。</param>
        public RoleIdentityBlock(string id, string header, string content)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Header = header;
            _content = content ?? "";
        }

        public string Id { get; }
        public string Header { get; }

        public string GetContent() => _content;
    }
}
