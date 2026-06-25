using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace DataToolKit
{
    /// <summary>
    /// Reusable orchestrator that loads static-data tables from a remote DataTool-style API.
    /// It knows nothing about any specific game: URLs/headers, HTTP and persistence are injected.
    ///
    /// Flow (InitializeAsync): fetch all write-keys -> compare with local -> load Parallel tables
    /// (one batched request, concurrent) -> load Heavy tables (one-by-one).
    ///
    /// App-specific bootstrap (an "init/config" table, force-update/migration, DI wiring) lives in the
    /// app around this service — NOT inside it. See README.
    /// </summary>
    public class DataToolService
    {
        private readonly IDataToolConfig _config;
        private readonly IDataToolNetwork _network;
        private readonly IWriteKeyStore _writeKeyStore;

        private readonly Dictionary<string, IDataToolLoader> _loaders = new Dictionary<string, IDataToolLoader>();
        private readonly List<string> _parallelTables = new List<string>();
        private readonly List<string> _heavyTables = new List<string>();

        private Dictionary<string, string> _localWriteKeys;

        public DataToolService(IDataToolConfig config, IDataToolNetwork network, IWriteKeyStore writeKeyStore)
        {
            _config = config;
            _network = network;
            _writeKeyStore = writeKeyStore;
        }

        // =====================================================
        // REGISTRATION
        // =====================================================

        public void RegisterTable(IDataToolLoader loader, DataToolLoadMode mode)
        {
            _loaders[loader.TableKey] = loader;

            if (mode == DataToolLoadMode.Heavy)
            {
                _heavyTables.Add(loader.TableKey);
            }
            else
            {
                _parallelTables.Add(loader.TableKey);
            }
        }

        public IDataToolLoader GetLoader(string tableKey)
        {
            _loaders.TryGetValue(tableKey, out IDataToolLoader loader);
            return loader;
        }

        // =====================================================
        // MAIN FLOW
        // =====================================================

        public async UniTask InitializeAsync()
        {
            DataToolResponseData serverWriteKeys = await GetAllWriteKeyAsync();
            HashSet<string> sameTables = CompareWriteKey(serverWriteKeys);

            await LoadParallelAsync(_parallelTables, serverWriteKeys, sameTables);
            await LoadHeavyAsync(_heavyTables, serverWriteKeys, sameTables);
        }

        private async UniTask LoadParallelAsync(
            List<string> tables,
            DataToolResponseData serverWriteKeys,
            HashSet<string> sameTables)
        {
            DataToolResponseData tableResponse = await GetAllDataTableAsync(tables, sameTables);

            List<UniTask> tasks = new List<UniTask>();

            for (int i = 0; i < tables.Count; i++)
            {
                string tableKey = tables[i];

                IDataToolLoader loader = GetLoader(tableKey);
                if (loader == null) continue;

                string serverWriteKey = GetValue(serverWriteKeys, tableKey);

                if (tableResponse.Data != null && tableResponse.Data.TryGetValue(tableKey, out string json))
                {
                    tasks.Add(loader.LoadAsync(serverWriteKey, json, true));
                }
                else
                {
                    tasks.Add(loader.LoadAsync(serverWriteKey, string.Empty, false));
                }
            }

            await UniTask.WhenAll(tasks);
        }

        private async UniTask LoadHeavyAsync(
            List<string> tables,
            DataToolResponseData serverWriteKeys,
            HashSet<string> sameTables)
        {
            for (int i = 0; i < tables.Count; i++)
            {
                string tableKey = tables[i];

                IDataToolLoader loader = GetLoader(tableKey);
                if (loader == null) continue;

                string serverWriteKey = GetValue(serverWriteKeys, tableKey);

                string json = string.Empty;
                if (sameTables == null || !sameTables.Contains(tableKey))
                {
                    json = await LoadSingleTable(tableKey);
                }

                if (!string.IsNullOrEmpty(json))
                {
                    await loader.LoadAsync(serverWriteKey, json, true);
                }
                else
                {
                    await loader.LoadAsync(serverWriteKey, string.Empty, false);
                }
            }
        }

        // =====================================================
        // NETWORK
        // =====================================================

        public async UniTask<string> LoadSingleTable(string tableName)
        {
            string url = _config.BuildDataUrl(tableName);
            DataToolResponseData response = await _network.GetAsync<DataToolResponseData>(url, _config.BuildHeaders());

            if (response?.Data != null && response.Data.TryGetValue(tableName, out string result))
            {
                return result;
            }

            return string.Empty;
        }

        public async UniTask<DataToolResponseData> GetAllDataTableAsync(List<string> tables, HashSet<string> ignoreTables)
        {
            List<string> requested = new List<string>();
            for (int i = 0; i < tables.Count; i++)
            {
                if (ignoreTables != null && ignoreTables.Contains(tables[i])) continue;
                requested.Add(tables[i]);
            }

            string tableRequest = string.Join(",", requested);
            if (string.IsNullOrEmpty(tableRequest)) return new DataToolResponseData();

            string url = _config.BuildDataUrl(tableRequest);
            return await _network.GetAsync<DataToolResponseData>(url, _config.BuildHeaders());
        }

        public async UniTask<DataToolResponseData> GetAllWriteKeyAsync()
        {
            List<string> all = new List<string>(_parallelTables);
            all.AddRange(_heavyTables);

            string tableRequest = string.Join(",", all);
            if (string.IsNullOrEmpty(tableRequest)) return new DataToolResponseData();

            string url = _config.BuildWriteKeyUrl(tableRequest);
            return await _network.GetAsync<DataToolResponseData>(url, _config.BuildHeaders());
        }

        // =====================================================
        // WRITE KEYS
        // =====================================================

        public Dictionary<string, string> LocalWriteKeys()
        {
            return _localWriteKeys ??= _writeKeyStore.Load() ?? new Dictionary<string, string>();
        }

        public void UpdateLocalWriteKey(string tableKey, string newWriteKey)
        {
            Dictionary<string, string> local = LocalWriteKeys();
            local[tableKey] = newWriteKey;
            _writeKeyStore.Save(local);
        }

        private HashSet<string> CompareWriteKey(DataToolResponseData serverWriteKeys)
        {
            HashSet<string> same = new HashSet<string>();
            if (serverWriteKeys?.Data == null) return same;

            Dictionary<string, string> local = LocalWriteKeys();

            foreach (KeyValuePair<string, string> kv in serverWriteKeys.Data)
            {
                if (local.TryGetValue(kv.Key, out string localKey) && kv.Value == localKey)
                {
                    same.Add(kv.Key);
                }
            }

            return same;
        }

        private static string GetValue(DataToolResponseData data, string key)
        {
            if (data?.Data != null && data.Data.TryGetValue(key, out string v)) return v;
            return string.Empty;
        }
    }
}
