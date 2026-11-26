# Therion.CaveGeometry — changes for consumers

## 2026-11-27 — the walls in parts

- **`WallParts.Split(geometry)`**: the walls as `WallPart`s (`Index`, `Mesh`, `Bounds`, `Branches`,
  `TriangleCount`), tubes by branch of the reduced graph (grown to `PartLength`, 400 m of passage),
  scrap walls by a `GridSize` (100 m) grid; positions, colours, texture coordinates and reveal
  values kept. `IsLarge(geometry)`, `Subset(mesh, triangles)`.
- **`GeometryResult.TriangleLegs`**: the leg of every triangle of synthesized tubes (empty for scrap
  walls). The tubes are built with it always; their mesh is unchanged.

## 2026-11-26 (later) — tours: the main passage, what hangs on it, and which of it to visit

### Additive API
- **`TourPlanner.Plan(graph, model, options?, leads?)`** gives a **`TourPlan`**: a tour of a cave as
  far as the passage network alone decides it. No camera and no time; pure and deterministic (ties
  go to the lower station index, and the legs' order in the file does not matter).
- **The trunk** (`TourOptions.Trunk`, `TourTrunkKind`): `Longest` (the longest passage, begun at
  the end nearer the start), `Farthest` (from the start to the station farthest along the
  passages), `Deepest` (to the lowest station), `Through` (to another entrance, or to
  `TourOptions.End`), `Via` (through `TourOptions.Via`, to `End`). `Auto` is `Farthest` when there
  is a start (`TourOptions.Start`, or the first station flagged as an entrance), else `Longest`.
  The trunk is a simple path in the start's part of the cave: `TourPlan.TrunkStations`,
  `TrunkLength`, `TrunkLeads`, `Component`; the other parts are counted
  (`OtherComponents`, `OtherComponentsLength`).
- **Side systems** (`TourPlan.Branches`, each a **`TourBranch`**): the trunk's legs taken out, every
  connected remainder is one. `Kind` is `Branch` (it hangs on the trunk at one station;
  `Stations` is the way from `Junction` to its farthest station) or `Bypass` (at two or more;
  `Stations` runs from the first to the last, `Rejoins`). A leg between two trunk stations that is
  not the trunk's own is a bypass of one leg. Each has `Id` ("B1" … in tour order: along the
  trunk, at one junction the shorter first; the ids do not depend on the choice), `JunctionAt`,
  `Length` (all its legs), `RouteLength`, `Reach`, `DepthChange`, `MeanWidth` (left + right of the
  legs' shots, weighted by length; null without them), `Leads`, `Surveys`, `Rank` (by reach, then
  length) and `Choice`.
- **The choice** (`TourChoice`: `ByRank`, `Included`, `Lead`, `Short`, `OverCount`, `Excluded`;
  `TourBranch.Chosen`): `TourOptions.MinLength` (null: the greater of `DefaultMinLength` 50 m and
  `MinLengthTrunkShare` 2 % of the trunk), `MaxCount` (12), `WithLeads` (a side system with a
  lead is chosen whatever its length), `Include` and `Exclude` (branch ids, station names, survey
  names; a choice by hand wins over every rule, and `TourPlan.Unmatched` lists the entries that
  named nothing), `Depth` (2 also lists what hangs on the chosen branches' own ways, "B3.1", with
  `Parent` and `Depth`).
- **Leads** are the stations given by index, or the stations the model flags as continuations.
- An option that cannot be met is an `ArgumentException` whose parameter name is the option's
  (`tour.start`, `tour.via[1]`, `tour.end`, `tour.trunk`): a station that is not in the model, in
  more than one survey (`name@survey` tells them apart) or on no leg; no second entrance; stations
  in parts that are not joined; a way that would pass a station twice.

## 2026-11-26 — shortest paths as stations

### Additive API
- **`CenterlineGraph.ShortestPath(from, to)`**: the station indices of the shortest way along the
  legs (in metres) between two stations, both ends included; `[from]` for the same station, empty
  when no leg joins them. Deterministic.

## 2026-11-25 — reveal values on the vertices of a mesh

### Additive API
- **`RevealField`** works out a number for every leg of a cave, at its two ends, which an
  animated threshold is compared with to show the cave part by part (`RevealLeg`, in the order
  and with the legs of `TubeMesher.Legs`: no splays, surface legs or duplicates). Three sources
  (`RevealSource`, `RevealOptions`):
  - `Date`: the day the leg was surveyed (`CaveShot.DateFrom`), as a decimal year
    (`RevealField.DecimalYear`), the same at both ends. A model without dates can take them from
    a second model of the same cave (`RevealOptions.Dates`: a `.lox` has the walls, the `.3d`
    the dates): legs are matched by where their two stations are, to the centimetre, in the
    files' own coordinates. A leg without a date has the earliest one (`LegsWithoutDate` counts
    them); a model without a single date has no field (`Build` returns null).
  - `Order`: leg *i* runs from *i* to *i* + 1, from its end that is already there to its new one.
  - `Distance`: metres along the passages from the entrances, or from `FromStation`; a part of
    the cave that is joined to nothing starts at its own entrances, or at the first station of
    its first leg (`Starts` names them all). A station the model has not, or one on no leg, is an
    `ArgumentException`.
- **`GeometryOptions.Reveal`** makes the geometry stage build the field
  (`GeometryResult.Reveal`) and give the wall mesh its values (**`CaveMesh.Reveal`**,
  `HasReveal`; `Combine` keeps them only when every part has them):
  - tubes: every vertex has the value of the end of the leg its ring stands at
    (`RevealField.OnTubes`);
  - surveyed walls (scraps): every vertex has the value of the nearest place on the centreline,
    looked for at every metre of every leg (`RevealField.OnWalls`).
- **`PlyWriter`** writes the values as the float vertex property `reveal`
  (`PlyWriter.RevealProperty`), after the colours and the texture coordinates; Blender's PLY
  importer (4.2, 4.5, 5.0) keeps it as a mesh attribute of that name. **`GlbWriter`** writes them
  as the float `SCALAR` attribute `_REVEAL` (`GlbWriter.RevealAttribute`), between the positions
  and the indices in the buffer. STL and OBJ leave them out.

### Behaviour to know
- **With values, a tube mesh can have more vertices than without.** Where two legs that share a
  ring of vertices at a station have different values there (a leg of 1987 meets one of 2003; a
  junction in a reveal by order), the ring is written once for each, so that no triangle lies
  across two dates and neither leg shows before its time. The triangles, and where every corner
  of them is, are the same. A reveal by distance never needs it. Without
  `GeometryOptions.Reveal` the mesh is, byte for byte, what it was.

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
