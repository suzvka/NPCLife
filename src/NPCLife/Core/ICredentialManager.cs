using NPCLife.Framework.Llm;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 凭证管理接口（UI 用）。
    /// 继承 <see cref="ICredentialStore"/>，提供卡片式 CRUD 和激活顺序管理。
    /// 以"凭证名"作为用户可见概念，每个凭证是自包含实体。
    /// </summary>
    public interface ICredentialManager : ICredentialStore
    {
        // ================================================================
        // CRUD
        // ================================================================

        /// <summary>
        /// 创建凭证。name 必须唯一（不区分大小写），重复时抛出 ArgumentException。
        /// </summary>
        void Create(string name, LlmCredential credential);

        /// <summary>
        /// 获取凭证。无条件返回（不要求 IsChatReady）。
        /// 不存在时返回 null。
        /// </summary>
        LlmCredential Get(string name);

        /// <summary>
        /// 更新凭证。name 必须已存在，不存在时抛出 KeyNotFoundException。
        /// </summary>
        void Update(string name, LlmCredential credential);

        /// <summary>
        /// 删除凭证。同时从激活列表中移除。name 不存在时静默忽略。
        /// </summary>
        void Delete(string name);

        /// <summary>
        /// 获取所有凭证（名称 + 凭证对象），无序。
        /// </summary>
        IReadOnlyList<(string Name, LlmCredential Credential)> GetAll();

        /// <summary>
        /// 凭证是否存在。
        /// </summary>
        bool Exists(string name);

        // ================================================================
        // 激活顺序（Fallback 链路）
        // ================================================================

        /// <summary>
        /// 获取当前激活顺序列表。
        /// </summary>
        IReadOnlyList<string> GetActivationOrder();

        /// <summary>
        /// 设置激活顺序。自动过滤掉不存在的名称。
        /// </summary>
        void SetActivationOrder(IReadOnlyList<string> names);

        /// <summary>
        /// 将凭证追加到激活列表末尾。已存在时静默忽略。
        /// </summary>
        void Activate(string name);

        /// <summary>
        /// 将凭证从激活列表移除。不存在时静默忽略。
        /// </summary>
        void Deactivate(string name);

        // ================================================================
        // 模型设置
        // ================================================================

        /// <summary>
        /// 设置凭证的模型名称。name 必须已存在。
        /// </summary>
        void SetModel(string name, string modelName);
    }
}
