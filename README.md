# DataToolKit

A reusable static-data loader for Unity. Fetch versioned config tables from a DataTool-style
remote API, with **write-key change-detection**, disk caching, in-code fallback, and
**parallel / heavy** load modes. The orchestrator is a **plain class** (no singleton) — wire
it with three small adapters. Zero game-specific dependencies, mirroring the `HttpKit` /
`IAPKit` convention.

## How it works

`DataToolService.InitializeAsync()`:
1. Fetch all table **write-keys** → compare with the locally stored ones.
2. Load **Parallel** tables (one batched request, concurrent) — skipping unchanged ones.
3. Load **Heavy** tables one-by-one (large tables, avoids memory/CPU spikes).

Each table is a `GenericConfigDataToolLoader<T>`: parse server JSON → register (you decide
where it lands) → cache to `persistentDataPath/{TableKey}.json`; on an unchanged write-key it
loads from disk, else downloads, else falls back to an in-code default.

## Requirements

| Dependency | Notes |
|---|---|
| Unity 2021.3+ | — |
| UniTask (`com.cysharp.unitask`) | Required. Install separately (Git/OpenUPM). |

The core uses `JsonUtility`; there are **no registry dependencies**. (The optional flattening
sample uses Newtonsoft.Json — only needed if you enable `DATATOOLKIT_SAMPLES`.)

## Install

1. **Install UniTask first** (not resolvable from the Unity registry) — add to `Packages/manifest.json`:
   ```json
   "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
   ```
2. **Install DataToolKit:**
   ```json
   "com.mycore.datatoolkit": "https://github.com/thinhtranbmt/datatoolkit-unity.git#v0.1.0"
   ```
   Or via Package Manager UI → *Add package from git URL…*. Drop `#v0.1.0` to track `main`.

## The 3 adapters (the only glue you write)

| Seam | Responsibility |
|---|---|
| `IDataToolConfig` | Build data/write-key URLs + headers (base url, project id, version, api key). |
| `IDataToolNetwork` | HTTP GET. **Must flatten** the server's `{ tableName: value }` body into `DataToolResponseData.Data` — the transport is never assumed to special-case a type. See the sample adapter. |
| `IWriteKeyStore` | Persist `Dictionary<tableKey, writeKey>` for change-detection. |

> **Server wire shape:** the API returns a bare object `{ tableName: value }`, **not**
> `{ "Data": { ... } }`. Your `IDataToolNetwork` adapter flattens it (the sample mirrors
> HttpKit's `KeyValueEnvelope.Flatten`). This is the one gotcha to get right.

## Usage

```csharp
var service = new DataToolService(myConfig, myNetwork, myWriteKeyStore);

service.RegisterTable(
    new GenericConfigDataToolLoader<ShopTable>(
        tableKey:         "ShopTable",
        register:         data => myStore.Set(data),          // you decide where it lands
        fallbackProvider: () => DefaultShop(),                // in-code default
        service:          service,
        parser:           json => JsonUtility.FromJson<ShopTable>(json)), // optional; default JsonUtility
    DataToolLoadMode.Parallel);

await service.InitializeAsync();
```

App-specific bootstrap (an init/config table loaded first, force-update/migration, DI wiring)
lives in **your** code around the service — not inside it. See the bootstrap sample.

### Parser note
The default parser is `JsonUtility`, which does **not** preserve `Dictionary<>` or top-level
arrays. If a table needs those, pass a Newtonsoft-based `parser` to the loader.

## Samples
Import **DataTool Adapters (template)** from the Package Manager. The samples reference
app-specific types so they are guarded by the `DATATOOLKIT_SAMPLES` define and stay inert
until you add that symbol and adapt the type names.

## Editor self-test
`Tools ▸ DataToolKit ▸ Run Init Self-Test` runs the init flow against fake adapters
(no network/disk) and logs PASS/FAIL — a quick sanity check after wiring.

## License
See [LICENSE.md](LICENSE.md).
