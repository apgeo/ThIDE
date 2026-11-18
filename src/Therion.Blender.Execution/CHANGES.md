# Therion.Blender.Execution — changes for consumers

## 2026-11-18 — the terrain surface

### Behaviour change
- `BlenderRenderService` converts the terrain surface (and its image) when the spec's
  `Surface.Mode` is not `Off`, points the script at them, and removes `surface.ply`,
  `surface.png` and `surface.jpg` from the job folder with the other scratch files.

## 2026-11-17 (later) — a Blender newer than tested is used with a warning

### Additive API
- `BlenderLocator.NewestTestedVersion` (5.0) and `BlenderLocator.UntestedVersionWarning(version)`.
  `BlenderRenderService.RenderAsync` puts that warning first in `RenderResult.Warnings` when the
  Blender it found is a newer release (5.1 or later); it renders anyway.

## 2026-11-17 — split out of Therion.Blender

This assembly was part of `Therion.Blender` until 2026-11-17. Its history before then, and the
migration note for the split (old → new namespaces, project references), are in
[../Therion.Blender/CHANGES.md](../Therion.Blender/CHANGES.md).
