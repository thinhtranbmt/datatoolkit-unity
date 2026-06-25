using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DataToolKit
{
    /// <summary>
    /// Loads a single table of type <typeparamref name="T"/>:
    ///  - alwaysOverride: parse server json -> register -> cache to disk.
    ///  - otherwise (write-key unchanged): load from disk cache, else download, else fallback.
    /// Registration is delegated to the caller via <c>register</c> (e.g. push into a ServiceLocator).
    /// Caching is a plain JSON file at <c>persistentDataPath/{TableKey}.json</c>.
    /// </summary>
    public class GenericConfigDataToolLoader<T> : IDataToolLoader
    {
        private readonly Action<T> _register;
        private readonly Func<T> _fallbackProvider;
        private readonly DataToolService _service;
        private readonly Func<string, T> _parser;

        public string TableKey { get; }

        private string CacheFilePath => Path.Combine(Application.persistentDataPath, TableKey + ".json");

        /// <param name="tableKey">Server table name (also the cache file name).</param>
        /// <param name="register">Called with the loaded data; you decide where it lands.</param>
        /// <param name="fallbackProvider">In-code default used when server data is missing/invalid.</param>
        /// <param name="service">Owning service (for single-table download + write-key updates).</param>
        /// <param name="parser">Optional custom json parser. Defaults to JsonUtility.FromJson&lt;T&gt;.</param>
        public GenericConfigDataToolLoader(
            string tableKey,
            Action<T> register,
            Func<T> fallbackProvider,
            DataToolService service,
            Func<string, T> parser = null)
        {
            TableKey = tableKey;
            _register = register;
            _fallbackProvider = fallbackProvider;
            _service = service;
            _parser = parser ?? (json => JsonUtility.FromJson<T>(json));
        }

        public async UniTask LoadAsync(string serverWriteKey, string jsonDataServer, bool alwaysOverrideDataServer)
        {
            try
            {
                if (alwaysOverrideDataServer)
                {
                    if (!TryParse(jsonDataServer, out T serverData))
                    {
                        LoadFallback(serverWriteKey);
                        return;
                    }

                    RegisterAndSave(serverData);
                }
                else
                {
                    if (TryLoadFromLocal(out T localData))
                    {
                        _register(localData);
                        return;
                    }

                    string serverJson = await _service.LoadSingleTable(TableKey);
                    if (!TryParse(serverJson, out T serverData))
                    {
                        LoadFallback(serverWriteKey);
                        return;
                    }

                    RegisterAndSave(serverData);
                }

                _service.UpdateLocalWriteKey(TableKey, serverWriteKey);
            }
            catch (Exception ex)
            {
                LoadFallback(serverWriteKey);
                Debug.LogError($"[DataToolKit] Failed to load {TableKey}/{serverWriteKey}: {ex}");
            }
        }

        private bool TryParse(string json, out T data)
        {
            data = default;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            data = _parser(json);
            return IsValidData(data);
        }

        private bool TryLoadFromLocal(out T data)
        {
            data = default;
            if (!File.Exists(CacheFilePath))
            {
                return false;
            }

            string json = File.ReadAllText(CacheFilePath);
            return TryParse(json, out data);
        }

        private void RegisterAndSave(T data)
        {
            _register(data);

            string dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(CacheFilePath, JsonUtility.ToJson(data, true));
        }

        private void LoadFallback(string serverWriteKey)
        {
            T fallback = _fallbackProvider != null ? _fallbackProvider() : default;
            if (fallback != null)
            {
                _register(fallback);
            }

            Debug.LogError($"[DataToolKit] Using fallback data: {TableKey}/{serverWriteKey}");
        }

        private static bool IsValidData(T data)
        {
            if (data == null)
            {
                return false;
            }
            if (data.Equals(default(T)))
            {
                return false;
            }

            return data switch
            {
                ICollection collection => collection.Count > 0,
                _ => true
            };
        }
    }
}
