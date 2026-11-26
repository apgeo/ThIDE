# Therion.Blender — changes for consumers

## 2026-11-26 — camera routes

Nothing changes in the plan (still **version 1**) or the player: a route is a camera track, one
pose a frame, as every other. Everything here is additive.

### Breaking changes
- None. The flythrough's poses are unchanged (a golden of the built-in preset, to 10 µm, on
  invented caves holds them), its 1,800-pose limit included.

### The route camera
- **`CameraTemplate.Route`** with **`CameraSpec.Route`** (`RouteParams`, JSON `camera.route`):
  `From`, `To` and `Via` (the stops, in order), or `Stations` (stations to pass in order; the
  camera stops only at the first and last). A station is named as the model names it, or as
  `name@survey` where the name is in more than one survey; `RouteParams.Entrance` (`"entrance"`,
  the first station flagged as an entrance) and `RouteParams.Farthest` (`"farthest"`, the
  station farthest from the start along the passages) stand for stations the model gives. Null
  parameters fly from the entrance (or, in a model with none, the first dead end of its largest
  part) to the farthest station. Stops are joined by the shortest passages.
- Holds: `StartHoldSeconds`, `HoldSeconds` (each stop on the way, 1 s), `EndHoldSeconds`. The
  camera speeds up from rest and slows down to rest over about a second at every stop (the
  flythrough has one ease for its whole path), and at a stop on the way turns its view in place,
  at the flythrough's turn limit, from the way it came to the way it goes on (a half turn at a
  dead end, about 1.8 s).
- Speed by passage size: `CameraRoutes.SpeedFor(radius)` = `clamp(2 · radius, 1.5, 12)` m/s, the
  radius from the tube of the nearest leg (the mean of its two cross-sections' half width and half
  height), `CameraRoutes.FallbackRadius` (2 m) where there is no tube (scrap walls). Speed changes
  between passages at no more than 1 m/s².
- `RouteParams.Timing`: `Fit` (the default) fits the flight into the spec's duration, scaling the
  travel and keeping holds and turns (unless they would take more than 80 % of the clip);
  `Natural` lets the route set the duration, filled in by **`CameraRoutes.WithNaturalDuration`**
  (the CLI does; a planner given a `Natural` route without it fits it to the spec's duration).
- `LookAheadMeters`, `ClearanceMeters`, `SmoothingIterations` as the flythrough's. Where the camera
  flies inside a passage is the flythrough's: the middle of the tubes, corners rounded, a passage
  radius short of the route's ends and of a stop where it turns back.
- One pose for every frame, with no limit (the flythrough keeps its 1,800).
- Lead markers sized `HiddenInFlythrough` are hidden for a route too.
- `SceneSpecValidator` checks `camera.route.*`. A station that is not in the model, is on no leg,
  is ambiguous, or cannot be reached is an `ArgumentException` from the planner naming the
  station and the field (`camera.route.via[1]`).

### Routes for other planners
- **`CameraRoutes.Resolve(framing, route)`** → `RoutePath` (the station indices passed, the stops
  among them, the hold at each). **`CameraRoutes.Measure(framing, route)`** → `RouteMeasure`
  (length; travel, hold and turn seconds; `Seconds`): what a route takes, for a planner with a
  time budget. **`CameraRoutes.NameOf(framing, station)`** names a station by index so that a
  route finds it again (`name` or `name@survey`).
- **`CameraFraming`** carries `Centerline`, `Model` and `TubeLegs` (from `FromGeometry`), and
  frames any way through the cave: **`AlongPath(points)`**, **`AlongStations(indices)`**; the
  tube lookup **`CameraFraming.TubesAlong(path, tubeLegs)`** is public.

### CLI
- `therion-cli blender … --route <from>[:<via>…]:<to> [--route-seconds <n>]`: a route camera over
  whatever the preset or spec had; the route sets the clip's length unless `--route-seconds`
  gives one. A station it cannot find is a usage error (exit 2) before anything is written.

## 2026-11-26 — marker symbols and sizes, outlines, id over role, photo layout, a reveal over a leg graph

The plan stays **version 1** and this is player **1.10**. Everything here is a new key, a new
entry point or a rule that became wider, so every plan of yesterday plays as it did. A plan that
uses a new key needs a player 1.10 (an older player refuses the key by name, as it refuses every
key it does not know).

### Breaking changes
- `ScenePlanValidator` no longer refuses a change by id and a change by role of the same property
  of one target (see below): a producer that relied on that refusal to catch its own mistakes
  checks it itself. Two by id, or two by role, are refused as before.
- Nothing else in this library's API.

### Annotations: marker symbols and sizes by the camera
- **`PlanAnnotation.Symbol`** (`annotations[].symbol`, a marker): `ball` (the default), `pin`,
  `cone`, `cube`, `ring` or `image`. A pin and a cone stand with their point on the anchor, a cube
  and a ring are centred on it, a ring turns to the camera as a text does, and `image` is a small
  picture of `image` (a `.png` or `.jpg` that travels with the plan), turned to the camera,
  `2 × radius` wide. Each is a few primitives built by the player; a part's name is the symbol's
  (`pin`, `cone`, `cube`, `ring`, `picture`).
- **`SizeBy`** (`sizeBy`: `distance` | `screen`), with **`Factor`**, **`MinRadius`** and
  **`MaxRadius`**: the marker's scale follows its distance from the camera as
  `labels.markers.byDistance` does (the same driver). `distance`: the radius is `factor` metres per
  metre of distance; `screen`: it is seen `factor` times the picture's width across wherever the
  camera is (by the camera's lens at the start of the clip). Both are held between the two radii
  (a quarter and four times `radius` unless given), and `radius` is the size the shape is made at.
- `AnnotationSpec` has `Symbol`, `SizeBy`, `SizeFactor`, `MinSize` and `MaxSize`; a marker that is
  an image names its file in `Image`, which `AnnotationPlanner.Files` carries as a photo's.

### Annotations: outlines
- **`PlanAnnotationKind.Outline`** (`kind: "outline"`, with `points` (two at least), `width`,
  `color`, `strength`): a tube through the points, drawn as the plan's lines are and shown and
  hidden by changes as any annotation is (role `outline`). The points are in the plan's frame, or
  in the model's own coordinates with `model`; `at` is the point the annotation is said to be at.
  The producer puts the points on the surface; nothing is draped. `AnnotationSpec` has
  `AnnotationKind.Outline` with `Points` and `Size` (the width).

### Changes: by id over by role
- A change **by id and a change by role may both change the same property of one target**; the
  **change by id has it**, whichever comes first in the list, and the change by role leaves that
  property of that target alone and changes it of every other target of the role. Two changes by
  id, or two by role, of one property of one target are still refused. The player works the
  changes by id first.

### `AnnotationPlanner`: a model's point, and photos laid out
- **`Near(camera, timeline, point, within, model, changes)`** works the nearness of a point of a
  model, with the point in the model's own coordinates: it goes where the model's `scale` and
  `offset` keys take it frame by frame (stretch along its own axes, its turn, its place plus the
  offset; the change by id has the model's keys before the one by role). `ModelPointAt` is the
  place at one time. The key rules are those of the player except for the third key on a frame,
  which is read as the second.
- **`Layout(cards, camera, timeline, options)`** for photos (`PhotoCard`, `PhotoLayoutOptions`,
  `PhotoLayout`): photos whose times overlap are shown **one at a time** (the shown one stays
  until its time is over, then the `PhotoOrder` picks: nearest to the camera, or highest
  `Priority`); photos shown together are moved apart **on the screen** (on the frame in the middle
  of the first time each is shown, kept inside the picture and a `Gap` apart; a card whose anchor
  is off the picture is left alone) and a card that moved gets a line to its anchor; a card's
  **`Caption`** is a label under it (`<id>-caption`, role `photo-caption`) shown and hidden with
  it. The result is annotations and changes to add to a plan. A card is fixed in the scene: the
  separation is exact for that frame and fine for a camera that moves slowly.

### Reveal
- **`RevealField.FromLegs(legs, options, entrances)`** is the entry point over a plain graph of
  legs (`RevealGraphLeg`: station names, positions, an optional date); no `CaveModel` is needed.
- **`RevealPlanner.Change(target, min, max, start, end, edge, glow, glowColor, fps)`** is the rule
  for a reveal's first and last key, its edge and its glow, public and free of `SceneSpec` and
  `SceneMeta`; the plan of a spec is made by it. **`RevealPlanner.ByRole(role)`** names every
  model and line of a role: give the parts of one cave (walls, centreline) one role and key them
  all, in step, with one change.
- **`RevealPlanner.Counter(target, min, max, format, decimals)`** is the counter, and `RevealSpec`
  has **`CounterFormat`** (with `{}` where the number goes: `"{} m"`) and **`CounterDecimals`**:
  with a format the counter counts a reveal by distance (metres from the entrance) and by order
  (the number of the leg) too. Without a format only a reveal by date has one, as before.

### Texts
- **`OverlaySpec.TitleFadeIn`** and **`TitleFadeOut`** split `TitleFade`. `TitleFade` alone means
  what it did: the title takes that long to appear and, as a card, to go. `TitleFadeIn` and
  `TitleFadeOut` each override their half; `TitleFadeOut` needs `TitleSeconds` (only a card goes).

### `LampReach`
- **`Keys` and `Apply` take an optional `daylight`**, a share 0 to 1 for each time (each frame
  for `Apply`): the lamp is raised only by the share of daylight that is gone (1 leaves it as it
  is, 0 gives the whole reach, in between is linear). `Keys` takes time linear in the number of
  times on a straight run, where it took the square; the keys are the same.

## 2026-11-25 (later) — keys before the clip and three on a frame, a backdrop apart from the light, bands, photos over the picture

The plan stays **version 1**, and this is the same player **1.9** as the entry below: the two
were made in one step. Everything here is a new key or a rule that became wider, so every plan of
yesterday plays as it did. A plan that uses one of the new keys needs a player 1.9 (a player 1.8
refuses the key by name; a key before the clip or a third key on a frame it refuses with "keys
are in rising order of time" or "no more than two keys").

### Breaking changes
- None in this library's API. `ScenePlanValidator` no longer refuses a change key with a negative
  time, and takes three keys on a frame where it took two: a producer that relied on those
  refusals to catch its own mistakes checks them itself.
- `SceneMeta` (`scene-meta.json`, still version 1) has one more field, `centerlineLength`; a
  document without it reads as before.

### Changes: keys before the first frame
- **A key's `t` may be negative** (`changes[].*[].t`, also of `visible`): the clip then begins on
  the way from that key, part of the way through a move, a smooth move or a fade. A smooth move
  from −1 s to 1 s is half done on the first frame, and is the same curve it would be from 0.
  A `visible` key at −0.5 s with a fade of 1 s has the target half there on the first frame.
  Before, such keys were refused, and a producer had to work out the first frame's value and
  could not say "part of a smooth move" at all.

### Changes: a third key on a frame
- **Up to three keys of a property may fall on one frame.** The first is where the value was
  going, the second (`constant`) is what the frame shows and has from then on, and a third
  (`constant`) is **the value the move to the next key starts from, which no frame shows**: the
  frame after is one frame along a move that began at the third key's value. For a track that
  restarts a curve at a cut: `[…, {t: 2, value: 20}, {t: 2, value: 50, easing: "constant"},
  {t: 2, value: 100, easing: "constant"}, {t: 3, value: 200}]` shows 50 on the frame at 2 s and
  110 a tenth of a second later (at 10 fps). The curve is still one Blender works out by itself.

### The world: a backdrop apart from the light
- **`PlanWorld.Backdrop`** (`look.world.backdrop`) is how bright the camera sees the world, set
  apart from `strength`, which is then only how strongly the world lights the scene. Without it
  nothing changes: one strength for both.
- **`PlanChange.WorldBackdrop`** (`changes[].worldBackdrop`, no target) keys it; `worldStrength`
  then keys the light alone. A sky that stays behind a cave while its light goes out underground
  is `worldStrength` to 0 with `worldBackdrop` left at 1; a world that lights and is not seen is
  `backdrop: 0`. Either sets the world apart; a world that is set apart no longer shows its
  backdrop following `worldStrength`.
- **`PlanChange.WorldRotation`** (`changes[].worldRotation`, no target) keys how far the world's
  image is turned, in degrees, as `look.world.rotation` has it: an HDRI that turns while the
  clip runs. For a world of the kind `image`.

### Materials: a ramp in bands
- **`PlanMaterial.Banded`** (`look.materials[].banded`, written only when true): the colour of a
  stop holds from the stop up to the next one and changes there at once. It replaces two stops
  a ten-thousandth apart at every edge: bands by depth are one stop each, `{ at: <where the
  band begins>, color }`, in rising order. In a draft a band ends along the faces of the mesh.

### Texts: an image over the picture, and fade lengths
- **`PlanScreenItemKind.Image`** (`texts.items[].kind: "image"`, with `image`): a PNG or a JPEG
  laid over the frames in the second stage, as a text is: `width` is its width as a share of the
  picture's width, `height` (optional) its height as a share of the picture's height, `at` the
  point that `alignX`/`alignY` name, and it has `start`, `end`, `fadeIn`, `fadeOut`, `opacity`
  and `slot` as every item has. Where a PNG is clear the picture shows through. Its file
  travels with the plan (`PlayerWriter.FilesOf`, a bundle, a single file) and is checked as a
  photo is. It changes no frame of the first stage: `--stage compose` is enough after one changes.
- **`ScreenTextPlanner.PhotoInset(id, image, imageInfo, pictureWidth, pictureHeight,
  wholePicture, start, end, fade)`** lays a photo out as such an item: in the lower right corner
  (at most 0.3 of the width and 0.35 of the height, above the credits line) or over the whole
  picture inside the margin, keeping its shape. **Not built:** a `SceneSpec` annotation that asks
  for an inset, and with it the panel, the command line and the MCP tools; a producer of plans
  adds the item itself.
- **`EndCardSpec.Fade`** (`endCard.fade`) and **`OverlaySpec.TitleFade`** (`titleFade`), both in
  seconds and written only when set: how long the end card takes to appear, and how long the
  title takes to appear and (a title card) to go. `SceneBuilders.NotDrawnByTheGenerator` names
  `labels.overlays.titleFade`.

### Photos: transparency
- A photo annotation whose PNG has an alpha channel is **not there where the image is clear**
  (a mix with nothing, by the image's alpha). A tint, a fade and an opacity work on it as on any
  photo. A JPEG, and a PNG without alpha, have the material they had. The draft's mosaic is whole.

### Placeholders
- **`{length}`** and **`{depth}`** (`TextPlaceholders.Length`, `.Depth`) beside `{cave}`: the
  length of the model's centreline (`SceneMeta.CenterlineLength`: the sum of its legs, without
  those flagged surface, splay or duplicate) and its vertical range (lowest to highest station),
  in whole metres with the unit. They are the model's own numbers, said as such in the guide;
  a model that gives none leaves the text out.

### Not built
- A line whose width stays the same on the screen. It needs the tubes rebuilt at every frame
  from the camera's place (geometry nodes), in three Blender versions, for the render, the
  draft, a reveal and a saved scene alike; the cost was out of proportion to this step.

### Verified
- Without Blender: the validator's and the serializer's rules for every new key, the schema and
  the player's known keys in step, the photo inset's layout, the fades from the spec to the
  texts, the placeholders against an invented cave's own legs and heights.
- In Blender 4.2.23, 4.5.16 and 5.0.1: the value of a light's energy, a model's place, its fade
  and its opacity at each of twenty frames, for a clip that begins half-way through a smooth
  move and inside a fade and for a frame with three keys; a world that lights and is not seen,
  then is seen and lights nothing, by the pixels of the corner and of the cave; a world's image
  turned by a key against the same image turned by `rotation` (the same picture); a ramp in
  bands against a blended one (a fifth of the in-between pixels, the edges' own); a photo with a
  clear half, tinted; an image in the corner and over the whole picture, by its columns and rows.

## 2026-11-25 — a cave seen from far away: unlit materials, a lamp that reaches, and four findings

The plan stays **version 1**. The player calls itself **1.9**: a material of the look may be
**unlit**, and the sun and sky rig is the same sky in every Blender. A plan with an unlit material
needs a player 1.9 (an earlier one refuses the key by name); every plan of yesterday plays as it
did, but for the sky in Blender 5 (below). A render that was stopped under a player 1.8 starts its
frames again; writing the output again from kept frames (`--stage compose`) is not affected.

### Breaking changes
- None in this library's API.
- **Behaviour: the sun and sky rig in Blender 5.** `look.world.kind: "sky"` picked Blender 5's
  newer sky model (`MULTIPLE_SCATTERING`) and left the sky's height at Blender 5's default of
  100 m, so one plan gave two different pictures: Blender 5.0.1 showed the sky above a cave as
  (79, 90, 104) of 255 where Blender 4.2.23 showed (166, 147, 118). The player now asks for the
  model both have (`NISHITA` in Blender 4, `SINGLE_SCATTERING` in Blender 5; measured equal to
  0.1 % in linear light) at height 0. Blender 5.0.1 now gives (162, 143, 114), and 4.2 and 4.5
  are unchanged. Migration: none; a render of the *SunSky* rig in Blender 5 looks as it does in
  Blender 4. **The script generator is not changed** (it is frozen): its scripts still pick the
  newer model in Blender 5, so the two builders differ there under the sun and sky.
- **Behaviour: the bounds a plan goes by include the walls.** `CameraFraming.FromGeometry` now
  also gives `MeshBounds`, the box round the wall mesh, and `ScenePlanBuilder.FromSpec` uses it
  (`DrawnBounds`) for the one pose of the static camera and for the heights of the depth tint,
  as a generated script uses the mesh it imported. Before, both went by the stations alone: the
  static camera stood 0.6 % nearer than the generated script's and the depth colours stopped
  changing over the top and bottom 1.5–1.8 m of the walls. Migration: none; a plan of the same
  spec differs in that pose and in the ramp's `low`/`high`. The animated templates still go by
  `LocalBounds`, as the generated scripts do. A `CameraFraming` made by hand without `MeshBounds`
  behaves as before.
- **Behaviour: texts in a picture higher than wide.** `ScreenTextPlanner` makes the title, its
  subtitle and the north arrow smaller by the picture's shape (by `width / height`, not below a
  half) when the picture is higher than wide, and keeps the title's widest line (`wrap`) inside
  the room the north arrow leaves. In 9:16 "Invented Cave" took two lines and the arrow touched
  it; it now takes one, clear of the arrow. A picture of 3:2 or wider is laid out as before
  (between 1:1 and 3:2 only the title's `wrap` is a little narrower beside an arrow).

### Additive API: an unlit material
- **`PlanMaterial.Unlit`** (`look.materials[].unlit`, written only when true) makes a material
  that no light changes: it shows the colours of its kind (flat, the mesh's own, a ramp) as a
  screen shows them, in a cave no lamp reaches as under the sun. It emits to the camera only, so
  a line that reads in the dark does not light the walls round it. **`PlanMaterial.Strength`**
  (`strength`, only with `unlit`) is how brightly: 1 is the colours as they are.
- A model or a line wears it as it wears any material, and what changes of a material changes
  of it: a tint, an opacity, a fade, a reveal. The draft shows it in its colours, as it shows
  every material. Roughness has no part in it.
- For survey lines, a route, an outline: give the line's material `Unlit = true`. Markers and
  labels (`labels`, `annotations`) glow already and need nothing.
- Measured on a line 3 m thick, of 255: lit, under three suns 168; lit, by the 80 W head lamp
  from 25 m 14 and from 140 m 0; unlit 201.5 in all four.

### Additive API: a lamp that reaches (`LampReach`)
- **`LampReach.Apply(plan, reference = 3, lightId = null, distances = null)`** keys the energy of
  the plan's head lamp by the distance the camera looks over: `energy × (distance / reference)²`
  beyond the reference distance, and the lamp's own energy nearer. It is a change of the lamp's
  `energy` (`changes[].energy`), so the player needs nothing new for it, and a plan says what it
  does. A camera that stands still gives the lamp one energy and no keys.
- The distance is from each pose's `eye` to its `target`, at every frame, straight from pose to
  pose. **A producer whose targets are only where the camera focuses passes its own distances**
  (`distances`: seconds and metres, in rising order of time), e.g. to the nearest cave.
- `LampReach.Keys(distances, energy, reference, tolerance)` gives the keys alone (keys that a
  straight line between their neighbours stands in for, within 2 %, are left out),
  `LampReach.Distances(camera, timeline)` the distances of a plan's camera, and
  `LampReach.Energy(energy, distance, reference)` one value. `DefaultDistance` is 3 m: a wall
  that far from the 80 W lamp is lit to about the middle of what a picture shows.
- `Apply` throws `ArgumentException` when a change keys that lamp's energy already.
- **The spec:** `LightingSpec.LampReach` (`lighting.lampReach`, written only when true), for the
  `Headlamp` rig. `ScenePlanBuilder.FromSpec` applies it; `SceneBuilders.NotDrawnByTheGenerator`
  names it (`lighting.lampReach`), and the script generator leaves it out.
- Measured, of 255: the same line by the lamp without the reach 14 at 25 m and 0.1 at 140 m; with
  it 176 and 179. An invented cave in one still from 148 m: 0.1 without, 154 with.
- An exposure keyed over time was the alternative, and is not built: it would also brighten what
  is unlit, the sky and what the sun lights.

### Additive API: leads without a project (`ModelLeads`)
- `ModelLeads.FromModel(CaveModel)` gives the stations a model flags as continuations (a `.lox`
  file's flag) as `SourceLead`s, each named with its surveys before it so that
  `LeadsEnricher` finds it; `ModelLeads.Read(json)` / `ReadFile(path)` read a leads file (a JSON
  array of station names, or of `{ "station", "kind", "note" }`), and throw `FormatException`
  with the entry's place and nothing of the file's contents.

### Command line
- **`therion-cli blender` passes leads.** It passed none, so the lead markers of a preset never
  showed from the command line. It now marks the stations the model flags as continuations, or
  those of `--leads <file.json>` (once for each file), for a render and for `--bundle`.
- **`--lamp-reach`** lights the scene by the head lamp with `LampReach` on.

### The panel (ThIDE)
- **Light** has "The lamp reaches as far as the camera looks", for the *Headlamp* rig. It is the
  spec's `lighting.lampReach`, so "Save preset" keeps it.

### Verified
- Without Blender: the lamp's energy by distance, its keys within their tolerance, the distances
  of a camera, `Apply` and its refusals, the spec's field to the plan and to JSON; an unlit
  material through the serializer, the validator, the schema and the player's known keys; the
  bounds of the drawn mesh in the static pose and the depth ramp; the title and the arrow in
  9:16, 3:4 and 1:1; leads from a model's flags and from a file, and their errors; the command
  line's leads in a bundle's plan.
- In Blender 4.2.23, 4.5.16 and 5.0.1, by the numbers of the picture: the unlit line and the lit
  one under three suns and by the lamp from 25 m and 140 m (Cycles), in EEVEE and in a draft; an
  unlit line beside a wall in the dark, which stays black; a tint over an unlit line; the lamp
  with and without its reach from both distances and on a whole cave; the sky and its light on a
  cave within 4 of 255 of one set of numbers in all three versions; the static camera of a plan
  and of a generated script in one place (to a hundred-thousandth of their distance); a title
  on one line and clear of the north arrow in a 360 × 640 picture.

## 2026-11-25 — an export is the scene a render shows; the panel's light, reveal, annotations and texts

The plan stays **version 1**. The player calls itself **1.8**: started in Blender's window with
`--build-only`, it leaves the scene seen through its camera. Nothing a plan says has changed, and
a player 1.7 plays every plan of today. A render that was stopped under a player 1.7 starts its
frames again (the note kept with them names the player); writing the output again from frames
that were kept (`--stage compose`) is not affected.

### Breaking changes
- None in this library's API. What changes for a user of **ThIDE** is below ("The panel"): "Export
  script…", "Open in Blender…" and the MCP tool `export_render_script` now write the player's
  scene where they wrote a generated script. Migration: choose the script generator ("Scene
  builder (advanced)" → *Generator*; `builder: "generator"`) for the export as it was.
- `therion-cli blender --export` is **unchanged**: it is the script generator's export, and
  `--bundle <folder>` is the player's.

### Additive API: an export by either builder
- **`IBlenderRenderService.ExportAsync(spec, source, outputDir, ExportOptions, …)`** returns a
  `RenderExport`. `ExportOptions.Builder` chooses who builds the scene when the export is run,
  as `RenderOptions.Builder` does for a render: `Auto` and `Player` the player, `Generator` the
  script generator (`ExportScriptAsync`, which stays as it was).
  - **The player's export is the folder of a render bundle** (`RenderBundleWriter.WriteFolder`):
    `scene-plan.json`, `render.py`, `thide_player/`, the meshes and images at their plan paths
    (`photos/`, `hdri/`), `README.txt` (`ExportOptions.Language`: "en" or "ro"), `render.sh`,
    `render.cmd`, `LICENSE`, `NOTICE`, and the captions as `out/<base name>.srt` and `.vtt`. Its
    plan is the plan a render of the same spec plays, but for the output directory, which is
    `out` beside the plan: the folder can be moved.
  - **`ExportOptions.SingleFile`** writes one `render.py` instead (`PlayerWriter.SingleFile`),
    with every file the plan names inside it. For the script generator it turns on
    `SourceSpec.SelfContained` and `EmbedMesh`.
  - **`ExportOptions.Interactive`** writes the job folder a render plays, for Blender's window:
    start `RenderExport.ScriptPath` without `-b` and with `RenderExport.ScriptArguments`
    (`--build-only`) after `--`. Its output directory is the spec's, or `out` in the folder.
  - `RenderExport` says what was written (`Files`), by which builder, how many frames the scene
    has, what the script generator leaves out (`LeftOut`), what the scene shows less of than was
    asked for (`Notices`), and `TextsInSecondStage`: the scene has texts, which the player writes
    over the finished frames and Blender's window therefore does not show.
- **The interface method has a default implementation**, so an implementation of
  `IBlenderRenderService` written before today still compiles: it exports the generated script
  through `ExportScriptAsync` and says `Builder = Generator`. `BlenderRenderService` overrides it.
- A spec the player cannot plan (a station an annotation names that the model does not have, an
  HDRI image that is neither `.hdr` nor `.exr`) throws `ArgumentException` from `ExportAsync`;
  an export to render has then written nothing.

### The player
- **`--build-only` in Blender's window** (Blender started without `-b`) sets every 3-D view to
  look through the scene's camera once the scene is built. In the background nothing changes.

### The MCP tools
- **`start_render`** takes `hdri` (an image in the workspace, a `.hdr` or an `.exr`),
  `hdriStrength` and `hdriRotation` (degrees). The image is held to the workspace, as a photo
  is; the strength and the turn alone go with a preset that is lit by an image already, and are
  refused otherwise. A user preset's own image is left as the user saved it.
- **`export_render_script`** exports through `ExportAsync`: by default the player's folder,
  with `oneFile: true` one script, with `builder: "generator"` the generated script. It takes
  what `start_render` takes for the scene (`title`, `credits`, `endCard`, `endCardSeconds`,
  `captions`, `annotations`, `reveal`, `revealFrom`, `revealDates`, `hdri`, `hdriStrength`,
  `hdriRotation`), and its answer has `builder`, `oneFile`, `leftOut` and `warnings` beside
  what it had. What the exported scene renders goes to `out/` beside the script (a generated
  script's outputs land beside it, as before).

### The panel (ThIDE)
- **Light**: the light rig, its strength, and for the rig of an image (*HdriFile*) the image
  (typed, or picked with "Browse…", which also chooses the rig) and its rotation.
- **Reveal**: off, or by distance, order or date; the station to measure from, the file with the
  dates, when it starts, whether it ends with the clip or at a second of its own, its edge and
  its glow. A preset's glow color and counter are kept while the reveal is changed, and while
  it is off.
- **Annotations**: a JSON file is loaded in place of the scene's annotations, or they are
  cleared; a line says how many there are and of which kinds, and what is wrong with them is
  listed (what the validator says, a photo that is missing or is not an image a plan carries).
  A file that is not a list of annotations leaves the scene's as they are.
- **Texts over the picture**: Subtitle, Scale bar, North arrow, Depth legend, Fade in, Fade out.
- All of these are the spec's fields, so "Save preset" keeps them and a preset brings them back.
  A file the scene names that is not there (the image, the dates, a photo) is listed under "Fix
  before rendering" instead of failing the render.
- **"Export script…"** exports through `ExportAsync` with the builder of "Scene builder
  (advanced)": the player's folder, one file with "Self-contained script (one file)", the
  generated script with *Generator*. "Embed the meshes in the script" can be ticked only for
  the script generator.
- **"Open in Blender…"** opens the player's scene (`ExportOptions.Interactive`), and says that
  the texts over the picture are not in Blender's window when the scene has any.

### Verified
- Without Blender: the exported folder's files; its plan equal to the plan the render service
  gives Blender for the same spec, but for the output directory (a spec with texts,
  annotations with a photo, a reveal and a turned HDRI image); one file with every file inside;
  the export to look at equal to a render's job folder to the last key; the script generator's
  export and what it leaves out; a refused image; the MCP tools' new parameters and refusals;
  the panel's view model (every new field to the spec and back from a preset, the annotations
  file, the blockers, what the two commands ask the service for).
- In Blender 4.2.23, 4.5.16 and 5.0.1: the exported folder and the exported single script
  render, started as the README says, frame for frame the picture a render of the same spec
  gives; the export to look at builds the scene with its annotations and without its texts;
  and, in a window on a display of its own, every 3-D view of it looks through the camera.
- The panel, driven as a user drives it (the UI tests, with a stand-in for Blender): the light,
  the reveal, the annotations and the texts set in the panel are in the plan that is rendered;
  an export's folder and single file hold that plan; "Open in Blender…" starts Blender on it
  with `--build-only`.

## 2026-11-25 — texts in presets: the cave's name by a placeholder, and built-in presets that write it

Nothing here changes the plan or the player (still version 1 and 1.7): a plan holds the texts
filled in. What changes is what a spec may say and what the built-in presets do say.

### Breaking changes
- **The built-in presets write the cave's name over the picture.** Each has
  `labels.overlays.title = "{cave}"`; "Helix descent" and "Full flythrough" also
  `titleSeconds = 3` (`BuiltInPresets.TitleCardSeconds`), which makes the title a card the clip
  opens on. So, over a model that has a name:
  - their plans have `texts` and list the `texts` module, and **a render of one takes two
    stages** (the frames as PNG, then the output from them) where it took one: more disk while
    it runs, and `job-frames.log` beside `job.log`;
  - their spec hashes are other hashes (`SceneSpecSerializer.ComputeHash`). A preset **without
    its texts** (`Labels.Overlays = new OverlaySpec()`) has the hash it had: verified against
    the release before, and held by a test with the five hashes written out;
  - the script generator draws the title in the scene, and for the two presets with a card it
    reports `labels.overlays.titleSeconds` among what it leaves out.
  Migration: for a preset as it was, clear the title (`spec with { Labels = spec.Labels with {
  Overlays = new OverlaySpec() } }`; `--title ""` on the command line; `title: ""` to
  `start_render`; the panel's Title field). The command line's default spec is "Orbit showcase"
  and has the title too.
- **`ScenePlanBuilder.FromSpec` fills in the placeholders** of the overlays' texts from its
  `meta` (below). A text that holds `{cave}` literally and was meant to is not possible any more;
  nothing wrote one before.
- **`start_render`'s `title`**: an empty text now takes the preset's title away (it used to mean
  "not given"). Omitted, the preset's is kept, as before.
- `RenderBundleWriter.FromModel` writes a bundle's subtitles from the captions with their
  placeholders filled in.

### Additive API: `TextPlaceholders`
- **`{cave}`** (`TextPlaceholders.Cave`) in a text of `OverlaySpec` stands for the cave's name:
  in `Title`, `Subtitle`, `Credits`, `Corner`, the end card's `Text` and `Subtitle`, and the
  texts of `Captions` and `CornerCaptions`. It is the only placeholder
  (`TextPlaceholders.Known`); other braces stay as they are written, and filling in is one pass
  (what a name holds is not looked at again).
- **The name** (`TextPlaceholders.CaveName(meta)`) is the model's own title, which a `.3d` file
  has (`SceneMeta.Source.Title`); else the title of the root of its survey tree, which a `.lox`
  file has; else, when that root has no title and one survey under it, that survey's title (what
  Therion writes for a project whose outermost `survey` has a `-title`). **A title that is only
  a file's name is no name**: one that ends in `.3d` or `.lox`, or equals the name of the file
  the model was read from, with or without its extension. Therion gives a `.3d` it exports the
  file's name for a title, and Survex gives one without `*title` the name of its `.svx`.
- **A text that held a placeholder and is blank once it is filled in is left out**: a title, a
  line or an end card is then none, a caption is dropped. So a preset whose title is `{cave}`
  writes nothing over a model without a name, and its render takes one stage, as before.
- `TextPlaceholders.Resolve(spec, meta)`, `Resolve(overlays, values)` (for whoever has names of
  their own: the values are by name, without braces), `Fill(text, values)`, `ValuesOf(meta)`,
  `HasAny(text)`. `Resolve` returns the same object when no text holds a placeholder.
- **Where it is filled in**: `ScenePlanBuilder.FromSpec` (so every plan, bundle and draft), and
  the render service once the model is converted, for whichever builder draws the texts (the
  script generator's title in the scene too) and for the `.srt`/`.vtt` subtitles.
  `ScreenTextPlanner.Plan(overlays, plan)`, which has no model, fills in nothing: call `Resolve`
  first.
- A spec without placeholders is untouched, and **`SceneSpec` has no new field for any of this**:
  the texts are the `labels.overlays` a spec, and so a preset file, has had. Preset files are as
  they were, read and written; a file from before the texts existed reads as the preset it was,
  with its spec's hash.

### The presets
- `BuiltInPresets`: "Orbit showcase", "Map reveal" and "Documentation stills" have the name at
  the top of the picture throughout; "Helix descent" and "Full flythrough" open on it. Nothing
  else is written: no words of any one language.
- "Map reveal" says `Reveal.Counter = true` itself (it was the default already): revealed by
  date (`--reveal date`, or `by` in the spec), it counts the years in the picture. A test holds
  that, on a cave surveyed in three years.
- A user's preset keeps whatever texts its spec has (`PresetStore`): the panel now has a Title
  field and rows for captions beside the credits line and the end card, and "Save preset" stores
  them with the rest.

### The panel (ThIDE)
- "Texts over the picture": Title, Credits line, End card, and **Captions**: rows of a text and
  the seconds it is shown from and to, added after the last one and removed one by one, with a
  strip that shows them along the clip. A row without a text is left out of the spec. The rows
  are the spec's `labels.overlays.captions`, so they are kept the way every setting of the panel
  is: in the scene it edits, and in a preset saved from it.

### Verified
- Without Blender: filling in (known and unknown braces, one pass), the name from a `.3d` title
  and from a survey tree, file names refused as names, every text of the overlays, texts left
  out when blank, each built-in preset's texts and their plans with and without a name, the
  counter of "Map reveal" by date, the five hashes of the presets without their texts and the
  hash of the default spec as the release before computed them, a preset file written before
  the texts existed, the render service filling the name in for both builders and for the
  subtitles; the panel's view model (title, adding, editing and removing rows, the strip,
  presets saved and chosen, what goes to a render).
- In Blender 4.2.23, 4.5.16 and 5.0.1: the whole suite, in which the presets as shipped are
  bundled, rendered in two stages and compared with the generated scripts' scenes.
- The panel, driven as a user drives it (the UI tests, with a stand-in for Blender): a title
  and captions typed into it are in the plan of both stages with the cave's name filled in, and
  beside the movie as subtitles.

## 2026-11-25 — HDRI in the player: a scene lit by an image all round it

The plan stays **version 1** and everything here is additive to it. The player calls itself
**1.7**: a player 1.6 refuses a plan with a world of an image by the key it does not know
("look.world.image is not known to player 1.6; a newer player may be needed"), so it never
renders such a scene under a plain colour. The script generator is as it was.

### Breaking changes
- **`ScenePlanBuilder.NotCarried` is empty for every spec.** The HDRI light rig
  (`lighting.hdriPath`) was the last thing a plan did not say; it says it now. So
  `SceneBuilder.Auto` is always the player, `SceneBuilder.Player` refuses no spec, a draft leaves
  nothing out, and `RenderResult.NotCarried`, `RenderBundleContent.NotCarried` (as
  `RenderBundleWriter.FromModel` fills it) and the README's "NOT IN THIS SCENE" are empty for
  specs that used to name `lighting.hdriPath`. **The script generator builds a scene only when it
  is asked for** (`SceneBuilder.Generator`, `--builder generator`, the panel's "Scene builder");
  it stays selectable, and loads the image from where the spec has it, as before. Migration: code
  that sent a spec with an HDRI rig to learn "Generator" from the result asks for the generator;
  code that showed `NotCarried` shows nothing. The rule itself is unchanged and kept
  (`SceneBuilders.Choose`), for whatever a later spec asks for before the player has it.
- **`ScenePlanBuilder.Lighting` gives the HDRI rig a world of the image**, not the dim plain
  world (0.05 grey at 0.5) it stood in with, and **`FromSpec` throws an `ArgumentException` for
  an HDRI rig whose image is not a `.hdr` or an `.exr` file by its name** ("lighting.hdriPath:
  the player lights a scene with a Radiance .hdr or an OpenEXR .exr image; 'panorama.jpg' is
  neither by its name."). The spec itself stays valid (`SceneSpecValidator`): a generated script
  loads whatever Blender reads. Migration: for another kind of picture, ask for the generator.
- **A plan of a spec with an HDRI rig names a file** (`hdri/<the image's file name>`), which
  whoever writes the plan somewhere must put beside it: `ScenePlanBuilder.Files(spec)` says which
  file that is (and the photos; `AnnotationFiles` alone no longer names every file of a spec).
  `PlayerWriter.WriteJob` and the bundle writer throw `FileNotFoundException` when it is not
  there, as for a photo.
- `PlayerWriter.SingleFile`'s message for a path the plan does not name reads "No model,
  material, photo or world of the plan has the path …".

### Additive API: a world of an image in a plan

```json
"look": {
  "lights": [],
  "world": { "kind": "image", "strength": 1.5, "image": "hdri/evening_sky.hdr", "rotation": 90 }
}
```

- **`kind: "image"`** (`PlanWorldKind.Image`) beside `color` and `sky`: the image is the
  background and lights the scene. `image` (`PlanWorld.Image`) is a Radiance **`.hdr`** or an
  OpenEXR **`.exr`** file, relative to the plan and inside its folder. It is an equirectangular
  picture (longitude by latitude, twice as wide as it is high) with its upper edge overhead and
  **its middle in the east (+X)**: from its left edge to its right it runs from the west by the
  north, the east and the south back to the west, as someone standing in the scene and turning to
  the right sees it. That is how Blender lays such a picture, with no turn of this library's own.
- **`rotation`** (`PlanWorld.Rotation`, degrees, optional) turns the image about the vertical,
  **anticlockwise seen from above**: at 90, what stood in the east stands in the north.
- **`strength`** is the world's, as for a colour and a sky: the factor the image is multiplied
  by. **A change of the scene keys it as it keys any world's** (`changes`: `worldStrength`, an
  entry without a target), so an image can dim, brighten, or light nothing for a while.
  `worldColor` still needs a world of the kind `color` (the validator and the player say so).
- **The file is checked before it travels** (`PlanEnvironmentImage.Check`, of a path or a
  stream): by what it holds, not by its name. A Radiance file is known by its first line and its
  size line (`-Y 512 +X 1024`), an OpenEXR file by its signature and its `dataWindow`; deep
  OpenEXR data is refused. Caps: **256 MiB**, **16384 pixels a side**, **16384 × 8192 pixels in
  all** (Blender holds sixteen bytes for each). Only the head is read (at most 1 MiB of it).
  A file that fails throws `PlanImageException`, and `PlayerWriter.WriteJob`, `SingleFile` and
  the bundle writer turn that into an `ArgumentException` that names the plan path ("The file of
  the world (hdri/notes.hdr) cannot travel with the plan: the file is neither a Radiance HDR nor
  an OpenEXR image.").
- **The player checks again** (the first bytes) before Blender reads the file, for a folder that
  came from elsewhere: `look.world.image: "hdri/x.hdr" is neither a Radiance HDR nor an OpenEXR
  image`.
- **It travels four ways.** In a job folder and in a bundle the file lies at its plan path
  (`PlayerWriter.FilesOf` lists it as the file of "the world"; the bundle's README calls it "the
  image round the scene, which lights it (HDRI)"). In a single-file script it is one of `FILES`
  (base64). In a saved `.blend` it is packed into the file by `blend_file`, as the photos are:
  the file renders with nothing beside it.
- **The draft** (Blender's solid view) draws no image: its background is a grey as strong as the
  world (0.5 × strength, at most white).
- **The scene digest** that guards the frames of a render in two stages covers the image, as
  every input file: another image is another scene.
- In the module `look`, `KEYS` has `world.image` and `world.rotation`; no new module. The world
  is an Environment Texture node through a Mapping node into the Background node whose Strength
  `changes` keys.

### Additive API: the spec

- **`LightingSpec.HdriRotation`** (degrees, anticlockwise seen from above; default 0), written
  to JSON only when it is not 0, **so a spec without it is the JSON, and has the hash, it had**.
  The player draws it; a generated script does not, and says so
  (`SceneBuilders.NotDrawnByTheGenerator` can name `lighting.hdriRotation`).
- `LightingSpec.Strength` is the world's strength for the HDRI rig (1 is the image as it is),
  as it was in the generated scripts.
- `ScenePlanBuilder.Files(spec)`, `ScenePlanBuilder.EnvironmentFolder` (`hdri`). The plan path
  is the image's own file name with every character but letters, digits, `.`, `-` and `_` made
  a `_`.
- The render service copies the image into the job folder and removes the copy with the rest
  of the scratch; `RenderBundleWriter.FromModel` puts it among the bundle's files.

### Command line
- `therion-cli blender … --hdri <image.hdr|.exr> [--hdri-rotation <degrees>] [--hdri-strength
  <n>]`, with a render and with `--bundle`: over a preset or a spec, it makes the image the light
  rig. A file that is not there, or is no such image, is a usage error (exit code 2).
- MCP: `start_render` has no parameter for it; a saved preset or a spec with the rig is rendered
  by the player.

### Verified
- Without Blender: the head checks on images made byte by byte (both formats, cut short,
  damaged, too large, an endless head), plan records, validator, schema and player keys, the
  routing (Auto, Player, Generator, drafts, frames) with and without the rig, job folder, bundle
  and single file.
- In Blender 4.2.23, 4.5.16 and 5.0.1, on an invented cave under invented skies: the image is
  the background and turns the way the plan says (four quarters of four colours, looked at from
  one side, turned by 0, 90, −90 and 180 degrees); the cave is lit from the side the image's
  bright half stands on (more than twice the light on the walls that face it) and by the
  world's strength; `worldStrength` keys take the scene from dark to lit over three frames; an
  OpenEXR image; the draft's grey; a single-file script with nothing beside it; a `.blend`
  copied alone to another folder and rendered there; a bundle unzipped elsewhere and run with
  `render.sh`; the render service with the builder left to choose.

## 2026-11-25 — reveal: a cave that appears part by part, by one animated number

The plan stays **version 1** and everything here is additive to it. The player calls itself
**1.6**: a player 1.5 refuses a plan with a reveal by the key it does not know
("changes[0].reveal is not known to player 1.5; a newer player may be needed"), so it never plays
a revealed cave as a whole one. The script generator is as it was: it draws no reveal, and says so.

### Breaking changes
- **"Map reveal" is in `BuiltInPresets.All` again**, between "Full flythrough" and "Documentation
  stills", and `ByName("Map reveal")` finds it: it reveals the cave now (by distance from its
  entrances, from 0.5 s to 9.5 s of its 12), and its component labels fade in at 9 s instead of
  from the first frame. Migration: code that counted four presets counts five.
- **A file that is not JSON is reported by where it stops being JSON, and by nothing of what is
  in it.** `SceneSpecSerializer.Read`, `ScenePlanSerializer.Read` and `PresetSerializer.Read`
  used to pass the parser's own words on, which quote the text ("'n' is an invalid start of a
  value…"): the contents of whatever file somebody named by mistake. The messages are now
  "Malformed render spec JSON: the text is not JSON at line 3, byte 14." (likewise "scene plan",
  "preset"), as `AnnotationsFile` has had it. JSON of the wrong shape is still named by its key
  ("The JSON value could not be converted to System.Int32. Path: $.seed | …"), in one line, cut
  after 300 characters. Migration: code that matched the parser's wording matches the new one.
- **The validator's sentence for a change without a target** names the reveal too: "A change of
  visibility, opacity, tint, scale, offset, energy or reveal needs a target."
- `SceneBuilders.NotDrawnByTheGenerator` can name `reveal`.
- In the player, `api.tube` takes two more optional arguments (`values`, `vertex_values`), and a
  material that `changes` made see-through for a revealed thing has one more factor in its mix
  (see below). A module from elsewhere that walks those nodes finds the product of fade and
  opacity one Multiply node further up.

### Additive API: a reveal in a plan

One more property of `changes`, of a model or a line:

```json
"changes": [
  { "target": { "id": "CaveModel" },
    "reveal": [
        { "t": 1, "value": 1962.3 },
        { "t": 9, "value": 2024.9 } ],
    "revealFront": { "edge": 1.5, "glow": { "color": [1, 0.55, 0.15], "strength": 3, "width": 2 } } }
],
"texts": { "items": [
  { "id": "year", "kind": "text", "at": [0.05, 0.17], "text": "{}", "size": 0.08, "alignX": "left", "alignY": "bottom",
    "counter": { "target": { "id": "CaveModel" }, "min": 1962, "max": 2024 } } ] }
```

- **`reveal`** (`PlanChange.Reveal`, keys of `PlanNumberKey`) is a **threshold**. Every vertex of
  the target's mesh has a **reveal value**, one number; at every moment the parts whose value lies
  below the threshold are there and the rest is not. The values are in units of their own —
  years, metres, a count — and so is the threshold. Its keys are those of any number of
  `changes`: easings, a key after the clip, two keys on a frame.
- **Where the values are.** In a PLY file, the float vertex property `reveal`; in a GLB file, the
  float `SCALAR` attribute `_REVEAL` (glTF's spelling of a name of one's own; found under the
  plan's name too); for a line, `strips[].reveal`, a number for every point
  (`PlanLineStrip.Reveal`; either every strip of a line has them or none). `revealFront.attribute`
  names another attribute than `reveal`. The player keeps them on the mesh as the attribute
  `thide_reveal`.
- **`revealFront`** (`PlanRevealFront`) is how the edge looks, all optional. `edge`: how far
  behind the threshold a part is wholly there, so a part appears over that much of the
  threshold's way (0, the default, is a cut). `glow` (`PlanRevealGlow`): what has just appeared
  shines in `color` by `strength`, all of it at the threshold and none of it `width` behind.
  Edge and width are in the units of the values.
- **A part is there once the threshold is past it**: how much of it is `(threshold − value) /
  edge`, between 0 and 1. So nothing is there while the threshold is at the lowest value, and
  everything is, with no glow left, once it is past the highest by the edge or the glow's width,
  whichever is more.
- **A counter** (`PlanScreenItem.Counter`, `PlanCounter`) makes a text of `texts` show the number
  the threshold has got to, at every frame, in place of the `{}` in its text
  (`ScenePlanValidator.CounterPlace`): a year counter. `target` is the target of the change whose
  reveal is counted, named as that change names it. The number is cut down to `decimals` decimals
  (1987.9 shows as 1987), kept between `min` and `max`, and after the clip's last frame (an end
  card) it stays as it was. The module works it out from the same keys, the way Blender works out
  the curve, and writes one strip for every stretch of frames the number lasts (`<id>.1`,
  `<id>.2`, …), which fade as one text would.
- **How it is played.** The threshold is a number on the object (`thide_reveal`, keyed), with
  the edge and the glow beside it, so objects that share a material are revealed one by one, and
  a saved `.blend` plays it with no Python. Two things read it. The **material** multiplies what
  it lets through by `(threshold − value) / edge` and mixes the glow in. A **modifier**
  (`ThideRevealCut`, geometry nodes) takes away every face that lies wholly beyond the threshold:
  a ray does not have to pass through the hidden walls of a whole cave to reach the one that is
  there (Cycles gives up after eight see-through surfaces), and the draft shows the reveal.
- **The draft** (Blender's solid view) shows a reveal as a cut along the faces of the mesh. With
  an edge or a glow it warns once: "the draft shows a reveal as a cut along the faces of the mesh,
  without its soft edge or its glow".
- **Refused, by name**: a reveal of a text, a marker, an annotation or a light (only a model or
  a line has one); of a model without a material; of a line without values, by the validator, and
  of a mesh without the attribute, by the player ("changes[0].reveal: "CaveModel" has no reveal
  values: its mesh has no attribute "reveal" (a number for every vertex)"); `revealFront` without
  `reveal`; a counter that is not a text, has no `{}`, or whose target no change reveals.
- `RevealPlanner.Centreline(field, id, width, material)` makes the centreline of a converted cave
  a line of the plan, one strip a leg, with the reveal values of its ends: for a plan that
  reveals the survey lines with the walls, or before them.

### Additive API: a reveal in a spec

`SceneSpec.Reveal` (`RevealSpec`, `RevealBy`), written only when it is set, so a spec without one
is the JSON, and has the hash, it had:

```json
"reveal": { "by": "Date", "start": 1, "end": 9, "edge": 0.03, "glow": true, "counter": true }
```

| Field | Meaning | Default |
|---|---|---|
| `by` | `Date` (the day each leg was surveyed), `Order` (the order of the legs) or `Distance` (along the passages, from the entrances) | `Distance` |
| `from` | for `Distance`: the station to measure from | the entrances; without any, the first station of the first leg |
| `datesPath` | for `Date`: a model file of the same cave that has the dates | a `.3d` of the model's name beside it |
| `start`, `end` | seconds: when the reveal begins and when the whole cave is there | 0, the last frame |
| `edge` | how soft the edge is, as a share of the range of the values (0–0.5) | 0.03 |
| `glow`, `glowColor` | whether what has just appeared shines, and in what colour | true, orange |
| `counter` | for `Date`: the year the reveal has got to, written above the scale bar | true |

- **The conversion works the values out** when it is told to: `RevealPlanner.GeometryFor(spec,
  geometry, modelPath)` gives the `GeometryOptions` for a spec (the render service and
  `RenderBundleWriter.FromModel` use it), the PLY then has the property `reveal`, and
  `scene-meta.json` says what it holds: `SceneMeta.Reveal` (`by`, `min`, `max`,
  `legsWithoutDate`, `from`). See Therion.CaveGeometry's changes for how the values are made.
- **`ScenePlanBuilder.FromSpec` lowers it** (`RevealPlanner.Plan`) to one change of `CaveModel`,
  two keys from the lowest value to past the highest, over the range that `meta.Reveal` gives;
  the edge and the glow's width (4 % of the range) become units of the values; and for `Date` a
  counter (`reveal-counter`) between the years of the first and the last survey.
- **`ScenePlanBuilder.Notices` says what a render cannot**: "The model has no survey dates to
  reveal it by (a .lox file has none; name the .3d file of the same cave); rendering without the
  reveal.", "6 legs have no survey date; they are shown from the start of the reveal.", "A still
  shows the whole cave; the reveal is left out." (a still and a set of stills:
  `RevealPlanner.Applies`).
- **Only the player draws it.** `ScenePlanBuilder.NotCarried` is as it was (the HDRI rig), so
  `Auto` gives a spec with a reveal to the player; a spec that also has what the player does not
  carry goes to the script generator, with `reveal` in `RenderResult.LeftOut`.
- A station to measure from that the model has not, and a file of dates that is not there or is
  no model, stop the conversion with an `ArgumentException` that names them.

### Command line and MCP
- `therion-cli blender … --reveal date|order|distance [--reveal-from <station>] [--reveal-dates
  <file.3d>] [--reveal-seconds <start>-<end>]`, with a render and with `--bundle`. Over a preset
  that reveals, `--reveal` changes the kind and keeps the times.
- `start_render` has `reveal`, `revealFrom` and `revealDates` (a file of the workspace). A file
  of dates that a saved preset names outside the workspace is not read.

## 2026-11-25 — annotations: labels, callouts, markers and photos that point things out

The plan stays **version 1** and everything here is additive to it. The player calls itself
**1.5**: a player 1.4 refuses a plan that lists the module `annotations` by its name ("the plan
needs the module "annotations", which this player does not have"), so it never plays an annotated
plan as a bare one. The script generator is as it was: it draws no annotation, and says so.

### Breaking changes
- **`ScenePlan.PlayerFeatures` has one more entry**, `annotations`, after `labels`. Migration:
  none for code that looks a module up by name.
- **`annotations` is a section of the plan now, not a reserved name.** It is
  `ScenePlan.Annotations`, no longer an entry of `ScenePlan.Sections`. An empty list reads as it
  did. A consumer that kept something of its own under that name moves it to a name of its own:
  a list of other things is now refused by the first key an annotation does not have.
- **A change's target also finds annotations**, by `id` and by `role` (the role an annotation
  states, or its kind: `label`, `callout`, `marker`, `photo`). `{ "role": "label" }` and
  `{ "role": "marker" }` are therefore the plain labels and markers of `labels` *and* the
  annotations of those kinds. Migration: give either a role of its own.
- **The id of an annotation is unique among everything the plan names in the scene**: models,
  lines, the texts and markers of `labels`, lights and annotations.
- `ScenePlanBuilder.Notices` has a third, optional parameter (the framing); source compatible.
- In the player, what `labels` and `lines` had for themselves is in `api` for any module:
  `api.glowing`, `api.text_facing`, `api.face`, `api.ball`, `api.stand`, `api.tube`, and the
  checks of a value `api.number`, `api.unit`, `api.amount`, `api.colour`, `api.vector`. A module
  from elsewhere that reached into `labels.glowing`, `labels.place` or `lines.add_tube` calls
  these instead.
- The draft's warning about fades has new words: "the draft shows a fade of a text, a marker or
  a photo as a cut, and no change of its opacity or tint".

### Additive API: annotations in a plan

`ScenePlan.Annotations` (JSON `annotations`, played by the new module `annotations`;
`ScenePlan.AnnotationsFeature`; `PlanAnnotation`, `PlanAnnotationKind`, `PlanFacing`,
`PlanPhotoFrame`). Each annotation has an `id`, a `kind` and an anchor `at`, the point it is
about, and everything of it is given: nothing is laid out in Blender.

```json
"features": ["models", "camera_track", "look", "annotations", "changes", "output"],
"annotations": [
  { "id": "hall", "kind": "label", "at": [12, -3, 1.5], "text": "Sala Mare", "size": 1.4 },
  { "id": "sump", "kind": "callout", "at": [40, -8, -12], "text": "Sifon", "size": 0.8, "offset": [2, 0, 6] },
  { "id": "dig", "kind": "marker", "at": [-4, 0, 0.25], "radius": 0.6, "color": [1, 0.8, 0] },
  { "id": "view", "kind": "photo", "at": [12, -3, 1.5], "image": "photos/hall.jpg", "width": 6,
    "offset": [0, 0, 5], "lineWidth": 0.06, "frame": { "width": 0.12 } },
  { "id": "notice", "kind": "photo", "at": [1.5, 2, 3], "image": "photos/notice.png", "width": 2,
    "facing": "fixed", "normal": [0, -1, 0] }
],
"changes": [
  { "target": { "id": "view" },
    "visible": [ { "t": 4, "value": true, "fade": 0.5 }, { "t": 8.5, "value": false, "fade": 0.5 } ] }
]
```

| Kind | What it is | Needs | May have |
|---|---|---|---|
| `label` | a text centred on the anchor, turned to the camera as the plan's labels are | `text`, `size` (letter height, m) | `color`, `strength` |
| `callout` | a text that stands `offset` away from the anchor, with a line from the anchor to it. The text stands on the end of the line: above it when the offset points up, below it otherwise | `text`, `size`, `offset` (not zero) | `lineWidth` (default a twelfth of `size`), `color`, `strength` |
| `marker` | a glowing ball on the anchor | `radius` | `color`, `strength` |
| `photo` | a picture from an image file, centred on the anchor, or `offset` away from it with a line to the anchor when `lineWidth` is given | `image` (.png, .jpg), `width` | `height` (default: the image's own shape), `facing`, `normal`, `up`, `frame { width, color }`, `offset`, `lineWidth`, `strength` |

- **A key of another kind is refused**, by the validator and by the player ("a marker has no
  "text""): it would otherwise be left out without a word.
- **Colours.** A text glows cream and a marker orange-red unless it has a `color` of its own
  (one material for each colour and strength in use): this is the per-item colour the `labels`
  section does not have.
- **A photo that faces the camera** (`"facing": "camera"`, the default) stays parallel to the
  picture and upright in it, wherever the camera is and however it is turned, roll included: it
  is always seen as the rectangle it is. (A text turns to where the camera *stands*, as the
  plan's labels do.) **A fixed photo** (`"facing": "fixed"`) faces along `normal` with its top
  towards `up` (default up, or north for one that lies flat) and stays so: a card that stands in
  the scene, seen from both sides.
- **A photo shows its image as a screen would**: it emits the image's colours, so it reads in an
  unlit cave and no lamp changes it, and it lights nothing itself and casts no shadow. Its
  colours pass through Blender's view transform with the rest of the picture. `strength`
  (default 1) is how bright it is. The image is drawn whole and solid: the transparency of a PNG
  is not used.
- **A frame** is a border about the picture, in its own plane, `width` metres wide, in a colour
  of its own (default light grey); a line to the anchor has the same colour.
- **An annotation of a model** (`"model": "<id of a model or a line>"`). `at` is then a point of
  that model in the model's own coordinates, as its file has them, before its scale, rotation
  and translation: a station of it. The annotation goes where that point goes, also while a
  change stretches or moves the model, and is itself neither stretched nor turned. An `offset`
  stays along the frame's axes.
- **When an annotation is there** is not part of it: it is there throughout unless a change
  shows and hides it. A change names it by `id` (every object of it: its text and its line, its
  picture and its frame) or by `role`. `visible` with a `fade` is its time window; `opacity` and
  `tint` change it too. `scale` and `offset` are refused: where it stands is its own section's.
- **Order.** `annotations` runs after `camera_track` and after `models` or `lines` when an
  annotation names one, and before `changes`. `ScenePlanBuilder.WithNeededFeatures` lists it.
- **Photos are files of the plan.** `PlayerWriter.FilesOf` lists them ("photo 'view'"), a job
  folder and a bundle carry them at their plan paths, a single-file script carries them when
  they are given, a saved `.blend` has them inside, and the frames of a render in two stages are
  rendered again when one changes.
- **A photo's file is checked before it travels** (`PlanImage.Check`; `PlanImageInfo`,
  `PlanImageException`): it is a PNG or a JPEG by what it holds, whatever it is called; its size
  can be read; it is at most 32 MiB (`PlanImage.MaxBytes`), 16,384 pixels a side (`MaxSide`) and
  64 million pixels (`MaxPixels`). Only its head is read. `PlayerWriter.WriteJob`,
  `PlayerWriter.SingleFile` and `RenderBundleWriter` refuse another file with an
  `ArgumentException` that names the plan path and the reason. The images of
  `texturedMaterials` are not checked this way; they are as they were.

### Additive API: annotations in a `SceneSpec`

- **`SceneSpec.Annotations`** (`AnnotationSpec`, `AnnotationKind`, `PhotoFacing`): what a person
  asks for. An annotation is about a **station** of the model, by its name, or a point `At` in
  the model's own coordinates (those of the model file); it has a `Kind`, a `Text` or an
  `Image`, and optionally a `Size` (letter height, radius or width, in metres), an `Offset`, a
  `Color`, `Framed`, `Facing` with a `Normal`, a `Role`, and when it is there: `From` and `To` in
  seconds, `Within` metres of the camera, and a `Fade` (default 0.5 s). Written with a spec only
  when there are any, so a spec without them is the same JSON, with the same hash, as before.
- **`ScenePlanBuilder.FromSpec` lowers them** (`AnnotationPlanner.Plan`): a station becomes its
  place in the plan's frame; a size that is left open becomes a share of the cave's diagonal (a
  text as large as a station label, a marker 1 %, a photo 10 %); a callout without an offset
  stands four letter heights above its anchor; a photo with an offset gets a line; a photo is
  named `photos/<file name>` (`AnnotationPlanner.PhotosFolder`;
  `ScenePlanBuilder.AnnotationFiles(spec)` says which file each is, for whoever writes the plan
  into a folder). A station the model does not have is an `ArgumentException` that names it.
- **Times and distances become changes.** `From`/`To` and `Within` are worked out against the
  plan's own camera poses, frame by frame, and written as the `visible` keys of a change of the
  annotation: nothing is left for Blender to decide, and a test reads from the plan on which
  frame a thing appears. `Within` is the frames on which the camera is that near; with both a
  time and a distance, both hold. It takes `Fade` to appear, from the start of its time, and
  has gone by the end of it; what is there on the first frame does not fade in and what is there
  on the last does not fade out. An annotation that is never shown is hidden throughout, and
  `ScenePlanBuilder.Notices(spec, meta, framing)` says so: "the camera never comes within 30 m
  of the annotation 'view'; it is not shown".
- **The render service** copies the photos beside the plan and removes the copies with the rest
  of the job's scratch; **`RenderBundleWriter.FromModel`** puts them into the bundle, and the
  README lists each as "a photo shown in the scene" (Romanian: "o fotografie afișată în scenă").
- **The script generator leaves annotations out**: `SceneBuilders.NotDrawnByTheGenerator` names
  `annotations`, so a render the generator builds (asked for, or because the spec has an HDRI
  rig) reports it in `RenderResult.LeftOut`. `NotCarried` is as it was: the player carries
  annotations, and a spec with them is the player's under `Auto`.
- **A file of annotations** (`AnnotationsFile.Read`, `ReadFile`, `Write`): a JSON list of
  annotations (or `{ "annotations": [...] }`), keys in camelCase, kinds as words, points and
  colours as `[x, y, z]` (or `{ "x": …, "y": …, "z": … }`), photos named relative to the file. A
  key that is not known is refused: it may be a misspelt one.
  `therion-cli blender … --annotations notes.json` adds such a file's annotations to the
  preset's or the spec's, for a render and for a bundle; the MCP tool `start_render` takes
  `annotations`, a workspace-relative file whose photos lie in the workspace.

  ```json
  [
    { "kind": "callout", "station": "main.0", "text": "Intrarea" },
    { "kind": "photo", "station": "main.14", "image": "photos/hall.jpg", "size": 6,
      "offset": [0, 0, 5], "framed": true, "within": 40 },
    { "kind": "marker", "at": [512040.5, 5120010.2, 1180], "color": [1, 0.8, 0], "from": 12, "to": 20 }
  ]
  ```

### Additive API: for whoever writes plans without a `SceneSpec`

- **`AnnotationPlanner.Near(camera, timeline, point, within)`** gives the times at which a plan's
  camera is within a distance of a point, as `PlanWindow`s; **`AnnotationPlanner.Shown(id,
  windows, timeline, fadeIn, fadeOut)`** makes the change that shows a thing in those windows;
  **`AnnotationPlanner.EyeAt(camera, frame, fps)`** is where the camera stands on a frame. A
  camera eased by `Bezier` is taken as moving evenly from pose to pose, which it does not quite:
  with a pose on every frame there is no difference.
- `AnnotationPlanner.Plan(annotations, meta, bounds, camera, timeline)` lowers `AnnotationSpec`s
  for a plan that is there already; `AnnotationPlanner.Files` names their photos.

### Behaviour changes
- **A draft shows a photo as a mosaic of its colours**: Blender's solid view draws no image, so
  the picture is cut into tiles (48 along its longer side), each in the colour the image has
  there, and the player says so once. Texts, markers and lines are drawn in their plain colours.
  A fade of any of them is a cut in a draft, as it is for labels.
- **Distances are measured where the plan puts things.** `Within` is worked out from the
  anchor's place as the plan's models stand at the start; it does not follow a model that a
  change moves.
- **`therion-cli blender … --export` says what its script leaves out.** An exported script is
  the script generator's; when the spec has something only the player draws (the later texts,
  annotations) the command now warns: "the exported script does not show
  labels.overlays.captions, annotations".
- A plan without annotations is the plan it was, byte for byte; the golden plan has not changed.

### What the player refuses, by name
- an unknown `kind`; a key of another kind; a text without `text` or `size`, a callout without an
  `offset` or with one that is zero, a marker without a `radius`, a photo without `width`;
- an `id` that something else in the scene has; a `model` the scene does not have;
- an `image` that is not a `.png` or `.jpg`, that is not there, or that cannot be read as an image;
- a `facing` it does not know, a `normal` or an `up` of a photo that faces the camera, a fixed
  photo without a `normal`, an `up` along the `normal`; a `lineWidth` of a photo without an offset;
- `annotations` listed before `camera_track`.

### Not done
- A photo over the picture in two dimensions (a corner inset, a full card) in the second stage.
- A photo's own transparency; a pulse or a size by distance for an annotation's marker (the
  markers of `labels` have both); a text wrapped to a width.
- A field for annotations in ThIDE's panel: they come from a spec, a file or an agent.

## 2026-11-25 — lights, the world's strength and a model's place change; lines; more for whoever writes plans

The plan stays **version 1** and everything here is additive to it. The player calls itself
**1.4**: a player 1.3 refuses the new keys by their path (`changes[0].energy is not known to
player 1.3`) and the new module by its name, and an earlier build of this library refuses them on
reading, so neither plays a newer plan as less than it says.

### Breaking changes
- **`ScenePlan.PlayerFeatures` has one more entry**, `lines`, after `models`. Migration: none for
  code that looks a module up by name; code that counted on positions in the list counts again.
- **`PlanVisibleKey` has a fourth value, `Easing`** (default `Linear`). Code that builds one is
  unchanged; code that takes one apart into three values (`var (t, value, fade) = key`) names
  the fourth.
- **A change's target also finds lights.** `{ "role": "light" }` is every light of the look, and
  an `id` that a light shares with a model, a text or a marker now names both. A plan that
  relied on such a shared id naming only the model gets the validator's message for the first
  property a light cannot take. Migration: give the light an id of its own.
- **The ids of models and lines are one set.** A plan cannot have a line with the id of a model.
- **Keys of a change after the clip's last frame, and two keys on one frame, are no longer
  errors** (see below). Code that leaned on the validator to refuse them checks for itself.
- The player's own `place` function moved from the module `models` to `api.place`, where a module
  from elsewhere can use it. Migration for such a module: `api.place(obj, item, where)`.
- The player's one draft warning has new words (see "Behaviour changes"); whoever matched its
  text matches the new one.

### Additive API: what a plan can say

- **Lights change** (`PlanChange.Energy`, JSON `energy`; `PlanNumberKey`). A change whose target
  is a light of `look.lights` — by its `id`, or all of them by the role `light` — keys how
  strong it is, as `PlanLight.Energy` has it: 0 puts it out. `Visible` shows and hides a light
  too, at once: a light has no material, so a key with a `Fade` is refused, and so are `Opacity`,
  `Tint`, `Scale` and `Offset` of a light. This is what puts a sun out when the camera goes
  underground.
- **The world's strength changes** (`PlanChange.WorldStrength`, JSON `worldStrength`), in a change
  without a target, as `WorldColor` does. It needs a world, of either kind: a sky has a strength
  too. The draft's background follows the colour times the strength at every frame.
- **A model is moved** (`PlanChange.Offset`, JSON `offset`; `PlanVectorKey`): metres along the
  frame's axes, added to where `PlanModel.Translation` puts the model. Models and lines only. An
  offset by role moves every model of the role by the same amount, which is what a relief that
  lifts every cave needs; `[0, 0, 0]` is the model's own place.
- **A key may lie after the clip's last frame.** The clip then ends on the way to it: a change
  that ends two seconds after the clip is two keys, not one for every frame up to the end.
- **Two keys of one property may fall on one frame** (not of `Visible`): the first is where the
  value was going, the second — its easing is `Constant` — the value it has from that frame on.
  The frame before shows the move at the place it has reached, not the end it never shows:

  ```json
  "tint": [
    { "t": 0, "color": [1, 0, 0], "strength": 0 },
    { "t": 2, "color": [1, 0, 0], "strength": 1, "easing": "smooth" },
    { "t": 2, "color": [0, 0, 1], "strength": 1, "easing": "constant" },
    { "t": 3, "color": [0, 0, 1], "strength": 0 }
  ]
  ```

  A third key on the frame, or a second one that is not `constant`, is refused.
- **A `Visible` fade has an easing** (`PlanVisibleKey.Easing`, JSON `easing`): `Linear` (the
  default) or `Smooth`. `Constant` is refused: without a fade the change is a cut already.
- **A colour is mixed as light or as a screen mixes** (`PlanTintKey.Mix`, `PlanColorKey.Mix`, JSON
  `mix`; `PlanColorMix`): `Linear` (the default, and not written) goes straight through the
  plan's linear RGB, `Srgb` straight through sRGB, which is what a browser or a picture editor
  shows between two colours. Half-way from red to green is `[0.5, 0.5, 0]` as light and
  `[0.214, 0.214, 0]` as a screen. The player keys an `Srgb` move frame by frame, exactly, with
  the key's easing. It is the colour's way between two keys; how a tint lies over the target's
  own colour by its `Strength` is a mix of light as before.
- **A ramp over height in the model's own space** (`PlanMaterial.Space`, JSON `space`;
  `PlanRampSpace`): `Scene` (the default) measures `Low` and `High` in the plan's frame, `Model`
  in the model's own coordinates, as its file has them, before its scale, rotation and
  translation. A colour then stays with the rock it is on while the model is stretched (a relief
  that grows) or moved. Only a `HeightRamp` has a space. The draft colours follow it.
- **A camera pose has a clip range of its own** (`PlanCameraKey.ClipStart`, `ClipEnd`, JSON
  `clipStart`, `clipEnd` in `camera.track[n]`). A pose without one has the camera's. Between
  poses the range moves as the camera does, so a flight from far away to a metre from a wall
  keeps its depth exact at both ends (it matters for EEVEE and the draft; Cycles needs no depth
  buffer).
- **Lines** (`ScenePlan.Lines`, JSON `lines`, played by the new module `lines`;
  `ScenePlan.LinesFeature`; `PlanLine`, `PlanLineStrip`). A line is drawn through given points as
  a thin six-sided tube `Width` metres thick, which the player builds: the legs of a survey, a
  route, an outline, without anybody writing a mesh. Its `Strips` are runs of two points or
  more; a strip may give each point a colour, which the tube blends from point to point (either
  every strip of a line has colours or none has). In everything else **a line is a model**: it
  has `Translation`, `Rotation` and `Scale`, it wears the material of `look.materials` it names
  (`Flat` for one colour, `VertexColor` for the colours of its points, a ramp over height), and
  what changes of a model changes of it. Its role is `line` unless it says another. The module
  runs before `look`. A strip takes one line of the JSON.

### Additive API: for whoever writes plans without a `SceneSpec`

- **`ScreenTextPlanner.Plan(OverlaySpec, ScenePlan)`** lays the overlays out for a plan that is
  there already — title, captions, credits, scale bar, north arrow, end card, fades — the way
  those of a spec are laid out; `ScenePlanBuilder.WithTexts(plan, texts)` puts the result into
  the plan and lists the module. The north arrow follows the plan's own camera:
  **`ScreenTextPlanner.Headings(PlanCamera, fps, frames)`** takes poses with an orientation by
  the way they are turned, so a camera that looks straight down keeps the heading the top of its
  picture gives it. `ScreenTextPlanner.Turns(headings, fps)` is public for headings that come
  from elsewhere.
- **`ScenePlanBuilder.Material(MaterialsSpec, bounds, id)`** and
  **`ScenePlanBuilder.Lighting(LightingSpec)`** give the rock and the lights (and the world) that
  `FromSpec` gives a scene, without a scene to lower: the lamp on the camera is `Headlamp`, the
  sun under a sky `Sun`, the three suns `Key`, `Fill` and `Rim`.
- **`ScenePlanBuilder.WithNeededFeatures(plan)`** lists, at its place in the running order, every
  module of the player that a section of the plan needs and its features do not name yet.
- **An end card over the scene** (`EndCardSpec.Backdrop`, `OverScene`; JSON `backdrop`,
  `overScene` under `labels.overlays.endCard`). `Backdrop` is how dark the card is: 1 (the
  default) is the black card, 0 no card at all — the text stands over the last frame on a dark
  box of its own — and anything between dims the frame. `OverScene` brings the card over the
  last seconds of the scene, which goes on under it, instead of holding the last frame: the
  movie is no longer for it. Both are written only when they are not the defaults, so a spec
  without them is the same JSON as before. The script generator draws no end card either way.
- **A bundle's files may be streams** (`RenderBundleContent.Streams`: plan path → what opens the
  content). A mesh that was just made goes into the zip without a file on disk:
  `["walls.ply"] = () => new MemoryStream(PlyWriter.Write(mesh))`. Each is opened once for every
  time the bundle is written. A path is in `Files` or in `Streams`.
- **A bundle's README in Romanian** (`RenderBundleContent.Language`: `"en"`, the default, or
  `"ro"`; `RenderBundleWriter.ReadmeLanguages`). A culture name is taken by its language
  (`ro-RO`); any other language gives the English text. `therion-cli blender … --bundle …
  --readme-language ro` asks for it.
- **A bundle's README with an opening and sections of its maker's**
  (`RenderBundleContent.ReadmeIntro`, `ReadmeSections` of `RenderBundleReadmeSection(Title,
  Text)`). The intro stands in place of the two lines that say the folder needs nothing of ThIDE;
  the sections come after the README's own and before "NOT IN THIS SCENE". Both are written as
  they are, in whatever language they are in.

### Behaviour changes
- **A draft shows more of the changes.** Blender's solid view draws no material, so until now a
  fade was a cut and an opacity or a tint was not shown. Now a model (or a line) in the draft's
  colours **takes its tint** there, exactly, and **shows how much of it is there** by that share
  of its faces: at fade × opacity 0.5 half of them are drawn, the same ones on every frame. A
  modifier does both from the numbers on the object; nothing runs but Blender's own nodes, so a
  saved draft scene plays it too. Of a text or a marker a fade is still a cut and an opacity or
  a tint is not shown, and the player says so: "the draft shows a fade of a text or a marker as
  a cut, and no change of its opacity or tint". A model without colours for the solid view (a
  GLB with its own material) is named in a warning of its own.
- `NotCarried` is as it was: a plan still does not carry the HDRI light rig.
- The README of a bundle that asks for nothing new is the same text as before, byte for byte.

### What the player refuses, by name
- an `energy` of anything but a light; an `offset` of a text or a marker; an `opacity`, a `tint`,
  a `scale` or an `offset` of a light, and a `visible` key of a light with a `fade`;
- a `worldStrength` without a world; two changes of it;
- three keys on one frame, a second key on a frame that is not `constant`, keys out of order, a
  `constant` fade, a `mix` that is not `linear` or `srgb`;
- a pose whose clip range is not a range;
- a line without a positive `width`, without strips, a strip of fewer than two points, colours
  that are not one to a point or that only some strips have.

## 2026-11-24 — timed changes in the plan: things appear, go, fade, take a tint, are stretched

### Breaking changes
- **The plan's `changes` section is defined.** It was a reserved name whose value had to be an
  array and travelled in `ScenePlan.Sections`. `ScenePlan.Changes` holds it now, as a list of
  `PlanChange`; it no longer arrives in `Sections`, and `ScenePlanSerializer.Read` refuses a key
  in it that the schema does not have. Migration: a plan that wrote the empty reserved array
  (`"changes": []`) still reads and is still valid without the module; one that kept data of its
  own under that name moves it to a section of another name.
- **`ScenePlanBuilder.NotCarried` no longer names `labels.events`**: a plan carries a spec's
  visibility events now. Only `lighting.hdriPath` is left. With the builder on `Auto`, a spec
  with events is therefore built by the player and no longer by the script generator, and
  `SceneBuilder.Player` no longer refuses it. Migration: code that looked for `labels.events` in
  `NotCarried` (to warn, or to pick a builder) can drop the check; whoever wants the generator's
  grow-in for such a spec asks for `SceneBuilder.Generator` by name. What differs in the picture
  is under "Behaviour changes".
- **`ScenePlan.PlayerFeatures` has one more entry**, `changes`, between `labels` and `texts`.
  Migration: none for code that looks a module up by name; code that counted on positions in the
  list counts again.

### Additive API
- **Changes** (`ScenePlan.Changes`, JSON `changes`, played by the new player module `changes`;
  `ScenePlan.ChangesFeature`). A `PlanChange` names a `Target` — the one thing with an `Id` (a
  model, a text or a marker of the plan) or every thing with a `Role` (stated in its section, or
  `model`, `label`, `marker`) — and gives each property that changes its keys, in rising order of
  time: at `T` seconds the property has the key's value. Between two keys the value moves as the
  later key's `Easing` says (`PlanEasing`): `Linear` (the default, and not written), `Smooth`
  (3x² − 2x³ of the way at the share x of the time, exactly) or `Constant` (it holds, and jumps at
  the key's time). **Before its first key a property has that key's value and after its last it
  keeps it, so one key is a value for the whole clip**: a tint or an opacity that does not change
  is a change of one key.

  | Property | Keys | What it is |
  |---|---|---|
  | `Visible` | `PlanVisibleKey(T, Value, Fade)` | shown or hidden from `T` on. With a `Fade` the change starts at `T` and takes that many seconds: the target appears little by little, or goes little by little and is hidden when it has gone. Each key says the opposite of the one before; before the first key the target is the other way round |
  | `Opacity` | `PlanNumberKey(T, Value, Easing)` | how much of the target is there, 0 to 1, over what its material lets through by itself (a terrain that is 0.4 solid is at most that) |
  | `Tint` | `PlanTintKey(T, Color, Strength, Easing)` | a colour laid over the target's own colours (a ramp, an image, vertex colours) by `Strength`, before light falls on it: the shading stays |
  | `Scale` | `PlanVectorKey(T, Value, Easing)` | a model's stretch along its own axes, as `PlanModel.Scale`; `(1, 1, 2)` doubles the heights. Models only |
  | `WorldColor` | `PlanColorKey(T, Value, Easing)` | the colour of `look.world`, in a change **without** a target; the world must be of the kind `color` |

  In the JSON a target and each key take one line:
  `{ "target": { "role": "component-label" }, "visible": [ { "t": 0, "value": true, "fade": 3 } ] }`.
- **Nothing here touches the size of a text or a marker.** Showing and hiding is the object's own
  switch; a fade, an opacity and a tint are numbers on the object (custom properties `thide_fade`,
  `thide_opacity`, `thide_tint`, `thide_tint_strength`, keyed) that its material reads, so a
  marker goes on pulsing and keeps the size its distance from the camera gives it while it fades,
  and objects that share a material change one by one. A change that only cuts (no fade) touches
  no material. Every curve is one Blender works out by itself: a scene saved as a `.blend` plays
  the changes with no Python.
- **What the validator refuses, with the path of the field**: a target that is not exactly an id
  or a role, or that names nothing the plan has (checked when every feature of the plan is this
  library's; a module from elsewhere may make things only the player can see); a change with no
  keys; keys that are not in rising order, that share a frame or lie after the clip; values out of
  range; a `Scale` of anything but a model; a `WorldColor` with a target, or under a world that is
  a sky or not there; two changes of the same property of the same thing; a fade, an opacity or a
  tint of a model that has no material. The player refuses the same when it meets them, and
  always checks that a target names something in the scene. Changes need their module in
  `features`, after `look`, `textured_materials` and `labels`; an empty list does not.
- **A spec's visibility events are lowered** by `ScenePlanBuilder.FromSpec`: station labels to the
  role `station-label`, component labels to `component-label`, lead markers to `lead-marker` and
  `lead-text` (a caption goes with its marker, as in the generated scripts). A show event is a
  key that shows from its frame, a hide event one that hides from its frame; of two on one frame
  the later event decides. Events beyond the clip are left out, and a group that would appear
  only after it is hidden throughout. **The events of the overlays are not changes**: the title,
  the scale bar, the north arrow and the depth legend are written in the second stage, so their
  events become the `start`, `end` and `fadeIn` of those texts (a text shown twice is written
  twice, the second as `<id>#2`); a still has the overlays that are there on its frame.
- The schema describes the section (`$defs/easing` is new); a producer that validates against it
  catches a misspelt property as an unknown key.

### Behaviour changes
- The player calls itself **1.3**. The plan stays version 1: a player 1.2 refuses a plan that
  lists `changes` by name ("the plan needs the module "changes", which this player does not
  have"), and an earlier build of this library refuses the section's keys on reading, so neither
  plays a plan with changes as one without.
- **A label fade is a fade.** Where a generated script grows a group in from nothing
  (`VisibilityEvent.FadeSeconds`), the player fades it in at its full size, evenly, over the same
  frames. The frames on which a group is shown and hidden are the same in both builders; hiding
  is at once in both. A second show event for a group that is already shown does nothing in the
  player (the script shrinks the group and grows it again).
- **Not carried: a change of a material's image** (a terrain that swaps or cross-fades its
  picture). There is no such property; a plan that writes one is refused by its name
  (`changes[n].image is not known to player 1.3`). Two terrains with a `visible` track each do
  carry a cut from one to the other.
- A closed shape that is half there shows its far side through its near side, as a material's
  alpha does: half-way through a fade a cave covers three quarters of the pixels it covers when
  solid, not half.
- A height ramp is by height in the scene, so stretching a model moves it through its ramp's
  colours; texts and markers stay where their section puts them.
- **The draft look** (Blender's solid view) draws no material: a fade is a cut there, and an
  opacity or a tint is not shown. The player says so in one warning. Showing, hiding, a model's
  stretch and the world's colour are shown.
- `api.fcurves` of the player asks an action for the curves of the datablock's own slot first.
  From Blender 4.4 a world and its node tree share one action, and the action's own list is that
  of its first slot only. Nothing changes for a datablock with an action of its own.

## 2026-11-20 (later) — a turn for each camera pose, the player's licence in its files and bundles

### Breaking change
- **A bundle's `LICENSE` and `NOTICE` are its own.** `RenderBundleWriter` refuses a file of
  `RenderBundleContent.Files` at either path (in any case), as it refuses `README.txt` or
  `render.py`. Migration: a consumer that put a file of its own there gives it another name or
  puts it in a folder (`docs/LICENSE`).

### Additive API
- **An orientation for each camera pose** (`PlanCameraKey.Orientation`, JSON
  `camera.track[n].orientation`): how the camera is turned, as a unit quaternion `[w, x, y, z]`
  (`PlanQuaternion`) in the plan's frame, X east, Y north, Z up. It turns the camera's own axes
  the way Blender has them: the camera looks along its −Z and the top of its picture is its +Y,
  so `[1, 0, 0, 0]` looks straight down with north at the top. With it the player keys the
  camera's rotation (quaternion mode) and puts no `TRACK_TO` on it: the camera keeps the turn,
  roll included, also straight down or straight up, where aiming at a target loses the heading.
  The target is then only where the camera focuses (depth of field). Without it nothing changes.
- **Either every pose of a track has an orientation or none has.** A camera turned by its poses
  for part of a track and aimed at targets for the rest would jump between the two; the
  validator refuses the mix at the first pose that differs, and so does the player.
- Between two poses Blender interpolates the four numbers as the track's `interpolation` says
  (linear, bezier, constant) and the turn is that quaternion made unit length. q and −q are the
  same turn; the player takes each pose's quaternion as the one of the two nearer the pose before,
  so the camera turns the short way. A producer that wants a particular path between two turns
  writes poses close enough together (one per frame needs no interpolation at all).
- `PlanQuaternion.FromView(forward, up)` is the turn of a camera that looks along `forward` with
  the top of its picture towards `up` (neither of unit length; the part of `up` along the line
  of sight is taken away); `Forward`, `Up` and `Rotate(v)` go the other way; `Identity` is no
  turn.
- `ScenePlanValidator` refuses an orientation that is not finite or whose length is more than
  `ScenePlanValidator.OrientationLengthTolerance` (1e-4) from 1, so six decimals are enough; the
  player turns it to unit length. The schema has `$defs/quaternion` (four numbers from −1 to 1),
  and an orientation takes its place on the pose's one line of JSON, after `lens`.
- **The licence travels with the player.** Every Python file of the player (`render.py`,
  `thide_player/**`, and the start of a single-file script) begins with the notice of the Mozilla
  Public License 2.0, and a single-file script says at its top that the player in it is ThIDE's
  and under that licence. A render bundle carries `LICENSE` (the text of the MPL-2.0, as
  `src/Therion.Blender/LICENSE` has it) and `NOTICE` (what the player is, and that its source is
  in the ThIDE repository, https://github.com/apgeo/ThIDE); the README lists both and says where
  the source is. `RenderBundleWriter.LicenseFileName`, `.NoticeFileName` and `.SourceUrl` name
  them.

### Behaviour changes
- The player calls itself **1.2**. The plan stays version 1: a player 1.1 refuses a pose with an
  `orientation` by name ("camera.track[0].orientation is not known to player 1.1; a newer player
  may be needed"), and an earlier build of this library refuses it on reading, so neither plays
  an oriented track as an aimed one.
- A bundle has two more entries, so its bytes differ from those of an earlier build; the same
  content under the same name still gives the same bytes every time.
- The north arrow's turns that `ScreenTextPlanner` makes come from a `SceneSpec`'s camera, which
  has no orientations; a producer that writes oriented poses and an arrow writes the arrow's
  turns from its own views (looking straight down, the heading of the top of the picture).

## 2026-11-20 — texts over the picture in a second stage, the terrain in plans, two scene builders

### Breaking changes
- **`IBlenderRenderService.RenderAsync` takes `RenderOptions`.** The new overload
  `RenderAsync(spec, source, options, progress, ct)` is the one an implementation provides; the
  four-argument overload is now a default method of the interface that calls it with
  `new RenderOptions()`. Migration: an implementation (a fake in a test, a decorator) adds the
  `RenderOptions options` parameter after `source`; callers of the four-argument overload change
  nothing.
- **The plan's `texts` section is defined, and is an object.** It was a reserved name whose value
  had to be an array. `ScenePlan.Texts` holds it; it no longer arrives in `ScenePlan.Sections`, and
  `ScenePlanSerializer.Read` refuses `"texts": []`. Migration: a plan that wrote the empty reserved
  array leaves the key out; one that wants texts writes the object described below.
- **`RenderPhase.Composing`** stands between `Rendering` and `CollectingOutputs`, so the numbers
  of `CollectingOutputs` and `Done` are one higher. Migration: none for code that names the
  members; anything that stored the numbers must store the names.
- **`ScenePlanBuilder.NotCarried` no longer names `labels.overlays` and `surface`**: a plan
  carries both now. Only `lighting.hdriPath` and `labels.events` are left. Migration: code that
  looked for those two paths to warn about them can drop the check.

### Additive API
- **Texts over the picture** (`ScenePlan.Texts`, JSON `texts`, played by the new player module
  `texts`; `ScenePlan.TextsFeature`). `PlanScreenTexts` holds `Items` and three times for the movie
  as a whole: `Hold` (seconds the last frame of the scene stands still after the scene, for an end
  card), `FromBlack` and `ToBlack`. A `PlanScreenItem` is a `Text`, a `Box` of one colour or an
  `Arrow` that turns about its middle with a letter at its tip; it has an `Id`, an optional `Role`,
  a place `At` (`PlanPoint`: from the left edge and from the bottom edge, 0 to 1), a `Size` or a
  `Width` and `Height` as shares of the picture, `AlignX`/`AlignY`, `Color`, `Opacity`, `Backing`
  (a dark box behind a text), `Wrap`, `Start`/`End` and `FadeIn`/`FadeOut` in seconds, `Turn` (an
  arrow's angles over time, `PlanTurn`), and `Slot`: of the items that name the same slot only the
  one that started last is drawn, which is how one caption replaces another. Later items lie over
  earlier ones. The schema, the validator and the serializer know all of it; an item takes one
  line of JSON and so does each turn of an arrow. `ScenePlan.HasTexts` says whether a plan needs
  the second stage and `ScenePlan.OutputFrameCount` how long its movie is, the hold included.
- **`ScreenTextPlanner`** lowers a spec's overlays to those items, laid out inside a margin of
  5 %: the title at the top (or, with `TitleSeconds`, a title card in the middle that fades out),
  the scale bar bottom left as a round length at the distance the camera looks from (one for each
  still of a still set), the north arrow top right, the depth legend at the right edge, the credits
  bottom right, a corner line top left, captions bottom centre, and the end card over everything.
  `ScreenTextPlanner.Headings(camera, frames)` is the camera's heading at every frame, clockwise
  from north and without jumps; the arrow's turns are those headings, thinned to the frames that
  do not lie on a straight line between their neighbours. A producer without a `SceneSpec` writes
  the same `turn` list itself: one entry per frame, or per camera pose.
- **`OverlaySpec`** gains `Credits`, `Corner`, `Captions` and `CornerCaptions` (`CaptionSpec`:
  text, start, end), `Subtitle` and `TitleSeconds` for the title, `EndCard` (`EndCardSpec`: text,
  subtitle, seconds) and `FadeFromBlack`/`FadeToBlack`. They are written to a spec's JSON only when
  set, so every existing spec keeps its JSON and its `SPEC_HASH`. `OverlaySpec.Any` and
  `.HasFlatOnlyTexts` say whether anything is written over the picture, and whether any of it is
  something only the second stage draws. `SceneSpecValidator` checks the new fields.
- **`Subtitles`**: `Srt(captions)` and `Vtt(captions)` write captions as SubRip and WebVTT;
  `OneAtATime(captions)` is the captions as they are shown (a later caption replaces an earlier
  one it overlaps); `TryParseCaption("2.5-6:Sala Mare", out caption)` reads one from a command
  line.
- **Materials with an image** (`ScenePlan.TexturedMaterials`, JSON `texturedMaterials`, played by
  the new player module `textured_materials`; `ScenePlan.TexturedMaterialsFeature`). A
  `PlanTexturedMaterial` names the `Models` that wear it, its plain `Color`, its `Image` (a `.png`
  or `.jpg` beside the plan, laid on a model by the model's own UV map), `Opacity` and
  `BackOpacity` (how solid it is from the front and from behind) and `Shadow`. `PlanModel.Role`
  (JSON `role`) says what a model is; the player stamps it on the object.
- **`ScenePlanBuilder.FromSpec` carries the terrain surface**: a second model `Terrain` (role
  `terrain`, `ScenePlanBuilder.TerrainId`/`TerrainRole`) from `SourceSpec.SurfacePlyPath`, with a
  material that is see-through from above by the spec's opacity, not there from below and casts no
  shadow, with `SourceSpec.SurfaceImagePath` on it for `SurfaceMode.Textured`. In Blender it is the
  terrain a generated script makes, and renders the same picture.
- **`ScenePlanBuilder.Notices(spec, meta)`**: what a render of the plan shows less of than the
  spec asks for and cannot say itself, in the sentences a generated script prints: station labels
  that were thinned out, a terrain or a surface image the model does not have.
- **`SceneBuilder`** (`Auto`, `Player`, `Generator`), **`RenderOptions`** (`Builder`, `Draft`,
  `KeepFrames`, `ComposeOnly`, `FramesDirectory`) and **`SceneBuilders.Choose(spec, options)`**,
  which says which builder a spec gets, what the player does not carry when that decided, what the
  chosen builder does not show, and why a combination is refused.
  `SceneBuilders.NotDrawnByTheGenerator(spec)` lists the overlays a generated script leaves out.
- **`RenderResult`** gains `Builder`, `NotCarried`, `LeftOut` and `FramesDirectory`;
  **`RenderFailureKind.BuilderRefused`** is a render that cannot be made with the builder asked
  for.
- **`PlayerWriter.FilesOf(plan)`**: every file a plan names, meshes and images. `WriteJob` checks
  that all of them are in the folder, `SingleFile` carries any of them, and a render bundle must
  hold them all.
- **`RenderBundleContent.Notices`** and **`.Captions`**; `RenderBundleWriter.FromModel` converts
  the terrain when the spec draws it and fills both. A bundle with captions holds them as
  `<output folder>/<base name>.srt` and `.vtt` (`RenderBundleWriter.SubtitlePath`).

### Behaviour changes
- **A plan with texts is rendered in two stages**, each in a Blender of its own. The first
  renders the scene as PNG frames (`frame_0001.png` onwards) into a frames folder, with a note of
  what they were rendered from (`frames.json`: a digest of every part of the plan that shapes the
  frames and of every file the modules read). The second lays the frames and the texts into the
  video editor of an empty scene and writes the plan's output. Run as before
  (`blender -b --factory-startup --python render.py`), the player does both, starting the second
  Blender itself; the frames are removed when the output is written.
- **The player's command line** has `--stage frames|compose|all` (the first stage only; the
  second only, from the frames that are there; everything, the default), `--frames DIR` (the
  frames folder; default `frames` beside the plan) and `--keep-frames` (keep them, and render in
  two stages whatever the plan says).
- **A first stage that was stopped goes on**: frames that are there are kept when their note says
  they belong to this scene, a frame that was left empty or cut short is rendered again
  (`THIDE:resumed=<kept>/<all>`), and frames of another scene are replaced. The second stage
  refuses frames whose note names another scene, and takes frames without a note as they are.
  A change to the texts or to the output's name leaves the frames valid.
- **Player modules can take part in the second stage**: `two_stage(section)`, `compose(ctx,
  section)`, `finish(ctx, section)` and `IN_FRAMES = False`, beside `build` and `render` (see
  `thide_player/api.py`). `ctx.frames` is the frames folder, or `None` in a render of one stage.
  The core passes the script it was started with to `core.main(..., script=...)`.
- **The second stage's progress** is reported with `THIDE:phase=compose`, and
  `RenderProgressParser` gives the frames that follow the phase `RenderPhase.Composing`.
- **A saved scene packs its images** (`blend_file`), so a `.blend` with a terrain's image can be
  moved alone, as one with meshes only already could.
- **A draft leaves a see-through terrain out**, with a warning: Blender's solid view cannot draw
  one model see-through. A terrain with opacity 1 is drawn in its plain colour.
- **The bundle's run scripts** pass `--stage` to the player and take `--compose`
  (`sh render.sh --compose`, `render.cmd --compose`) to write the output again from the frames.
  The README describes the two stages when the plan has texts, counts the held frames in the
  movie's length, and no longer lists overlays or the terrain among what a plan leaves out.
- The player still calls itself 1.1 and the plan is still version 1: both additions are a new
  module name in `features`, which an earlier player refuses by name.
- The script generator is unchanged: it draws the title, the scale bar, the north arrow and the
  depth legend as objects in the scene and none of the newer texts, and its goldens are the same
  bytes.

## 2026-11-18 (follow-ups) — terrain, player plans, lead captions, bundles

### Additive API
- `ScriptAssets.SurfacePlyBytes` and `.SurfaceImageBytes`: a script that carries its mesh
  (`SourceSpec.SelfContained` and `.EmbedMesh`) now carries the terrain and its image too, base64
  like the mesh, and draws them without their files. The generator refuses such a spec without the
  bytes it needs (`ArgumentException`, as for `PlyBytes`); a terrain the model does not have needs
  none. `BlenderRenderService` reads them from the conversion. A script that is self-contained
  without its mesh still reads the terrain from its files.
- **A text can follow a marker**: `PlanText.Follows` (JSON `"follows"`, the id of one of
  `labels.markers.items`) makes the text the marker's child in the scene, so that it moves and grows
  with the ball; its `at` and `size` are where it stands and how large it is while the ball has
  the radius it is built with. The validator refuses an id that is no marker's.

### Breaking change
- **A bundle's zip holds one folder**, named after the zip (`RenderBundleWriter.FolderName`: the
  file name without its extension, with what some file system refuses replaced by `_`), so that
  unpacking it makes that one folder. `WriteZip(content, stream)` became
  `WriteZip(content, stream, name)`: a stream has no file name to take it from. Both overloads
  return the entries' paths with the folder in front. The same content under the same name still
  gives the same bytes; another name gives another folder. Migration: whoever unpacks a bundle's
  zip finds the plan in `<name>/scene-plan.json`, not at the top; a caller of the stream overload
  passes the name it wants. `WriteFolder` is unchanged: the folder is the bundle.

### Behaviour changes
- **A lead's caption follows a marker sized by its distance from the camera**
  (`LeadMarkerSizing.CameraDistance`), in the generated scripts and in plans from `FromSpec`
  alike: it grows and shrinks with its ball, the pulse included, and stays two of the ball's radii
  above its middle, where it used to keep its size two cave-sized radii above a shrinking ball. The
  caption is the marker's child, with the ball's place as its parent inverse. Captions of markers
  sized any other way are unchanged.
- `SurfaceSpec.Opacity` defaults to 0.4 (was 0.5): the cave under the terrain shows better. It is
  still set per spec. `SPEC_HASH` of every generated script changes with it.
- **The player refuses a key it does not know** in the core's sections (`frame`, `timeline`) and
  in the section of every module that lists its keys, which all of the player's own do (`KEYS`
  beside `SECTION`; see `thide_player/api.py`). The plan fails before anything is built, naming the
  key, its section and the player: `models[1].tint is not known to player 1.1; a newer player may
  be needed`. A plan that sets a field a newer player added is no longer played as if the field
  were not there. Sections of other modules are still the business of their modules; a module
  without `KEYS` checks its own section, or not at all.
- The player's markers glow in a material named `LeadMarker`, as in the generated scripts, where
  it was `LabelMarker`.
- **`ScenePlanSerializer.Read` refuses an unknown key inside a known section** the same way
  (`ScenePlanFormatException`: `models[1].tint is not known to this build (scene plan version 1);
  a newer build may be needed.`), where it ignored it. The keys it knows are the schema's; unknown
  sections are still kept in `ScenePlan.Sections`. The schema says that every object but the plan
  itself is closed, which it already was.

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
