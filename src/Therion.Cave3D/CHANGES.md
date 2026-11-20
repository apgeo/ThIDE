# Therion.Cave3D — changes for consumers

## 2026-11-20 — unknown `.lox` chunks are skipped

### Behaviour change
- **`LoxReader` skips a chunk of a type it does not know** (anything but 1–6, e.g. from a newer
  Therion) instead of refusing the whole file. The chunk header's own sizes bound it; a header
  whose sizes do not fit the file still throws `CaveFileFormatException`.
  Migration: code that relied on the exception to reject such files checks
  `CaveModel.UnknownChunkTypes` instead.
- **`CaveModelReader.Detect` recognises a `.lox` that starts with an unknown chunk type**, when the
  type is a small non-zero number and the first chunk's sizes fit the file. A first chunk of type
  1–6 is recognised as before.

### Additive API
- `CaveModel.UnknownChunkTypes`: the chunk types a `.lox` file carried that the reader skipped,
  each once, in first-seen order. Empty for `.3d` and for files with only known chunks.

## 2026-11-17 — split out of Therion.Blender

This assembly was part of `Therion.Blender` until 2026-11-17. Its history before then, and the
migration note for the split (old → new namespaces, project references), are in
[../Therion.Blender/CHANGES.md](../Therion.Blender/CHANGES.md).
