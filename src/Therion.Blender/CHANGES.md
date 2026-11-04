# Therion.Blender — changes for consumers

## 2026-11 — correctness and safety fixes

### Behaviour changes
- **`RenderAsync` honours `spec.Output.OutputDirectory`.** When it is set, outputs are written there
  instead of under `jobRoot/job-*`, and the temporary inputs (`model.ply`, `scene-meta.json`,
  `render.py`) are deleted from the job folder afterwards. Migration: read outputs from
  `RenderResult.OutputPaths` (or the directory you set); leave `OutputDirectory` empty for the old
  placement.
- **`RenderResult.ErrorMessage` when Blender cannot be started** (`FailureKind.Crashed`) now reads
  "Could not start Blender at '<path>': <reason>". The Microsoft Store explanation is added only on
  Windows for a path under `WindowsApps` (it used to be in every message, on every platform), and the
  text no longer names ThIDE's Preferences page. Migration: none, unless you match on the old wording.

### Additive API
- `RenderResult.StartFailedPath` — the Blender executable that could not be started; null for every
  other outcome. `BlenderRunner.IsWindowsStorePath(path)` — true on Windows for a path inside
  `WindowsApps`. `FailureKind` is unchanged (still `Crashed`).
