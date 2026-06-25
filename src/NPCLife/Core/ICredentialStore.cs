using NPCLife.Framework.Llm;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 凭证存储接口（运行时）。
    /// Agent 仅依赖此接口获取活跃凭证列表。
    /// UI 管理场景请使用 <see cref="ICredentialManager"/>。
    /// </summary>
    public interface ICredentialStore
    {
        /// <summary>
        /// 获取当前激活顺序对应的凭证列表。
        /// 已过滤不存在或未就绪的凭证。返回空列表表示无可用凭证。
        /// </summary>
        IReadOnlyList<LlmCredential> GetActiveCredentials();

        /// <summary>
        /// 是否有任何可用凭证（至少一个凭证具备 API 访问能力）。
        /// </summary>
        bool HasCredentials { get; }

        /// <summary>
        /// 按凭证名 + 模型名解析凭证三元组。
        /// 将 modelName 覆盖到返回的 credential 上。
        /// 找不到凭证时返回 null。
        /// </summary>
        LlmCredential Resolve(string credentialName, string modelName);
    }
}
