// =====================================================================
// DataToolKit — Init self-test.
//
// Verifies that DataToolService.InitializeAsync() drives the load flow correctly,
// using fake adapters (no network, no disk, no Roxane types).
//
// Lives in an Editor folder so it compiles into Assembly-CSharp-Editor and can see
// the DataToolKit types — no asmdef and no Unity Test Framework required.
//
// Run it:  menu  Tools > DataToolKit > Run Init Self-Test   (logs PASS/FAIL).
// =====================================================================

using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using DataToolKit;
using UnityEditor;
using UnityEngine;

namespace DataToolKit.EditorTests
{
    public static class DataToolKitInitSelfTest
    {
        // Scenario:
        //   Parallel tables: "A" (changed) and "B" (unchanged → must be skipped).
        //   Heavy table:     "H" (changed → fetched individually).
        // Local write-keys are pre-seeded so only B matches the server.
        private const string TableA = "A";
        private const string TableB = "B";
        private const string TableH = "H";

        public static string LastReport;
        public static bool LastSuccess;

        [MenuItem("Tools/DataToolKit/Run Init Self-Test")]
        public static void RunFromMenu()
        {
            RunInternal().Forget();
        }

        private static async UniTaskVoid RunInternal()
        {
            bool ok = await RunAsync();
            if (ok) Debug.Log(LastReport);
            else Debug.LogError(LastReport);
        }

        public static async UniTask<bool> RunAsync()
        {
            var asserts = new Asserts();

            // --- arrange ---------------------------------------------------
            var config = new FakeConfig();

            var network = new FakeNetwork
            {
                // server write-keys
                WriteKeys = { [TableA] = "A_v2", [TableB] = "B_v1", [TableH] = "H_v2" },
                // server data payloads (json strings) available for download
                DataPayloads = { [TableA] = "json_A", [TableB] = "json_B", [TableH] = "json_H" },
            };

            var store = new FakeWriteKeyStore();
            store.Seed(new Dictionary<string, string>
            {
                [TableA] = "A_v1", // differs from server → A is "changed"
                [TableB] = "B_v1", // matches server     → B is "unchanged"
                [TableH] = "H_v1", // differs from server → H is "changed"
            });

            var service = new DataToolService(config, network, store);

            // B simulates a present local cache so its "unchanged" path registers from disk.
            var loaderA = new RecordingLoader(TableA, service, localCachePresent: true);
            var loaderB = new RecordingLoader(TableB, service, localCachePresent: true);
            var loaderH = new RecordingLoader(TableH, service, localCachePresent: true);

            service.RegisterTable(loaderA, DataToolLoadMode.Parallel);
            service.RegisterTable(loaderB, DataToolLoadMode.Parallel);
            service.RegisterTable(loaderH, DataToolLoadMode.Heavy);

            // --- act -------------------------------------------------------
            await service.InitializeAsync();

            // --- assert ----------------------------------------------------

            // 1) Write-key endpoint hit exactly once, for ALL tables.
            asserts.Equal("write-key calls = 1", 1, network.WriteKeyCalls.Count);
            if (network.WriteKeyCalls.Count == 1)
            {
                asserts.SetEqual("write-key request tables", new[] { TableA, TableB, TableH },
                    network.WriteKeyCalls[0]);
            }

            // 2) Parallel batch requests ONLY changed tables (B skipped).
            asserts.True("batch data request exists", network.DataCalls.Count >= 1);
            if (network.DataCalls.Count >= 1)
            {
                asserts.SetEqual("parallel batch tables = {A}", new[] { TableA }, network.DataCalls[0]);
            }

            // 3) Heavy table fetched as its own single-table request.
            asserts.True("heavy single-table request for H",
                network.DataCalls.Exists(c => c.Count == 1 && c.Contains(TableH)));

            // 4) Each loader received the right LoadAsync arguments.
            asserts.LoaderCall("A → (A_v2, json_A, override=true)", loaderA, "A_v2", "json_A", true);
            asserts.LoaderCall("B → (B_v1, empty, override=false)", loaderB, "B_v1", "", false);
            asserts.LoaderCall("H → (H_v2, json_H, override=true)", loaderH, "H_v2", "json_H", true);

            // 5) Write-key store persisted the changed tables; B untouched.
            asserts.Equal("store[A] updated", "A_v2", store.Get(TableA));
            asserts.Equal("store[H] updated", "H_v2", store.Get(TableH));
            asserts.Equal("store[B] unchanged", "B_v1", store.Get(TableB));
            asserts.True("store saved at least once", store.SaveCount > 0);

            LastSuccess = asserts.AllPassed;
            LastReport = asserts.Report("DataToolKit Init Self-Test");
            return LastSuccess;
        }

        // =====================================================================
        // Fakes
        // =====================================================================

        private class FakeConfig : IDataToolConfig
        {
            public string BuildDataUrl(string tableNames) => "data?tables=" + tableNames;
            public string BuildWriteKeyUrl(string tableNames) => "writekey?tables=" + tableNames;
            public IDictionary<string, string> BuildHeaders() => new Dictionary<string, string>();
        }

        private class FakeNetwork : IDataToolNetwork
        {
            public readonly Dictionary<string, string> WriteKeys = new Dictionary<string, string>();
            public readonly Dictionary<string, string> DataPayloads = new Dictionary<string, string>();

            // Each entry is the set of tables requested in that call.
            public readonly List<List<string>> WriteKeyCalls = new List<List<string>>();
            public readonly List<List<string>> DataCalls = new List<List<string>>();

            public UniTask<T> GetAsync<T>(string url, IDictionary<string, string> headers)
            {
                List<string> tables = ParseTables(url);
                bool isWriteKey = url.StartsWith("writekey");

                if (isWriteKey) WriteKeyCalls.Add(tables);
                else DataCalls.Add(tables);

                var source = isWriteKey ? WriteKeys : DataPayloads;
                var response = new DataToolResponseData();
                foreach (string t in tables)
                {
                    if (source.TryGetValue(t, out string v)) response.Data[t] = v;
                }

                return UniTask.FromResult((T)(object)response);
            }

            private static List<string> ParseTables(string url)
            {
                int idx = url.IndexOf("tables=", System.StringComparison.Ordinal);
                string list = idx >= 0 ? url.Substring(idx + "tables=".Length) : string.Empty;
                var result = new List<string>();
                if (!string.IsNullOrEmpty(list))
                {
                    foreach (string s in list.Split(',')) result.Add(s);
                }
                return result;
            }
        }

        private class FakeWriteKeyStore : IWriteKeyStore
        {
            private Dictionary<string, string> _data = new Dictionary<string, string>();
            public int SaveCount { get; private set; }

            public void Seed(Dictionary<string, string> data) => _data = new Dictionary<string, string>(data);
            public string Get(string key) => _data.TryGetValue(key, out string v) ? v : null;

            public Dictionary<string, string> Load() => _data;

            public void Save(Dictionary<string, string> writeKeys)
            {
                _data = writeKeys;
                SaveCount++;
            }
        }

        /// <summary>
        /// Stand-in for GenericConfigDataToolLoader that records its calls instead of
        /// parsing/caching. Mirrors the real loader's write-key side effects:
        ///   override + json  → update write-key (downloaded fresh)
        ///   !override + cache → register from local, no write-key update
        /// </summary>
        private class RecordingLoader : IDataToolLoader
        {
            private readonly DataToolService _service;
            private readonly bool _localCachePresent;

            public string TableKey { get; }
            public string LastServerWriteKey;
            public string LastJson;
            public bool LastOverride;
            public int CallCount;

            public RecordingLoader(string tableKey, DataToolService service, bool localCachePresent)
            {
                TableKey = tableKey;
                _service = service;
                _localCachePresent = localCachePresent;
            }

            public UniTask LoadAsync(string serverWriteKey, string jsonDataServer, bool alwaysOverrideDataServer)
            {
                CallCount++;
                LastServerWriteKey = serverWriteKey;
                LastJson = jsonDataServer ?? string.Empty;
                LastOverride = alwaysOverrideDataServer;

                if (alwaysOverrideDataServer && !string.IsNullOrEmpty(jsonDataServer))
                {
                    _service.UpdateLocalWriteKey(TableKey, serverWriteKey);
                }
                // !override + local cache present → would register from disk, no write-key update.

                return UniTask.CompletedTask;
            }
        }

        // =====================================================================
        // Tiny assertion helper (avoids needing NUnit / Test Framework)
        // =====================================================================

        private class Asserts
        {
            private readonly StringBuilder _sb = new StringBuilder();
            public bool AllPassed { get; private set; } = true;

            public void True(string name, bool condition)
            {
                Record(name, condition, condition ? "ok" : "expected true");
            }

            public void Equal(string name, string expected, string actual)
            {
                bool ok = expected == actual;
                Record(name, ok, ok ? "ok" : $"expected '{expected}' got '{actual}'");
            }

            public void Equal(string name, int expected, int actual)
            {
                bool ok = expected == actual;
                Record(name, ok, ok ? "ok" : $"expected {expected} got {actual}");
            }

            public void SetEqual(string name, IEnumerable<string> expected, IEnumerable<string> actual)
            {
                var e = new HashSet<string>(expected);
                var a = new HashSet<string>(actual);
                bool ok = e.SetEquals(a);
                Record(name, ok, ok ? "ok" : $"expected {{{string.Join(",", e)}}} got {{{string.Join(",", a)}}}");
            }

            public void LoaderCall(string name, RecordingLoader loader, string wk, string json, bool over)
            {
                bool ok = loader.CallCount == 1
                          && loader.LastServerWriteKey == wk
                          && loader.LastJson == json
                          && loader.LastOverride == over;
                string detail = ok
                    ? "ok"
                    : $"got calls={loader.CallCount} wk='{loader.LastServerWriteKey}' json='{loader.LastJson}' over={loader.LastOverride}";
                Record(name, ok, detail);
            }

            private void Record(string name, bool ok, string detail)
            {
                if (!ok) AllPassed = false;
                _sb.AppendLine($"  [{(ok ? "PASS" : "FAIL")}] {name} — {detail}");
            }

            public string Report(string title)
            {
                return $"{title}: {(AllPassed ? "ALL PASSED ✅" : "FAILURES ❌")}\n{_sb}";
            }
        }
    }
}
