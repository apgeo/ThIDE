# Therion.MeshSurvey — changes for consumers

## Unreleased — first version (2026-11)

A survey derived from a 3-D model that carries none. References only Therion.Cave3D and
Therion.CaveGeometry and starts no process, so any application can take it as it is.

### Pipeline
- **`MeshSurveySession.Open(path | RawModel, InputOptions)`** reads (STL, PLY, OBJ, glb, XYZ via
  `ModelReader`) and normalises a model: unit (guessed for files without one, always reported), up
  axis, north rotation, crop, welding, consistent winding, small parts dropped, recentring. Public
  coordinates are *model coordinates* (metres, Z up, north applied); `Model.SourceToModel` /
  `ModelToSource` convert from and to the file's own.
- **`CheckLeg(from, to, LegCheckOptions)`** → `LegCheck`: `Clear`, `Tight`, `OutOfRange`, `Blocked`
  (first hit, crossings, rock thickness), `EndpointInRock`, `EndpointOutside`; exact against the mesh
  (or the scan points). Available right after `Open`.
- **`Inspect()`** → size, holes, loops of a closed mesh, the void's estimated voxels and memory.
- **`Prepare(options, guide)`** → `PreparedCave`: the void (sparse voxels; winding-number flood for
  meshes, flood with hole closing for point clouds; thin pillars closed, narrow connections cut and
  reported), its exact distance transform, loop and cavity count, and the curve skeleton
  (distance-ordered homotopic thinning; spurs pruned; junctions merged; chambers as hub, axis line or
  perimeter loop). Cached while the void and skeleton settings stay the same.
- **`Plan(cave, options, guide)`** → `DerivedSurvey`: stations, legs, splays, branches and a
  `MeshSurveyReport`. Mainline from the start (guide, largest hole, or highest end); laterals, loop
  closures and side stations chosen best value first until `CoverageOptions.Target` or a lateral
  limit stops it (`PlanReport.StopReason`); stations per passage by dynamic programming over the
  axis with the `LegStrategy` (preferred, fewest, uniform, features), hard limits on length,
  deviation and clearance, step-by-step relaxation (reported), every leg checked exactly; LRUD;
  splays (`SplayMode`: LRUD, ring, sphere, adaptive, wall-uniform).
- **`SurveyGuide`**: optional start, points, legs (`Force`), `GuideMode.Complete | OnlyGiven`,
  `GuideBlockedPolicy`, `GuideSnap`, `GuideFrame`; failing legs raise `GuideRejectedException` under
  `Error`. `SurveyGuides.FromCaveModel` makes a guide of every leg of a .lox/.3d survey.
- **Output**: `TherionCentrelineWriter` (provenance header — derived, not measured — `instrument`,
  `fix`, `cs` with `grid-angle 0` or `declination 0`, normal or cartesian data, splays as `-`,
  `dimensions` LRUD, continuations, optional sub-surveys with equates, optional synthetic instrument
  error), `MeshSurveyJson` (settings, guide, report), `PreviewModel` (a `CaveModel` with the walls as
  a scrap, for `LoxWriter`; skeleton and points as PLY).
- **`MeshSurveyOptions`** with defaults, `Validate()`, and `MeshSurveyPresets` (scan-backbone,
  mainline-only, main-and-major-laterals, thorough, synthetic-survey).
