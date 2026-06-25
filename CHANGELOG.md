# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-06-25
### Added
- Initial release.
- `DataToolService` — plain-class orchestrator: write-key fetch → compare → parallel batch → heavy sequential.
- `GenericConfigDataToolLoader<T>` — per-table parse / disk-cache / fallback, with an overridable parser.
- Seams: `IDataToolConfig`, `IDataToolNetwork`, `IWriteKeyStore`, `IDataToolLoader` + `DataToolResponseData`.
- Editor self-test (`Tools ▸ DataToolKit ▸ Run Init Self-Test`) using fake adapters.
- Samples: config/network(flatten)/write-key adapters + bootstrap (guarded by `DATATOOLKIT_SAMPLES`).
