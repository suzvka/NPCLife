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
        /// 按凭证名解析凭证。返回凭证的克隆副本。
        /// modelName 仅在凭证自身未设置 ModelName 时作为回退值使用，
        /// 不会覆盖用户已在 UI 中显式设置的模型名。
        /// 找不到凭证时返回 null。
        /// </summary>
        LlmCredential Resolve(string credentialName, string modelName);
    }
}
