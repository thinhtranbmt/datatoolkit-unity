using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace DataToolKit
{
    // =====================================================================
    // DataToolKit — reusable static-data loader.
    // Zero game-specific dependencies. Wire it with the 3 adapters below.
    // Only hard dependencies: UnityEngine + UniTask (Cysharp.Threading.Tasks).
    // =====================================================================

    /// <summary>
    /// Builds project-specific request URLs and headers
    /// (base url, project id, version, api key, write-key endpoint).
    /// </summary>
    public interface IDataToolConfig
    {
        string BuildDataUrl(string tableNames);
        string BuildWriteKeyUrl(string tableNames);
        IDictionary<string, string> BuildHeaders();
    }

    /// <summary>Thin HTTP GET seam. Implement with your own web client.</summary>
    public interface IDataToolNetwork
    {
        UniTask<T> GetAsync<T>(string url, IDictionary<string, string> headers);
    }

    /// <summary>
    /// Persists table write-keys used for server change-detection.
    /// A simple map of tableKey -> writeKey.
    /// </summary>
    public interface IWriteKeyStore
    {
        Dictionary<string, string> Load();
        void Save(Dictionary<string, string> writeKeys);
    }

    /// <summary>One loadable table.</summary>
    public interface IDataToolLoader
    {
        string TableKey { get; }
        UniTask LoadAsync(string serverWriteKey, string jsonDataServer, bool alwaysOverrideDataServer);
    }

    public enum DataToolLoadMode
    {
        /// <summary>Batched into a single request and loaded concurrently.</summary>
        Parallel,

        /// <summary>Fetched one-by-one to avoid memory/CPU spikes (large tables).</summary>
        Heavy
    }

    /// <summary>
    /// Normalized response for both the data and write-key endpoints: a flat map of
    /// tableName -> payloadOrWriteKey (each value kept as its JSON string).
    ///
    /// NOTE: the server's wire shape is a bare object { tableName: value }, NOT
    /// { "Data": { ... } }. The <see cref="IDataToolNetwork"/> adapter is responsible for
    /// flattening the wire object into this <c>Data</c> dictionary (see Samples~ for a
    /// flattening adapter). The Kit never assumes a transport-level normalization.
    /// </summary>
    [System.Serializable]
    public class DataToolResponseData
    {
        public Dictionary<string, string> Data = new Dictionary<string, string>();
    }
}
