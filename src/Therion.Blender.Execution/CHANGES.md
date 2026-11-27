# Therion.Blender.Execution — changes for consumers

## 2026-11-27 — the disk preflight scales with resolution

### Breaking changes
- None. `DiskPreflight.EstimateBytes` keeps its signature.

### Behaviour
- A PNG frame (frame sequence or still) is now estimated at about 2 MB at 1080p, scaled by pixel
  count, never under 64 KB a frame, plus a fixed 32 MB margin. It was about 5 MB a frame at 1080p;
  a measured frame takes 1.77 MB, and a sample render was refused for it. The 1.5 times safety
  factor and the video estimate are unchanged. New public constants: `FrameBytesAt1080p`,
  `MinFrameBytes`, `FixedMarginBytes`.

## 2026-11-27 — fixes from a Windows machine with a GPU

### Breaking changes
- None. `IBlenderProcess` has one more member, `WaitForExitAsync(timeout)`, with a default body
  that returns at once: an implementation outside this assembly needs no change.

### Behaviour
- A render that is cancelled returns only once the killed Blender is gone (at most 30 s). Windows
  keeps what a dying process holds locked, its working folder included, so a worker job that went
  on with its frames right after a cancel could find its bundle folder in use.
- A Blender that crashes after it found no OpenGL to draw with (`No OpenGL vendor detected`: a draft
  or EEVEE started outside a signed-in desktop session on Windows, such as a service or an SSH
  session) is reported as a crash that says so and says where to render instead.
  `RenderProgressParser.NoOpenGl` tells it.

## 2026-11-27 — the render worker takes jobs from a server

### Breaking changes
- None. `RenderWorker` keeps its API; its loop moved into an internal `BundleJobRunner` that the
  server mode shares, with every check of a bundle.

### Additive API
- **`RenderNodeWorker`** (`RunAsync(once, ct)` → `RenderNodeRun(Jobs, Unauthorized)`,
  `JobFolder(id)`), with **`RenderNodeOptions`** (`Server`, `Token`, `NodeName`, `Scratch`,
  `Limits`, `MaxTime`, `ModuleFolders`, `Poll`, `FirstBackoff`, `MaxBackoff`, `DownloadTries`,
  `WorkerVersion`) and `RenderNodeUnauthorizedException`: claims a job, downloads its bundle
  (streamed, size and SHA-256 checked before it is read), renders the plan the job names (full or
  draft) in the container it names, sends heartbeats with the progress that renew the lease, uploads
  only the movie or stills, subtitles, log and status, and completes, or fails with a reason. A
  cancel at a heartbeat or a lost lease kills Blender's process tree; a job claimed again on the
  same node goes on from its frames; network failures and 5xx are retried with a growing wait; 401
  or 403 stops the worker. The token goes only to the server's own origin and into no log.
- **`RenderNodeToken.TryRead`**: the token of a token file, refused when the file is a link or
  others may read or write it (Unix), or the token is empty or holds a space.

### therion-cli
- `render-worker --server URL --token-file F [--node-name N] [--scratch DIR] [--once]
  [--poll-seconds S]`, with the folder worker's `--blender`, `--modules`, `--max-hours`,
  `--max-triangles` and `--max-bundle-gb`. Exit code 3 when the server refuses the token, 2 for a
  usage error (an address that is not HTTPS or this machine's, a token file that is missing or
  readable by others).

## 2026-11-27 — composite presets, estimates of a film

### Additive API
- `BlenderRenderService.RenderStoryboardAsync` and `EstimateStoryboardAsync` expand a storyboard's
  symbolic routes (`StoryboardExpander`) from the geometry of their conversion, before lowering it;
  what the expansion did is in the result's warnings.
- **`IRenderEstimator`** (`EstimateStoryboardAsync`), implemented by `BlenderRenderService` and by
  `QueuedRenderService` (which asks the service it wraps, without queueing).

## 2026-11-27 — large caves, frame ranges, a queue of renders and the render worker

### Additive API
- **Walls in parts.** `RenderOptions.Parts` (`WallPartsMode`: `Auto` for a large cave, `On`,
  `Off`) is passed to the conversion; the plans then show the parts (see Therion.Blender).
  `BlenderRenderService.PartsOf(mode)` is what the conversion is asked for. Job folders delete
  `walls.NNN.ply` with the other scratch.
- **Estimate.** `RenderEstimate` (`Frames`, `SecondsPerFrame`, `Measured`, `Seconds`, `DiskBytes`,
  `MemoryBytes`, `Triangles`, `TextObjects`, `TooManyTriangles` above `WarnTriangles` = 20 M,
  `Describe()`, `Of(scenes, outputFrames, trianglesOf, secondsPerFrame)`, `AssumedSecondsPerFrame`,
  `PlyFaces`). `BlenderRenderService.EstimateAsync` and `EstimateStoryboardAsync` convert and plan
  in a job folder that is removed after, and need no Blender. A render reports its estimate as a
  `GeneratingScript` tick (`Estimate: …`) and warns above 20 M triangles.
- **Time left.** Rendering ticks carry `SecondsPerFrame` and `SecondsLeft` (`FrameClock`), over the
  whole film for a storyboard, and `Segment` / `SegmentCount` for a film's shots.
- **Frame ranges.** `RenderOptions.FrameRange` renders those frames of one scene's first stage into
  `FramesDirectory` (required) and writes no output; `ComposeOnly` writes it after. Refused with
  `KeepFrames` or `ComposeOnly`, by the script generator (`BuilderRefused`), and for a storyboard
  (`ArgumentException`: render one shot).
- **Queue.** `RenderQueue` (FIFO, one job running; `RunAsync(request, work, progress, ct, id)`,
  `Cancel(id)`, `Jobs`, `Running`, `Ahead(id)`, `Changed`, `JobEnded`; a waiting job hears
  `RenderPhase.Queued`). With a state file it keeps its jobs (`RenderJobFile`), and the jobs a
  restart cut short are `Interrupted`; `Resume(replay)` starts them again in their order.
  `QueuedRenderService` is an `IBlenderRenderService` whose renders wait in a queue, each with a
  frames folder of its own under the queue's root (removed when the job ends), and
  `ResumeInterrupted()` replays the interrupted ones whose model is still there.
- **Render worker.** `RenderWorker` (`RenderWorkerOptions`: `Inbox`, `Outbox`, `Scratch`, `Limits`,
  `MaxTime`, `ModuleFolders`, `Poll`; `Pending()`, `RunAsync(once, ct)`, `ProcessAsync(zip, ct)`,
  `JobId`) renders the bundles of an inbox one at a time with this assembly's player: status in
  `<job>.status.json`, the movie or stills, the subtitles and `<job>.log` in the outbox under the
  job's name, `<job>.cancel` stops a job (zip and frames kept), a time-out or a failure in Blender
  keeps the frames, a refusal (`failureKind: refused`) removes the job.

### Behaviour changes
- A render's warnings may now include the triangle warning; its progress the estimate tick.

## 2026-11-26 — `RenderStoryboardAsync`: a film of several shots

### Additive API
- **`BlenderRenderService.RenderStoryboardAsync(storyboard, source, options, …)`** implements
  `IBlenderRenderService.RenderStoryboardAsync` (new, with a default that throws
  `NotSupportedException`, so another implementation of the interface still compiles). It converts
  the model once, lowers the storyboard (see Therion.Blender's changes), renders every shot's
  frames in a Blender of its own into `<frames folder>/s01` onwards, then writes the film from
  them in one more.
- `RenderOptions.Draft` drafts every shot; `KeepFrames` and `FramesDirectory` are those of a
  render in two stages, with the frames folder holding one folder for every shot; `ComposeOnly`
  writes the film again from them. A shot whose frames are there is passed over by the player, so
  the same call again goes on where a stopped one was, and renders again only a shot that changed.
  `Builder = Generator` is refused (`BuilderRefused`).
- Progress: each shot's ticks say `Shot 2 of 4 (<name>): …` and their fraction is that of all the
  shots' frames together; the second stage's are `Composing`. A failure names the shot. Each
  shot's first stage has a log of its own (`job-frames.s01.log`), which `PruneJobFolders` takes
  for scratch as it does `job-frames.log`.
- The subtitles of the film (its captions and its shots', in its time) are written beside it.
- **`PlanOutputs.SpecOf(StoryboardPlan, directory)`** and **`FrameBytes(StoryboardPlan)`**.

### Behaviour changes
- None for `RenderAsync` and the exports.

## 2026-11-25 — `ExportAsync`: the player's export

### Additive API
- **`BlenderRenderService.ExportAsync(spec, source, outputDir, ExportOptions, …)`** implements
  `IBlenderRenderService.ExportAsync` (see Therion.Blender's changes): the player's folder as a
  render bundle holds it, one script (`ExportOptions.SingleFile`, written as
  `BlenderRenderService.ExportScriptName`), the job folder for Blender's window
  (`ExportOptions.Interactive`), or the generated script when `ExportOptions.Builder` is
  `Generator`. It needs no Blender and starts none.
- The model is converted in a scratch folder under the job root, which is removed when the
  export is written: the export's folder gets nothing but the export. An export to look at is
  converted in place, as a render's job folder is.
- A spec that cannot be planned throws `ArgumentException`; a model that cannot be read throws
  what the conversion throws, as `ExportScriptAsync` does.

### Behaviour changes
- None for `RenderAsync` and `ExportScriptAsync`. The player's part of a render (convert, plan,
  write the job folder) is now one method that the export to look at shares, so the two cannot
  drift apart.

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
