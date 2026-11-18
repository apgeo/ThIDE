# Therion.CaveGeometry — changes for consumers

## 2026-11-18 — `GlbWriter` writes Y up

### Behaviour change
- **`GlbWriter` writes glTF's own axes, Y up**: a mesh vertex (x, y, z), Z up, is written as
  (x, z, −y). Its files used to keep the mesh's Z up, so every glTF reader (Blender, three.js,
  Cesium, model viewers) showed the cave lying on its side. The rotation keeps triangle winding,
  so faces still point the same way.

  Migration: a reader that turned ThIDE's GLB files upright itself must stop doing so; code
  that read the positions back as (x, y, z) reads (x, −z, y) now.

## 2026-11-17 — split out of Therion.Blender

This assembly was part of `Therion.Blender` until 2026-11-17. Its history before then, and the
migration note for the split (old → new namespaces, project references), are in
[../Therion.Blender/CHANGES.md](../Therion.Blender/CHANGES.md).
