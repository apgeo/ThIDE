# Therion.CaveGeometry — changes for consumers

## 2026-11-20 — recentring moves surfaces too

### Behaviour change
- **`GeometryResult.RecenteredModel.Surfaces` and `.SurfaceBitmaps` are in the local frame**, like
  stations, shots and scraps: the recentre offset is subtracted from each grid's calibration
  origin and heights, and from each bitmap's calibration origin (so an image still lands on its
  grid). They used to be passed through in world coordinates. Surfaces still do not count toward
  `OriginalBounds`/`LocalBounds` or the offset: a terrain tile is usually much larger than the cave.
  Migration: code that meshed `RecenteredModel.Surfaces` with `TerrainMesher.Build(grid, offset)`
  passes `CaveVector3.Zero` as the offset now; code that meshes the source model's surfaces with
  `geometry.Offset` (as `CaveConversionPipeline` does) changes nothing.
- `RecenteredModel` keeps the source model's `UnknownChunkTypes`.

## 2026-11-18 — terrain mesher and texture coordinates

### Additive API
- `TerrainMesher.Build(grid, offset, image?, maxVertices?)` meshes a `CaveSurfaceGrid` into a
  `TerrainMesh` (`Mesh`, `Stride`) in the model's local frame (world − offset; the geometry stage
  leaves surfaces in world coordinates), two up-facing triangles per grid cell whatever way the
  calibration runs. Points with a non-finite height are left out of the triangles. A grid of more
  than `DefaultMaxVertices` (512 × 512) points is thinned with the smallest stride that fits
  (`StrideFor`), always keeping the last row and column. Returns null for a grid it cannot mesh.
- With a `SurfaceImage`, every vertex gets the texture coordinate of its world X/Y: the image
  calibration inverted, pixel origin at the image's bottom-left corner, as Therion and Loch use it.
  `SurfaceImage.TryRead(bitmap)` reads a PNG's or JPEG's size and extension from its header.
- `CaveMesh.TextureCoordinates` (`CaveUv`, optional) and `HasTextureCoordinates`; `Combine` keeps
  them only when every part has them. `PlyWriter` writes them as float `s`/`t` vertex
  properties, which Blender's PLY importer (4.2, 4.5, 5.0) turns into a UV map; it ignores `u`/`v`.
  The other writers leave them out.

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
