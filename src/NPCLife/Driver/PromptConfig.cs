using System.IO;
using System.Reflection;
using System.Text;

namespace NPCLife.Driver
{
    /// <summary>
    /// 默认系统提示词提供者。三个角色的基础身份以 EmbeddedResource 形式打包在 NPCLife 中，
    /// 对外暴露为不可变的静态只读字符串。游戏侧（如 RimLife）只能在此基础上追加指令，
    /// 禁止覆盖这些基座身份。
    /// </summary>
    public static class PromptConfig
    {
        private static string _cachedDirectorPrompt;
        private static string _cachedScreenwriterPrompt;
        private static string _cachedFreelancerPrompt;

        /// <summary>导演 Agent 默认系统提示词。</summary>
        public static string DefaultDirectorPrompt =>
            _cachedDirectorPrompt ?? (_cachedDirectorPrompt = LoadPromptResource("NPCLife.Prompts.DirectorPrompt.txt"));

        /// <summary>编剧 Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultScreenwriterPrompt =>
            _cachedScreenwriterPrompt ?? (_cachedScreenwriterPrompt = LoadPromptResource("NPCLife.Prompts.ScreenwriterPrompt.txt"));

        /// <summary>Freelancer Agent 默认系统提示词（不含动态上下文和台词格式）。</summary>
        public static string DefaultFreelancerPrompt =>
            _cachedFreelancerPrompt ?? (_cachedFreelancerPrompt = LoadPromptResource("NPCLife.Prompts.FreelancerPrompt.txt"));

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
