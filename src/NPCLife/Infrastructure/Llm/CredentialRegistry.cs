using NPCLife.Core;
using NPCLife.Framework;
using NPCLife.Framework.Llm;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Infrastructure.Llm
{
    /// <summary>
    /// 凭证管理器实现。管理"凭证名 → API 凭证"映射。
    /// 实现 ICredentialManager（隐式实现 ICredentialStore）。
    /// 
    /// 通过宿主提供的 persistAction 委托持久化全局配置（不绑定存档）。
    /// 
    /// 运行时 Agent 通过 GetActiveCredentials 获取凭证，
    /// UI 通过 Create / Update / Delete / SetModel 管理配置。
    /// </summary>
    public class CredentialRegistry : ICredentialManager
    {
        // ---- 内部状态 ----

        private readonly object _lock = new object();
        private readonly Dictionary<string, LlmCredential> _credentials
            = new Dictionary<string, LlmCredential>(StringComparer.OrdinalIgnoreCase);
        private List<string> _activationOrder = new List<string>();

        // ---- 持久化委托 ----

        private readonly Func<string> _serializeState;
        private readonly Action<string> _persistAction;

        /// <summary>
        /// 创建凭证管理器实例。
        /// </summary>
        /// <param name="persistAction">将 JSON 字符串持久化到存储后端。</param>
        /// <param name="initialJson">初始 JSON 状态（从存储后端加载）。</param>
        /// <param name="serializeState">可选：将当前状态序列化为 JSON（用于调试）。</param>
        public CredentialRegistry(
            Action<string> persistAction,
            string initialJson = null,
            Func<string> serializeState = null)
        {
            _persistAction = persistAction ?? throw new ArgumentNullException(nameof(persistAction));
            _serializeState = serializeState;

            if (!string.IsNullOrEmpty(initialJson))
            {
                DeserializeState(initialJson);
            }
        }

        // ================================================================
        // ICredentialStore（运行时）
        // ================================================================

        public IReadOnlyList<LlmCredential> GetActiveCredentials()
        {
            lock (_lock)
            {
                var result = new List<LlmCredential>();
                foreach (var name in _activationOrder)
                {
                    if (_credentials.TryGetValue(name, out var cred) && cred.IsChatReady())
                    {
                        result.Add(cred.Clone());
                    }
                }
                return result;
            }
        }

        public bool HasCredentials
        {
            get
            {
                lock (_lock)
                {
                    return _credentials.Values.Any(c => c.HasApiAccess());
                }
            }
        }

        public LlmCredential Resolve(string credentialName, string modelName)
        {
            var cred = Get(credentialName);
            if (cred == null) return null;
            if (!string.IsNullOrEmpty(modelName))
                cred.ModelName = modelName;
            return cred;
        }

        // ================================================================
        // CRUD
        // ================================================================

        public void Create(string name, LlmCredential credential)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", nameof(name));
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            lock (_lock)
            {
                if (_credentials.ContainsKey(name))
                    throw new ArgumentException($"Credential '{name}' already exists.", nameof(name));
                _credentials[name] = credential.Clone();
            }
            Persist();
        }

        public LlmCredential Get(string name)
        {
            lock (_lock)
            {
                if (_credentials.TryGetValue(name, out var found))
                    return found.Clone();
            }
            return null;
        }

        public void Update(string name, LlmCredential credential)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty.", nameof(name));
            if (credential == null)
                throw new ArgumentNullException(nameof(credential));

            lock (_lock)
            {
                if (!_credentials.ContainsKey(name))
                    throw new KeyNotFoundException($"Credential '{name}' not found.");
                _credentials[name] = credential.Clone();
            }
            Persist();
        }

        public void Delete(string name)
        {
            lock (_lock)
            {
                _credentials.Remove(name);
                _activationOrder.Remove(name);
            }
            Persist();
        }

        public IReadOnlyList<(string Name, LlmCredential Credential)> GetAll()
        {
            lock (_lock)
            {
                return _credentials
                    .Select(kv => (kv.Key, kv.Value.Clone()))
                    .ToList();
            }
        }

        public bool Exists(string name)
        {
            lock (_lock)
            {
                return _credentials.ContainsKey(name);
            }
        }

        // ================================================================
        // 激活顺序（Fallback 链路）
        // ================================================================

        public IReadOnlyList<string> GetActivationOrder()
        {
            lock (_lock)
            {
                return _activationOrder.ToList();
            }
        }

        public void SetActivationOrder(IReadOnlyList<string> names)
        {
            lock (_lock)
            {
                _activationOrder = names
                    ?.Where(n => _credentials.ContainsKey(n))
                    .ToList() ?? new List<string>();
            }
            Persist();
        }

        public void Activate(string name)
        {
            lock (_lock)
            {
                if (!_credentials.ContainsKey(name)) return;
                if (_activationOrder.Contains(name)) return;
                _activationOrder.Add(name);
            }
            Persist();
        }

        public void Deactivate(string name)
        {
            lock (_lock)
            {
                _activationOrder.Remove(name);
            }
            Persist();
        }

        // ================================================================
        // 模型设置
        // ================================================================

        public void SetModel(string name, string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            lock (_lock)
            {
                if (!_credentials.TryGetValue(name, out var existing))
                    return;

                existing.ModelName = modelName;
                _credentials[name] = existing.Clone();
            }
            Persist();
        }

        // ================================================================
        // 持久化
        // ================================================================

        private void Persist()
        {
            try
            {
                string json;
                lock (_lock)
                {
                    json = SerializeState();
                }
                _persistAction(json);
            }
            catch
            {
                // 持久化失败不应影响运行时
            }
        }

        private string SerializeState()
        {
            var w = new JsonWriter(2048);

            // 凭证列表（JSON 字段名保持 "aliases" 以兼容旧数据）
            var credJsons = new List<string>();
            foreach (var kv in _credentials)
            {
                var cred = kv.Value;
                var cw = new JsonWriter(256);
                cw.Prop("alias", kv.Key);
                cw.Prop("baseUrl", cred.BaseUrl ?? "");
                cw.Prop("apiKey", cred.ApiKey ?? "");
                cw.Prop("modelName", cred.ModelName ?? "");
                cw.Prop("providerType", cred.ProviderType.ToString());
                cw.Prop("timeoutSeconds", cred.TimeoutSeconds);
                credJsons.Add(cw.Close());
            }
            w.ArrayRaw("aliases", credJsons);

            // 激活顺序（JSON 字段名保持 "activeAliases" 以兼容旧数据）
            w.Array("activeAliases", _activationOrder);

            return w.Close();
        }

        private void DeserializeState(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}")
                return;

            try
            {
                var dict = JsonParser.ParseDict(json);

                // 凭证列表
                if (dict.TryGetValue("aliases", out string aliasesJson))
                {
                    var aliasDicts = JsonParser.ParseObjectArray(aliasesJson);
                    foreach (var ad in aliasDicts)
                    {
                        if (!ad.TryGetValue("alias", out string alias) || string.IsNullOrEmpty(alias))
                            continue;

                        var cred = new LlmCredential();
                        if (ad.TryGetValue("baseUrl", out string bu)) cred.BaseUrl = bu;
                        if (ad.TryGetValue("apiKey", out string ak)) cred.ApiKey = ak;
                        if (ad.TryGetValue("modelName", out string mn)) cred.ModelName = mn;
                        if (ad.TryGetValue("providerType", out string pt)
                            && Enum.TryParse<LlmProviderType>(pt, out var pType))
                            cred.ProviderType = pType;
                        if (ad.TryGetValue("timeoutSeconds", out string ts)
                            && int.TryParse(ts, out var tsVal))
                            cred.TimeoutSeconds = tsVal;

                        _credentials[alias] = cred;
                    }
                }

                // 激活顺序
                if (dict.TryGetValue("activeAliases", out string activeJson))
                {
                    var activeList = JsonParser.ParseStringArray(activeJson);
                    if (activeList != null)
                    {
                        _activationOrder = activeList
                            .Where(a => _credentials.ContainsKey(a))
                            .ToList();
                    }
                }
            }
            catch
            {
                // 解析失败，保持空状态
            }
        }
    }
}
