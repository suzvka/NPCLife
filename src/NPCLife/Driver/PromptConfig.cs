using System.IO;
using System.Reflection;
using System.Text;

namespace NPCLife.Driver
{
    /// <summary>
    /// 角色身份提示词资源加载器。从 EmbeddedResource 中读取原始 .txt 文本。
    /// 
    /// 框架已内置三个角色身份 Skill（role_director / role_screenwriter / role_improviser），
    /// 它们通过 Skill 系统自动激活并注入 system prompt。此类仅作为资源加载的内部支撑，
    /// 供 RoleProvider 使用。宿主通常无需直接调用。
    /// </summary>
    public static class PromptConfig
    {
        private static string _cachedDirectorPrompt;
        private static string _cachedScreenwriterPrompt;
        private static string _cachedImproviserPrompt;

        /// <summary>导演 Agent 默认系统提示词。</summary>
        public static string DefaultDirectorPrompt =>
            _cachedDirectorPrompt ?? (_cachedDirectorPrompt = LoadPromptResource("NPCLife.Prompts.DirectorPrompt.txt"));

        /// <summary>编剧 Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultScreenwriterPrompt =>
            _cachedScreenwriterPrompt ?? (_cachedScreenwriterPrompt = LoadPromptResource("NPCLife.Prompts.ScreenwriterPrompt.txt"));

        /// <summary>即兴编剧 Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultImproviserPrompt =>
            _cachedImproviserPrompt ?? (_cachedImproviserPrompt = LoadPromptResource("NPCLife.Prompts.ImproviserPrompt.txt"));

        private static string LoadPromptResource(string resourceName)
        {
            try
            {
                var assembly = typeof(PromptConfig).Assembly;
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
