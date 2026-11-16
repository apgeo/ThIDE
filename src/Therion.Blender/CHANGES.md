# Therion.Blender — changes for consumers

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
