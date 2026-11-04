# Therion.Build — changes for consumers

## 2026-11 (later) — programs started by full path on Windows

### Behaviour changes
- **Windows only:** a program `ProcessSpec` (`UseShell: false`) is started by the full path
  `ExecutableSearch.Resolve` finds. A bare name is looked up in the system folders and then in the
  folders of PATH that are full paths; the host's own folder, its current directory and relative PATH
  entries such as `.` are no longer searched. A relative path with a folder in it (`tools\build.cmd`)
  is taken from the spec's `WorkingDirectory`. A program that is not found is refused:
  `ProcessRunner.RunAsync` returns a `StartError`, and `ProcessRunner.StartInfo` throws
  `FileNotFoundException`. Migration: a task that relied on a program in the current directory names
  it by its full path, or by a path relative to its working directory. Linux and macOS are unchanged.
- `ExternalToolLocator`'s PATH fallback skips relative PATH entries on every platform, so a tool is no
  longer found in the host's current directory through a `.` in PATH. Migration: none.

### Additive API
- `Therion.Build.Automation.ExecutableSearch`: `Resolve(fileName, workingDirectory)` and the
  platform-independent `ResolveOnWindows(fileName, workingDirectory, pathVariable, systemDirectories, fileExists)`.

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
