using System;
using System.Collections.Generic;
using System.Linq;
using NPCLife.Framework;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// LLM API 提供者类型。
    /// 当前唯一实现渠道为 OpenAI 兼容格式（Ollama、vLLM、中转代理等）。
    /// 新增厂商格式时：在此追加枚举值 + 在 LlmAccessor.CreateAdapter 增加对应实现。
    /// </summary>
    public enum LlmProviderType
    {
        /// <summary>OpenAI 及兼容 API（Ollama、vLLM、中转代理等）。</summary>
        OpenAI
    }

    /// <summary>
    /// LLM API 凭证。无状态数据类，维护模型名列表以及 API 访问所需的三元组。
    /// 零外部依赖，不持有任何持久化状态。
    /// </summary>
    public class LlmCredential
    {
        /// <summary>API 基础 URL。</summary>
        public string BaseUrl { get; set; }

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; set; }

        /// <summary>此凭证提供的模型名列表。</summary>
        public List<string> ModelNames { get; set; } = new List<string>();

        /// <summary>提供商类型，决定使用哪个适配器。</summary>
        public LlmProviderType ProviderType { get; set; } = LlmProviderType.OpenAI;

        /// <summary>扩展 HTTP 头，用于需要自定义 header 的代理场景。</summary>
        public Dictionary<string, string> ExtraHeaders { get; set; }

        /// <summary>HTTP 请求超时（秒），默认 120。</summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// 对话端点路径，相对于 BaseUrl。默认 /v1/chat/completions（OpenAI 兼容）。
        /// 当 BaseUrl 已包含详细 API 路径或使用自定义网关时，可设为对应路径。
        /// </summary>
        public string ChatEndpoint { get; set; } = "/v1/chat/completions";

        /// <summary>
        /// 模型列表端点路径，相对于 BaseUrl。默认 /v1/models（OpenAI 兼容）。
        /// 当 BaseUrl 已包含详细 API 路径时（如 https://open.bigmodel.cn/api/paas/v4/），
        /// 可设为 /models 以避免拼接出错误路径。
        /// </summary>
        public string ModelsEndpoint { get; set; } = "/v1/models";

        /// <summary>
        /// API 访问级校验：baseUrl + apiKey 均非空即可。
        /// 适用于模型发现、连接测试等不涉及具体模型的场景。
        /// </summary>
        public bool HasApiAccess()
        {
            return !string.IsNullOrEmpty(BaseUrl)
                && !string.IsNullOrEmpty(ApiKey);
        }

        /// <summary>
        /// 聊天级校验：baseUrl + apiKey + 至少一个模型名。
        /// </summary>
        public bool IsChatReady()
        {
            return HasApiAccess() && ModelNames != null && ModelNames.Count > 0;
        }

        /// <summary>
        /// [已废弃] 使用 <see cref="IsChatReady"/> 替代。
        /// </summary>
        [System.Obsolete("Use IsChatReady() for chat-scope checks, or HasApiAccess() for API-access checks.")]
        public bool IsValid()
        {
            return IsChatReady();
        }

        /// <summary>
        /// 创建凭证快照副本（浅拷贝字符串和集合引用）。
        /// </summary>
        public LlmCredential Clone()
        {
            return new LlmCredential
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                ModelNames = ModelNames != null ? new List<string>(ModelNames) : new List<string>(),
                ProviderType = ProviderType,
                ExtraHeaders = ExtraHeaders != null
                    ? new Dictionary<string, string>(ExtraHeaders)
                    : null,
                TimeoutSeconds = TimeoutSeconds,
                ChatEndpoint = ChatEndpoint,
                ModelsEndpoint = ModelsEndpoint
            };
        }

        public override string ToString()
        {
            return $"LlmCredential({ProviderType} [{string.Join(",", ModelNames ?? new List<string>())}] @ {BaseUrl})";
        }

        // ================================================================
        // 序列化 / 反序列化（自包含，外部模块无需感知内部字段）
        // ================================================================

        /// <summary>
        /// 序列化为 JSON 对象字符串。后续新增字段只需改此处。
        /// </summary>
        public string ToJson()
        {
            var w = new JsonWriter(256);
            w.Prop("baseUrl", BaseUrl ?? "");
            w.Prop("apiKey", ApiKey ?? "");
            w.Array("modelNames", ModelNames ?? new List<string>());
            w.Prop("providerType", ProviderType.ToString());
            w.Prop("timeoutSeconds", TimeoutSeconds);
            w.Prop("chatEndpoint", ChatEndpoint ?? "/v1/chat/completions");
            w.Prop("modelsEndpoint", ModelsEndpoint ?? "/v1/models");
            if (ExtraHeaders != null && ExtraHeaders.Count > 0)
                w.PropRaw("extraHeaders", JsonParser.SerializeDict(ExtraHeaders));
            return w.Close();
        }

        /// <summary>
        /// 从 JSON 对象字符串反序列化。同时兼容旧格式（平铺字段）和新格式。
        /// </summary>
        public static LlmCredential FromJson(string json)
        {
            var cred = new LlmCredential();
            if (string.IsNullOrEmpty(json)) return cred;

            try
            {
                var dict = JsonParser.ParseDict(json);
                if (dict.TryGetValue("baseUrl", out string bu)) cred.BaseUrl = bu;
                if (dict.TryGetValue("apiKey", out string ak)) cred.ApiKey = ak;
                if (dict.TryGetValue("modelNames", out string mnsJson))
                    cred.ModelNames = JsonParser.ParseStringArray(mnsJson)?.ToList() ?? new List<string>();
                if (dict.TryGetValue("providerType", out string pt)
                    && Enum.TryParse<LlmProviderType>(pt, out var pType))
                    cred.ProviderType = pType;
                if (dict.TryGetValue("timeoutSeconds", out string ts)
                    && int.TryParse(ts, out var tsVal))
                    cred.TimeoutSeconds = tsVal;
                if (dict.TryGetValue("modelsEndpoint", out string me))
                    cred.ModelsEndpoint = me;
                if (dict.TryGetValue("chatEndpoint", out string ce))
                    cred.ChatEndpoint = ce;
                if (dict.TryGetValue("extraHeaders", out string ehJson))
                {
                    var headers = JsonParser.ParseDict(ehJson);
                    if (headers != null && headers.Count > 0)
                        cred.ExtraHeaders = new Dictionary<string, string>(headers);
                }
            }
            catch
            {
                // 解析失败，保持默认值
            }

            return cred;
        }
    }
}
