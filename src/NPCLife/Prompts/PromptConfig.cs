using System.IO;
using System.Text;

namespace NPCLife.Prompts
{
    /// <summary>
    /// 提示词资源加载器。从 EmbeddedResource 中读取原始 .txt 文本。
    /// 视锥对齐管线的「写手」是唯一保留 LLM 的角色，其系统提示词在此加载。
    /// </summary>
    public static class PromptConfig
    {
        private static string _cachedWriterPrompt;

        /// <summary>
        /// 视锥对齐管线「写手」默认系统提示词：三区材料语义 + 袋外事实禁止 + 台词输出格式。
        /// </summary>
        public static string DefaultWriterPrompt =>
            _cachedWriterPrompt ?? (_cachedWriterPrompt = LoadPromptResource("NPCLife.Prompts.WriterPrompt.txt"));

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
