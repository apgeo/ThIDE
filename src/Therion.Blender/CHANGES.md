# Therion.Blender — changes for consumers

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
