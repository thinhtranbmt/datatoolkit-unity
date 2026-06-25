// =====================================================================
// SAMPLE / TEMPLATE — NOT COMPILED.
// This file lives in a "Samples~" folder, which Unity ignores on import.
// When wiring Phase 2, copy this into your Assets (e.g. Assets/Scripts/Network/DataTool/)
// and adjust to your project's real APIs.
//
// These 3 adapters are the ONLY glue between the reusable DataToolKit module
// and this specific Roxane project.
//
// References app-specific types (ServiceShared, NetworkHandler, SaveManager) that won't
// exist in a fresh project, so this file is guarded by DATATOOLKIT_SAMPLES and stays
// inert by default. Read it as a reference; to compile, add DATATOOLKIT_SAMPLES to your
// Scripting Define Symbols and adapt the type names.
// =====================================================================
#if DATATOOLKIT_SAMPLES

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DataToolKit;
using MyCore.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Roxane.DataTool
{
    /// <summary>Maps DataToolKit URL/header needs onto Roxane's ServiceShared.</summary>
    public class RoxaneDataToolConfig : IDataToolConfig
    {
        public string BuildDataUrl(string tableNames)
        {
            return ServiceShared.GetDataToolRequestUrl(tableNames);
        }

        public string BuildWriteKeyUrl(string tableNames)
        {
            return ServiceShared.GetWriteKeyRequestURL(tableNames);
        }

        public IDictionary<string, string> BuildHeaders()
        {
            return new Dictionary<string, string>
            {
                { "x-api-key", ServiceShared.GetAPIKey() }
            };
        }
    }

    /// <summary>
    /// HTTP seam adapter. IMPORTANT: the server returns a bare object { tableName: value },
    /// NOT { "Data": { ... } }. DataToolService always requests a DataToolResponseData, so we
    /// fetch the RAW body and flatten { key: value } -> DataToolResponseData.Data ourselves
    /// (each value kept as its compact JSON string). This mirrors HttpKit's
    /// KeyValueEnvelope.Flatten — do the normalization in the adapter, never rely on the
    /// transport to special-case a type.
    /// </summary>
    public class RoxaneDataToolNetwork : IDataToolNetwork
    {
        private readonly NetworkHandler _handler = new NetworkHandler();

        public async UniTask<T> GetAsync<T>(string url, IDictionary<string, string> headers)
        {
            // Fetch the raw JSON string from your HTTP client (NetworkHandler now runs on HttpKit).
            string rawJson = await _handler.GetAsync<string>(url, headers);

            var dict = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(rawJson))
            {
                var obj = JObject.Parse(rawJson);
                foreach (var kv in obj)
                    dict[kv.Key] = kv.Value?.ToString(Formatting.None);
            }

            return (T)(object)new DataToolResponseData { Data = dict };
        }
    }

    /// <summary>
    /// Persists write-keys via Roxane's SaveManager.
    /// NOTE: SaveManager currently types its API to the global DataToolWriteKeyResponseData,
    /// which is declared inside DataToolServiceManager.cs. In Phase 2, either keep that model
    /// or change SaveManager to store a plain Dictionary&lt;string,string&gt; directly.
    /// </summary>
    public class RoxaneWriteKeyStore : IWriteKeyStore
    {
        public Dictionary<string, string> Load()
        {
            DataToolWriteKeyResponseData data = SaveManager.LoadDataToolWriteKeyLocalData();
            return data?.Data ?? new Dictionary<string, string>();
        }

        public void Save(Dictionary<string, string> writeKeys)
        {
            SaveManager.SaveDataToolWriteKey(new DataToolWriteKeyResponseData { Data = writeKeys });
        }
    }
}

#endif
