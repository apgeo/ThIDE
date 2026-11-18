# Therion.Blender — changes for consumers

## 2026-11-18 (follow-ups) — terrain, player plans, lead captions, bundles

### Behaviour changes
- `SurfaceSpec.Opacity` defaults to 0.4 (was 0.5): the cave under the terrain shows better. It is
  still set per spec. `SPEC_HASH` of every generated script changes with it.

## 2026-11-18 (render bundle) — a scene plan, its meshes and the player in one zip

Additive. Nothing that exists changes, except that `NotCarried` names one more setting.

### Additive API
- **`RenderBundleWriter`** (namespace `Therion.Blender.Player`) writes a render bundle: everything
  Blender 4.2 or newer needs to render a scene plan on a machine without ThIDE. Pure: it writes a
  zip or a folder and starts nothing.
  - `WriteZip(content, path)`, `WriteZip(content, stream)` and `WriteFolder(content, folder)`
    return the paths written. A bundle holds `scene-plan.json`, `scene-plan.draft.json` when there
    is a draft, the files the plans name at their plan paths, the player (`render.py`,
    `thide_player/`), the run scripts `render.sh` (POSIX sh, executable in the zip) and `render.cmd`
    (CRLF), and a `README.txt` that says how to render, what will be written, what the files are and
    what the plan leaves out. The zip has no top folder.
  - The scripts run `blender -b --factory-startup --python-exit-code 64 --python render.py --
    --plan <plan>`, take `--draft` to play the draft plan, pass any further options to the player,
    and use the Blender named in the `BLENDER` environment variable, else the one on the PATH, else
    the usual install folder (macOS, Windows).
  - **Deterministic**: entries in ordinal order, all dated 1980-01-01 00:00, fixed permissions, so
    the same content gives the same bytes every time (on the same .NET runtime; the zip's "made by"
    field says the operating system it was written on).
  - Refused with `ArgumentException`: an invalid plan or draft; an output directory that is not
    beside the plan (a bundle renders into its own folder); a file path outside the bundle or one of
    the bundle's own names; a model whose file, or a module folder whose files, are not given.
  - `RenderBundleContent`: `Plan`, `Draft`, `Files` (plan path → file on disk), `NotCarried`.
  - `RenderBundleWriter.FromModel(source, spec, workDirectory, draft, geometry, leads)` converts a
    model and lowers a `SceneSpec` to that content: the mesh as `model.ply`, the output in `out/`
    under the spec's base name, the draft from `ScenePlanBuilder.Draft` with its defaults, and
    `NotCarried` from `ScenePlanBuilder.NotCarried(spec)`.
  - `RenderBundleWriter.Readme(content)` gives the README on its own.

### Behaviour change
- `ScenePlanBuilder.NotCarried` names `surface` when the spec asks for the terrain surface: the plan
  has never carried it, and said nothing.

## 2026-11-18 (player) — more in a plan: models turned and stretched, the scene as a .blend file, a draft look, labels

Additive. The script generator, its scripts and the render service are untouched; a plan written
before reads and plays as before. The plan stays version 1: every addition is an optional field or
a new module name in `features`.

### Additive API
- **A model can be turned and stretched as well as moved**: `PlanModel.Rotation` (`PlanRotation`:
  an axis and degrees, right-hand rule, about the mesh's own origin) and `PlanModel.Scale` (along
  the mesh's own X, Y, Z; applied before the rotation). In the JSON:
  `"rotation": { "axis": [0, 0, 1], "degrees": 90 }, "scale": [1, 1, 2]`.
- **`ScenePlanBuilder.AddModel(plan, model, material, worldOrigin)`** adds one more model to a plan:
  a second cave, the centreline as a mesh of its own. `material` joins the look and is given to the
  model; `worldOrigin` (the `SceneMeta.Offset` of a mesh that was converted on its own) puts the
  model where it lies in the plan's frame.
- **A plan can save the scene it builds as a Blender file**: `ScenePlan.Blend` (`PlanBlendFile`,
  a `.blend` path relative to the plan and inside its folder; JSON section `blend`), played by the
  new player module `blend_file`. `PlanOutput.Render = false` (JSON `"render": false`, written only when false) sets
  the output up and renders nothing, for a scene that is saved now and rendered later.
  `ScenePlanBuilder.SaveBlend(plan, path, render)` sets all three and lists the module.
  - The file is saved when every module has built its part and before the render, so it is there
    while a long render runs. A scene that is only built (`--build-only`) is not saved.
  - In the file the render's output is named from where the file lies (`//frames/clip_####`) as
    long as it stays in the plan's folder, so the folder can be moved and rendered elsewhere, in
    parts: `blender -b scene.blend -s 1 -e 100 -a`. Use numbered frames for that; every part of a
    video would write the same file.
  - The meshes are in the file; it needs neither the plan nor the player. A Cycles device is a
    setting of the machine, not of the file: a GPU is chosen where the file is rendered
    (`blender -b scene.blend -a -- --cycles-device OPTIX`).
- **A draft look**: `PlanEngine.Workbench` (JSON `"engine": "workbench"`), Blender's solid view.
  It draws no lights, shadows or node materials, so the player gives every model the colours of its
  material as a colour attribute (`ThideDraft`): one colour for a flat material and for a noise
  ramp, the ramp itself for a height ramp, the mesh's own colours for a vertex-colour material.
  `PlanLook.Samples` picks the anti-aliasing there. The background is the world's colour.
  **`ScenePlanBuilder.Draft(plan, scale, engine)`** gives the same plan with that look: half the
  picture size by default, at most 8 samples, the output's base name with `-draft` added, and no
  saved scene. `engine: PlanEngine.Eevee` keeps materials and lights instead. A draft is one more
  plan beside the real one (`render.py -- --plan draft.json`), not a switch of the player.
  Measured on the invented medium cave, Blender 5.0.1 on a CPU, machine busy with other work:
  the flythrough at 1920×1080 with 96 samples takes about 57 s a frame in Cycles and 0.3 s as a
  960×540 draft; the orbit, where the cave fills little of the picture, 2.6 s and 0.22 s.
- **Labels**: `ScenePlan.Labels` (`PlanLabels`; JSON section `labels`), played by the new player
  module `labels`.
  - `PlanTexts`: a glow colour and strength, and `PlanText` items (`id`, `text`, `at`, `size`,
    optional `role`): texts that turn to face the camera.
  - `PlanMarkers`: a glow colour and strength, `PlanMarker` items (`id`, `at`, `radius`, optional
    `role`): glowing balls; `Pulse` (`PlanPulse`: every marker grows to `scale` times its size over
    `period` seconds, again and again) and `ByDistance` (`PlanDistanceSizing`: a marker's radius is
    `factor` × its distance from the camera, within `minRadius`–`maxRadius`).
  - Every position and size is data: nothing is laid out in Python. An id is the object's name in
    the scene, unique among texts and markers; a role is stamped on the object (`thide_role`).
  - One line per text and per marker in the JSON, as for camera poses.
- **`ScenePlanBuilder.FromSpec` carries the labels of a spec**: station labels, component labels
  and lead markers with their captions, placed and sized by `LabelPlanner` — every
  `LeadMarkerSizing` and `LabelRoofSource` — under the names the generated scripts give them
  (`stn:…`, `cmp:…`, `lead:…`, `leadtxt:…`; a second label of the same name gets `.001`). The plan
  lists `labels` in `features` when it has any. `ScenePlanBuilder.NotCarried` no longer names them;
  what a plan still does not say: overlays, the visibility events of labels, the HDRI rig. The
  "N of M station labels" notice of a capped label set is not in the plan either: it is known when
  the plan is made (`LabelPlanner.Plan(...).StationsCapped`).
- `ScenePlan.BasicFeatures` is what `PlayerFeatures` was (the four modules every plan needs, and
  the default of `ScenePlan.Features`); **`ScenePlan.PlayerFeatures` now lists every module of the
  player** in running order, the optional ones too: `models`, `camera_track`, `look`, `labels`,
  `blend_file`, `output`.
- The validator refuses a plan whose `labels` or `blend` section has no module for it in
  `features`: nothing would play it.
- The player reports itself as version 1.1 (`THIDE:player=1.1`).

### Behaviour changes
- In Blender's solid view a model now shows the plain colour of its material, and a label its glow
  colour (`diffuse_color` and the object colour are set). Renders are unchanged.
- A plan made by `FromSpec` from a spec with labels now has them, where it had none: a consumer
  that plays such plans gets the labels the generated script would show.

### What an older player does with a newer plan
A player 1.0 refuses a plan that lists `labels` or `blend_file` (it has no such module) and one
with `"engine": "workbench"`. It does not notice `rotation` and `scale` on a model or
`"render": false` on the output, and would place the model unturned and render. No player 1.0 was
released, so the plan version stays 1; from the first release on, a field like these needs a new
plan version or a module of its own.

## 2026-11-18 — the terrain surface, optionally with its image

### Additive API
- `SceneSpec.Surface` (`SurfaceSpec`): `Mode` (`SurfaceMode`: `Off`, the default, `Solid` or
  `Textured`) and `Opacity` (0–1, default 0.5, validated as `surface.opacity`). The generated script
  imports the terrain after the model's bounds are taken, so the camera still frames the cave. The
  terrain is see-through from above at `Opacity` and invisible from below (alpha 0 on back faces,
  the same in Cycles and EEVEE), and casts no shadow, so the sun rigs still light the cave under
  it. `Textured` drapes the surface image through the terrain's UV map; with no image it draws the
  terrain in one colour and the script prints a `THIDE:warning`. A spec asking for a terrain the
  model does not have also warns, and renders without one.
- `SourceSpec.SurfacePlyPath` and `.SurfaceImagePath`: the terrain files the script reads. They are
  never embedded: a self-contained script draws the terrain only while the files are where they
  were, and warns otherwise.
- `ConversionOptions.IncludeSurface` (default false) and `.SurfaceFileName` (`surface.ply`): the
  pipeline meshes the model's first surface (as Loch shows it) and saves its image beside it as
  `surface.png` or `surface.jpg`, by the image's own signature. `ConversionManifest.SurfacePath`
  and `.SurfaceImagePath` name what was written, or are null.

### Behaviour change
- The spec's JSON has the new fields, so `SPEC_HASH` of every generated script changes; with the
  terrain off the scripts are otherwise byte-identical.

## 2026-11-18 — lead marker size and label roof are choices

### Additive API
- `LeadMarkerSpec.Sizing` (`LeadMarkerSizing`): `Auto` (the default, as before: from the passage
  in a flythrough, from the cave otherwise), `Cave`, `Passage`, `CameraDistance` and
  `HiddenInFlythrough`. `CameraDistance` gives each marker a driver that scales it with its
  distance from the camera (0.015 × distance, between 0.1 m and the cave size); the expression is
  one Blender evaluates without Python, so it works in a headless render, and the pulse is part of
  it. `LabelPlan.LeadsByDistance` carries the numbers.
- `LabelsSpec.Roof` (`LabelRoofSource`): `SurveyFirst` (the default, as before), `WallsFirst`,
  `WallsOnly`, `None`. It sets both the height station labels are lifted to and the roof a
  passage-sized marker is sized from.
- `SceneMetaStation.WallRoof` and `.SurveyedRoof` keep the two roofs apart; `Roof` stays the
  survey-first value. A document without them uses `Roof` for every source but `None`.

### Behaviour change
- The spec's JSON has the two new fields, so `SPEC_HASH` of every generated script changes; the
  scripts are otherwise byte-identical.

## 2026-11-17 (player) — a scene plan, and a player that renders it

Nothing existing changes: `SceneSpec`, `ScriptGenerator` and the scripts they give are as before, and
remain what the render service uses. This adds a second way to get from a description to a Blender
scene, meant for what the script generator cannot say (several models, a camera path made elsewhere,
and later texts and annotations) and for consumers that do not start Blender themselves.

### Additive API
- **`Therion.Blender.Plan`: the scene plan**, `scene-plan.json`. A plan is one render with nothing
  left to work out: placed models, timed camera poses, the look and the output, all positions in one
  frame (metres, X east, Y north, Z up, about `frame.origin`) and all times in seconds. Anything can
  write one; it is the only thing the player reads.
  - Records: `ScenePlan` with `PlanFrame`, `PlanTimeline` (`fps`, `frameCount`), `PlanModel` (a
    `.ply` or `.glb` beside the plan, a translation, a material id), `PlanCamera` with its
    `PlanCameraKey` poses (`t`, `eye`, `target`, optional `lens`), `PlanLook` (engine, samples,
    device, picture size, `PlanMaterial`s of four kinds, `PlanLight`s of two kinds, `PlanWorld`),
    `PlanOutput`, and `Features`: the player modules the plan needs, in the order they run.
  - `ScenePlan.Sections` keeps every section the library does not know, so a plan with a section
    for a module of your own reads and writes unchanged. `changes`, `annotations` and `texts` are
    reserved names for later versions.
  - `ScenePlanSerializer` (`Write`, `Read`, `WriteFile`, `ReadFile`, `ScenePlanFormatException`):
    one line per camera pose, LF line endings, the same bytes on every platform and culture. A plan
    of a newer version is refused.
  - `ScenePlanValidator.Validate` returns `SpecError`s with field paths, as `SceneSpecValidator`
    does. Model files and module folders must be relative paths that stay beside the plan.
  - `ScenePlanSchema.Text`: the JSON schema (draft 2020-12) of the format, embedded in the assembly,
    for a program that writes plans without this library.
  - `BlenderModule.ScenePlanSchemaVersion` (1). The version is raised only by a change an older
    player must not ignore; a new capability comes as a new module name in `features`, which an
    older player refuses.
- **`ScenePlanBuilder.FromSpec(spec, framing, meta)`** lowers a `SceneSpec` to a plan with the same
  camera planners the script generator uses: the same poses, clip range, materials and lights, as
  data. `framing` is required for every template, the static one too.
  **Not carried yet**: station and component labels, lead markers, overlays, visibility events, and
  the HDRI light rig (the plan gets a dim plain world). `ScenePlanBuilder.NotCarried(spec)` lists
  what a given spec loses, so a caller can say so or keep to the script generator.
- **`CameraPlanner.PlanPoses(spec, framing)`**: `Plan`, with the static template's one pose worked
  out from the framing's bounds instead of left to the script.
- **`Therion.Blender.Player`: the player**, Python embedded in the assembly (`PlayerFiles`). It is a
  small core and one file per feature: `thide_player/core.py` reads and checks the plan and hands
  each section to its module; `registry.py` finds the modules; `api.py` is what a module may use;
  `modules/` holds `models`, `camera_track`, `look` and `output`. It prints the same `THIDE:` lines
  as a generated script, so `BlenderRunner` and `RenderProgressParser` run it unchanged, and it needs
  only `bpy`, `mathutils`, `bmesh` and the standard library. Blender 4.2, 4.5 and 5.0.
  - `PlayerWriter.WriteJob(plan, folder, files)` writes `scene-plan.json`, `render.py` and
    `thide_player/` into a folder, after copying the given meshes in. Start it like a generated
    script: `blender -b --factory-startup --python render.py`.
  - `PlayerWriter.SingleFile(plan, files)` gives the same as one script: the plan, the player and,
    if given, the meshes. No text of the plan can end up as code: it travels as JSON in a raw string
    with every apostrophe escaped.
  - Options after `--`: `--plan PATH`, `--modules DIR` (repeatable), `--build-only` (build the
    scene, do not render; also for opening the scene in Blender's window).
- **Adding a feature without touching the player.** A feature module is one Python file named after
  the feature, with `SECTION = "<plan section>"`, `build(ctx, section)` and optionally
  `render(ctx, section)`. Put it in a folder, name the folder with `--modules DIR` or in the plan's
  `moduleFolders` (only folders beside the plan), add the feature's name to the plan's `features`
  and its data to the plan as a section of its own. A name found in two places is an error.
- **GLB models** come in through Blender's own glTF importer and are joined into one object. That
  importer needs numpy: it works in the official Blender builds and not in a Blender packaged
  without numpy (Ubuntu's 5.0.1 is one), where the player refuses the plan and says why. PLY works
  everywhere. A GLB is read as glTF defines it, Y up, as `GlbWriter` now writes it (see its change log).

## 2026-11-17 (later) — lead markers inside the cave, roofs of tube models, the gallery

### Behaviour changes
- **In a flythrough a lead marker is sized from its passage**: radius 0.15 × the roof height at
  its station (×`MarkerScale`, at least 0.1 m; 1.5 m assumed where no roof is known), never more
  than the outside size. Every other camera keeps the size from the cave's diagonal. A
  flythrough no longer ends inside a ball that fills the picture.
- **`SceneMetaStation.Roof` of a tube model comes from the survey**: the largest vertical part of
  the up distance of the station's level legs (legs within about 60° of horizontal). The walls are
  asked only where there is none, and for scrap walls as before. A pitch head now has a roof (the
  walls gave none, having no floor under it), and a junction no longer gets the roof of another
  level seen through its opening. A station halfway down a shaft still has none.
- **"Map reveal" is no longer in `BuiltInPresets.All`**, so `ByName("Map reveal")` returns null:
  it revealed only labels, not the cave. `BuiltInPresets.MapReveal()` still builds it.

  Migration: code that looked the preset up by name should build it with `MapReveal()`.

## 2026-11-17 (split) — one library becomes four, and the model and geometry namespaces are renamed

### Breaking changes
- **`Therion.Blender` is split into four assemblies**, layered so that each references only the
  ones above it in this list:

  | Assembly | Holds | Starts a process |
  |---|---|---|
  | `Therion.Cave3D` | the 3-D model records, the `.lox` and `.3d` readers, the `.lox` writer | no |
  | `Therion.CaveGeometry` | centreline graph and paths, tube and scrap meshing, bounds, depth ramp, mesh writers | no |
  | `Therion.Blender` | scene description, planners, script generator, presets, scene metadata, model sources | no |
  | `Therion.Blender.Execution` | finding Blender, running it, progress, output collection, the render service | yes |

  A consumer that only reads models references `Therion.Cave3D` alone; one that meshes them adds
  `Therion.CaveGeometry`. Nothing outside `Therion.Blender.Execution` references
  `System.Diagnostics.Process`, and a test keeps it so — a rule such as "may not reach code that
  starts a process" becomes "no reference to `Therion.Blender.Execution`".
- **Namespaces of the model and geometry types are renamed.** Type names do not change:

  | Old namespace | New namespace (assembly) | Types |
  |---|---|---|
  | `Therion.Blender` (model records only) | `Therion.Cave3D` | `CaveBitmapType`, `CaveLrud`, `CaveModel`, `CavePassage`, `CavePassageStation`, `CaveScrap`, `CaveShot`, `CaveShotFlags`, `CaveShotSection`, `CaveSourceFormat`, `CaveStation`, `CaveStationFlags`, `CaveSurfaceBitmap`, `CaveSurfaceGrid`, `CaveSurvey`, `CaveTraverseError`, `CaveTriangle`, `CaveVector3`, `SurveyStyle` |
  | `Therion.Blender.Parsing` | `Therion.Cave3D.Parsing` | `CaveFileFormatException`, `CaveModelReader`, `LoxReader`, `LoxWriter`, `Survex3dReader` |
  | `Therion.Blender.Geometry` | `Therion.CaveGeometry` | `BoundingBox`, `CaveColor`, `CaveMesh`, `CenterlineBranch`, `CenterlineComponent`, `CenterlineEdgeGeometry`, `CenterlineGraph`, `CenterlinePath`, `CenterlineReducedGraph`, `DepthRamp`, `GeometryOptions`, `GeometryResult`, `GeometryStage`, `KdTree`, `RoofFinder`, `TubeLeg`, `TubeMesher`, `TubeSection`, `WallSource` |
  | `Therion.Blender.Writers` (mesh writers) | `Therion.CaveGeometry.Writers` | `GlbWriter`, `ObjWriter`, `PlyWriter`, `StlWriter` |

  Unchanged: everything else in `Therion.Blender` (`SceneSpec`, `SceneMeta`, the labels and
  materials specs, `IBlenderRenderService`, `RenderProgress`, `RenderResult`, `BlenderModule`),
  `Therion.Blender.Emit`, `.Presets`, `.Sources`, `SceneMetaWriter` in `Therion.Blender.Writers`, and
  `Therion.Blender.Execution` (same namespace, now its own assembly).
- The model assembly is not called `Therion.CaveModel` because of its main type: inside any
  `Therion.*` namespace the simple name `CaveModel` would then mean the namespace, not the type.

### Migration
1. Project references: replace the reference to `Therion.Blender.csproj` with the lowest layer the
   project needs — `Therion.Cave3D` for reading models, `Therion.CaveGeometry` for the centreline
   graph or meshes, `Therion.Blender` for scenes and scripts, `Therion.Blender.Execution` for
   rendering. Each brings the layers above it in the table.
2. `using` lines: `Therion.Blender.Parsing` → `Therion.Cave3D.Parsing`, `Therion.Blender.Geometry` →
   `Therion.CaveGeometry`, `Therion.Blender.Writers` → `Therion.CaveGeometry.Writers` (keep
   `Therion.Blender.Writers` only for `SceneMetaWriter`); add `using Therion.Cave3D;` wherever a
   model record is used — it used to come with `using Therion.Blender;`, which can go if nothing
   else from that namespace is used.
3. Lists of allowed or forbidden types by full name (architecture tests): rename by the table
   above, e.g. `Therion.Blender.CaveModel` → `Therion.Cave3D.CaveModel`,
   `Therion.Blender.Geometry.CenterlineGraph` → `Therion.CaveGeometry.CenterlineGraph`,
   `Therion.Blender.Parsing.LoxWriter` → `Therion.Cave3D.Parsing.LoxWriter`. A rule by assembly
   can instead forbid `Therion.Blender.Execution` (and, for a reader-only consumer, `Therion.Blender`).
4. No behaviour, file format or schema version changes; scripts and goldens are byte-identical.

## 2026-11-17 — synthesized tubes are joined into one skin

### Behaviour changes
- **The tubes of legs that meet at a station are joined** instead of each leg being a closed tube of
  its own that overlaps its neighbours. This changes the wall mesh of every model without scrap
  walls (`.3d` files, centreline-only `.lox` files, `WallSource.Tubes`), and so every PLY, STL, OBJ
  and glb written from one, and every render of one.
  - *A station with two legs* has one ring of vertices that both tubes use, on the plane that halves
    the bend (a mitre), so the passage is as wide round the bend as along the legs. Where the two
    legs give the station different cross-sections (their own frames, their own wall distances) each
    corner of the ring is halfway between the two. A turn of more than about 127° is cut flatter
    than its mitre, so the outside corner reaches at most twice the wall distance past the station.
  - *End caps close a tube only where the passage ends*: at a station with one leg, or whose legs
    all leave within 30° of each other (a hairpin, or one leg surveyed twice and not flagged as a
    duplicate). Those tubes are as before.
  - *A station with three or more legs* has no caps. Each tube ends in an open rim: every wall
    starts where it comes out of the other tubes at the station, so the side passage opens into the
    main one and no tube has walls inside another there. A rim that is cut on a slant has four
    vertices to each side of the cross-section, and so has the ring at the other end of that leg.
    With more than five legs at a station (a survey hub, splays not flagged as splays) the rims are
    left uncut, on the cross-section at the station.
  - Splay, surface and duplicate legs and legs shorter than a millimetre still get no tube and take
    no part in a join; legs without wall distances join at `DefaultTubeRadius`.
  - On an invented test cave of 116 legs the mesh went from 3,944 vertices and 3,712 triangles to
    1,942 and 2,768.

  Migration: do not assume a fixed number of vertices or triangles per leg, that a leg's vertices
  are its own, or that the mesh is a set of closed solids. `GeometryResult.TubeLegs` still has one
  entry per meshed leg, in shot order, and is unchanged; the walls of leg `i` are still a run of
  triangles after those of leg `i - 1`, but the run's length depends on how the leg's two ends are
  joined. A consumer that needs one closed tube per leg (a volume, a print, picking a leg by its
  vertex range) sets `GeometryOptions.JoinTubes = false`, which gives the previous mesh vertex for
  vertex.
- **A flythrough no longer passes through an end cap at every station.** On the invented test cave
  (80 frames) the camera was within 0.3 m of a wall surface at 8 frames and is at none now; the
  nearest it comes to one went from 0.04 m to 1.04 m. Four frames had more than a quarter of the
  picture blown out by the head torch on a cap in front of the lens, and two were more than half
  black behind one; none are now. The picture is darker on the whole (the mean luminance of a
  frame, median over the clip, was 0.57 and is 0.26): the torch lights a passage instead of a cap a
  metre away. Migration: none; the torch is as strong as it was, so raise `LightingSpec.Strength`
  if a flythrough should be as bright as before.
- **`SceneMetaStation.Roof` can be null for a station in a shaft or at the head of one.** With no
  end cap under it such a station has no floor straight below (a shaft's walls are too steep to
  count as one), and `RoofFinder.HeightNear` counts a roof only over a floor. On six invented caves
  with a 30 m pitch, one to four of the 116 stations lost their roof this way; a shaft station's
  roof used to be the end cap of the next leg, a few millimetres or one leg above it. Such a
  station's label stays on the station. Elsewhere the roof moved by a centimetre (median) and is
  nearer the measured height. Migration: none.
- Known limits. The rims at a junction do not meet exactly: gaps of up to a few hundredths of the
  view from the middle of the junction show the world behind (black in a head-torch render). Where
  a passage leaves a junction within about 25° of another one, the two tubes run through each other
  beyond the station as before, and a camera in the one meets the wall of the other. A leg shorter
  than its passage is wide, at a sharp bend, gets open rims instead of a shared ring.

### Additive API
- `GeometryOptions.JoinTubes` (default true). Off gives every leg a closed tube of its own, as
  before this change.
- `GeometryOptions.CapTubes` now means "close the tubes where a passage ends"; with `JoinTubes`
  off it closes both ends of every leg, as before.

## 2026-11-16 (flythrough) — the flythrough keeps to the middle of the passage and turns smoothly

### Behaviour changes
- **With synthesized tube walls the flythrough flies down the middle of each leg's cross-section**
  (halfway between the left and right walls and between floor and roof) instead of along the station
  line pushed up to 1.5 m away from the nearest wall *vertex*, which says little about where the wall
  is on a tube whose only vertices are at its stations. Corners are rounded only as far as the passage
  has room for: the route keeps `ClearanceMeters` from the walls, or 60 % of the room there is where
  that is less. On an invented test cave (80 frames) the median distance to the nearest wall surface
  went from 0.96 m to 1.40 m, and the frames within 1 m of a side wall from 22 to 2. Migration: none.
- **The flythrough starts and ends inside the passage**: about a passage radius (at least 0.5 m, at
  most 45 % of the leg) in from the first and last station, which lie on the end caps of their tubes.
  The last frame used to sit on the end cap and look out of the cave. Migration: none.
- **With real scrap walls** (no cross-sections to read) the station line is sampled every 2 m and each
  sample moved away from the nearest wall vertices in steps of at most half its distance to them,
  and only while that leaves them farther off. In a passage narrower than twice `ClearanceMeters` it
  ends in the middle; the single push it replaces could end against the far wall. Checked on
  invented walls only. Migration: none.
- **The view turns smoothly.** Its heading and pitch are averaged over about a quarter of a second
  either side, turn at no more than 100° a second, and the pitch stays within ±60°: a camera that
  tracks a target rolls over when its view passes the vertical. On the same test cave at 10 fps the
  largest turn in one frame went from 88° to 10° (176° to 10° on a larger one). The look target is
  now exactly `LookAheadMeters` from the camera (at least 1 m), in the smoothed direction, instead
  of a point on the route. Migration: none.
- **The camera speeds up from rest over the first second and slows to rest over the last** (each at
  most a quarter of the clip) instead of flying at one speed. Migration: none.
- **Every frame has its own pose, up to 1,800 frames** (it was at most 300 poses, with the frames
  between two poses on the straight line between them, which can cut a corner). A 20-second clip at
  30 fps now has 600 lines in its keyframe table instead of 300. Migration: none.
- **`FlythroughParams.SmoothingIterations = n` now draws each corner with 2^n straight pieces** (at
  most 32) on the curve that Chaikin corner cutting converges to, instead of running n passes of it.
  `0` still means no smoothing, and `ClearanceMeters = 0` still rides the station line. Migration:
  none; scripts generated from the same spec differ in the keyframe table.
- Not changed: every leg is still its own tube, closed by a cap at each end and overlapping its
  neighbours. A flythrough still passes through two caps at every station and sees the next one
  ahead, and passes through the side of a tube wherever a side passage joins. On the test cave 8
  of 80 frames are still within 0.3 m of a wall surface, each of them for that reason; before this
  change it was 12.

### Additive API
- `Geometry.TubeSection` and `Geometry.TubeLeg`: the cross-sections the tube mesher gives the two
  ends of a leg (station, axes, the four wall distances with unmeasured ones already replaced), with
  `TubeSection.Center`, `TubeLeg.SectionAt(t)`, `TubeLeg.Clearance(point)` (distance to the nearest
  side wall, negative outside) and `TubeLeg.Reversed()`.
- `TubeMesher.Legs(shots, options)` and `TubeMesher.BuildFromLegs(legs, options)`.
  `TubeMesher.Build(shots, options)` is the two together and gives the same mesh as before.
- `GeometryResult.TubeLegs`: the tubes the walls were synthesized from; empty for scrap walls.
- `CameraFraming.RouteTubes` (init-only): the tube of each leg of `CenterlinePath`.
  `CameraFraming.FromGeometry` fills it. A consumer that builds a `CameraFraming` by hand and leaves
  it null gets the scrap-wall behaviour when `WallTree` is set, and the station line otherwise.

## 2026-11-16 (leads) — leads find their station in a .lox survey tree

### Behaviour changes
- **`LeadsEnricher` matches a lead's dotted name against the `.lox` survey tree first.** A `.lox`
  file names stations within their survey ("3"), so a register lead "cave.upper.3" used to be
  placed at whichever "3" came first, or dropped. It is now placed on station 3 of survey
  `upper` in survey `cave`, and also when one name has an extra outermost survey the other lacks,
  as long as the match is unique. Migration: none.
- **A station name shared by several stations no longer places a lead** by exact or
  separator-insensitive match either (the last-segment fallback already worked this way): the
  lead is dropped rather than put on an arbitrary one of them. Migration: none.

## 2026-11-16 (later) — station labels above the passage roof

### Behaviour changes
- **Station labels sit just above the roof of their passage** instead of on the station, inside
  the passage, where the walls hid them from any camera outside the cave (102 of 116 labels on an
  invented test cave; 2 now). Stations with no wall above them keep their labels on the station.
  Migration: none.
- **Lead marker objects are named after the lead's station** (`lead:<station>`,
  `leadtxt:<station>`) instead of its note, so two leads with the same note no longer collide.
  The caption still shows the note. Migration: a consumer that looks the objects up by name in a
  generated scene uses the station name.

### Additive API
- `SceneMetaStation.Roof` (`roof` in `scene-meta.json`, omitted when null): how far above the
  station the walls close over it, in metres. Null without walls or with nothing above. The
  schema version stays 1; documents without the field read as before.
- `Geometry.RoofFinder`: the height of the wall surface above a point (`HeightAbove`), or of the
  roof of the passage the point is in (`HeightNear`).
- `LeadItem.Station` (init-only).

## 2026-11-16 — the head torch lights the passage; titles stay in frame

### Behaviour changes
- **The headlamp rig's light is 0.3 m across** instead of 15 % of the model's bounding radius. On a
  cave a few hundred metres across the old light was tens of metres wide, mostly outside the passage
  the camera was in, and flythrough frames came out almost black (median luminance about 0.002;
  about 0.3–0.7 with the new size). Renders of small models also change slightly. Migration: none.
- **The title and the depth legend are placed and sized from the visible frame** (camera lens, sensor
  fit and output aspect ratio) instead of fixed offsets that suited a square frame. In a 16:9 frame
  the title was below the picture and never rendered. Migration: none; scripts generated from the
  same spec differ in those lines.

## 2026-11 (later) — job folders are cleaned up

### Behaviour changes
- **`RenderAsync` with `spec.Output.OutputDirectory` set deletes the job folder after a successful
  render**, `job.log` included, and the result's `JobLogPath` is then null. Before, a `jobRoot/job-*`
  folder holding only `job.log` was left behind for every render. A failed or cancelled render
  still keeps its folder and `job.log`, and a render without an output directory still writes its
  outputs and `job.log` into the job folder. Migration: a consumer that reads the log after a
  successful render with an output directory reads `Warnings`/`Device` from the result instead.
- `BlenderLocator`'s PATH candidates skip relative PATH entries (such as `.`), so Blender is no longer
  found in the host's current directory that way. Migration: none.

### Additive API
- `BlenderRenderService.PruneJobFolders(TimeSpan olderThan)`: deletes the `job-*` folders under the
  job root that are older than the given age and hold nothing but render scratch (`model.ply`,
  `scene-meta.json`, `render.py`) and `job.log`. A folder holding anything else (the outputs of a
  render without an output directory) is kept. Returns the number deleted. ThIDE calls it with 7 days
  when the service is created.

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
