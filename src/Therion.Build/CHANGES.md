# Therion.Build — changes for consumers

## 2026-11 — correctness and safety fixes

### Behaviour changes
- **Windows only:** shell specs start `cmd.exe` by its full system path with
  `NoDefaultCurrentDirectoryInExePath=1`, and "reveal in file manager" starts `explorer.exe` from the
  Windows folder, so a same-named program in the working directory is never run. Linux and macOS are
  unchanged. `ICompileGate` is unchanged.
- `ExternalToolLocator` runs its `<tool> --version` probe with its working directory set to
  `<temp>/ThIDE/tool-probe` (created on demand). Before, the probe ran in the host process's current
  directory, so a host started from a project folder had `therion --version` overwrite that
  project's `therion.log`. Migration: none.

### Additive API
- `OutputListState(BuiltUtc, Stale)` and two default interface members on `IOutputArtifactCache`
  (`Save(entry, version, artifacts, state)`, `LoadState(entry, version)`). Existing implementations
  compile unchanged and report `Unknown`. `JsonOutputArtifactCache` writes two optional JSON fields
  (`built`, `stale`); older files read as `Unknown`.
