using System;
using System.Collections.Generic;
using NPCLife.Core;

namespace NPCLife.Tests.Helpers
{
    /// <summary>
    /// 内存 FakeCacheStore。用于需要 ICacheStore 的纯逻辑测试。
    /// </summary>
    public class FakeCacheStore : ICacheStore
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public void Cache<T>(string key, T value) => _data[key] = value;

        public T FetchCache<T>(string key, T fallback = default)
        {
            if (_data.TryGetValue(key, out var v) && v is T typed)
                return typed;
            return fallback;
        }

        public bool TryFetchCache<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var v) && v is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public T FetchOrRebuild<T>(string key, Func<T> factory)
        {
            if (TryFetchCache<T>(key, out var cached))
                return cached;
            var rebuilt = factory();
            Cache(key, rebuilt);
            return rebuilt;
        }

        public void ClearCache(string key) => _data.Remove(key);

        public IEnumerable<string> ListCacheKeys() => _data.Keys;
    }
}
