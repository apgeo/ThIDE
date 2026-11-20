# Therion.Blender.Execution — changes for consumers

## 2026-11-20 — the service builds scenes with the player; two stages; drafts

### Behaviour changes
- **`BlenderRenderService.RenderAsync` builds the scene with the player** whenever the player
  carries the whole spec (`SceneBuilders.Choose`), and with the script generator otherwise or when
  it is asked for (`RenderOptions.Builder`). On the player's path the job folder holds
  `scene-plan.json`, `render.py` and `thide_player/` instead of a generated `render.py`; all of it
  is scratch and is removed with the rest. The result says which builder built the scene
  (`RenderResult.Builder`), what the player does not carry when that is why the script generator
  ran (`NotCarried`), and what the render does not show (`LeftOut`). A builder that cannot build
  the scene is a failed result of kind `BuilderRefused`, returned before Blender is looked for.
  `ExportScriptAsync` is unchanged: an exported script is a generated one.
- **A scene with texts, or one whose frames are asked for, runs as two Blender processes**: the
  frames (log `job-frames.log`), then the output from them (`job.log`). The result is the second
  stage's, with the first's device and the time and warnings of both. Captions are written as
  `<base name>.srt` and `.vtt` beside a movie or a frame sequence and listed among the outputs.
- **Frames**: without `RenderOptions.FramesDirectory` they live in the job folder and go with it,
  whatever the outcome. With it they are the caller's: a render that fails leaves them there
  (`RenderResult.FramesDirectory`) and the next render of the same scene goes on from them;
  `ComposeOnly` writes the output again from them and never removes them. `KeepFrames` keeps them
  in `<output folder>/<base name>-frames` when no folder is named.
- **The notices of a generated script are raised by the service on the player's path**
  (`ScenePlanBuilder.Notices`): station labels thinned out, a terrain or an image the model does
  not have. They are in `RenderResult.Warnings`, as before.
- **`RenderOptions.Draft`** renders the player's quick draft (`ScenePlanBuilder.Draft`) whatever
  builder is named, under the base name with `-draft` added.
- **`OutputCollector` takes only a render's own files**: between the base name and the extension
  it accepts digits, `_` and `-` (a frame number, a frame range), so `<base name>-draft.mp4` beside
  a render is no longer listed as that render's output.
- **`PruneJobFolders`** also removes folders that hold the player's package and a `frames` folder
  beside the scratch files: what a render that was killed leaves.

### Additive API
- `RenderJob.ExtraBytes`: bytes a later stage will write, counted by the disk preflight of this
  one, so a render in two stages does not find the disk full after its long first stage.
  `RenderJob.LogFileName` (default `job.log`).
- `PlanOutputs`: `SpecOf(plan, directory)` and `FramesOf(plan, framesDirectory)` are a plan's
  output and its first stage's frames as the runner collects them; `StageFrameCount(plan)` and
  `FrameBytes(plan)`.
- `RenderProgressParser.ResumedFrames`: the frames that were there when a first stage went on
  where an earlier one had stopped.

## 2026-11-18 (render bundle) — arguments for the script

### Additive API
- `RenderJob.ScriptArguments` (default empty): passed to the script after `--` on Blender's command
  line. A player job folder or an unpacked render bundle plays another plan with
  `["--plan", "<folder>/scene-plan.draft.json"]`.

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
