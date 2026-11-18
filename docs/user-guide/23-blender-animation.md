# 23. Blender animation renders

> [← Back to the User Guide home](README.md) · [ThIDE project home](../../README.md)

> **Experimental — work in progress.** This module is young and still changing. Expect rough
> edges, and settings or behaviour that move between releases.

The **Blender Animation** panel turns the 3D model your project already produces into a
presentation you can share: a fly-around video, a helical descent, a flythrough of the main
passage, a set of documentation stills, or a top-down map pass. ThIDE converts the model to a
Blender-ready mesh, writes a complete Blender Python script tuned to your choices, and (if you
want) runs Blender in the background to produce the finished file — all without you touching
Blender's interface.

> **You need Blender installed** — version **4.2 LTS or newer**. ThIDE finds it automatically
> (Windows *Program Files*, the `PATH`, snap/flatpak on Linux, `/Applications` on macOS); if it
> can't, point it at the executable in **Settings → External Tools**. ThIDE does not bundle
> Blender and never modifies your Blender install.

---

## Opening the panel

**Tools → Blender Animation…** opens the panel in the central area. Everything you set there
describes one *render job*; nothing runs until you press **Render**.

## The workflow, step by step

1. **Pick a source.** By default the panel lists the `.lox`/`.3d` models discovered in your
   project's last compilation (newest first) — choose one from the dropdown. Or tick **Use an external
   file** and give a path to any `.lox` or `.3d`. If the dropdown is empty, compile your project
   first (**Compile → Compile**), or use an external file.

   > A `.lox` (Therion *loch*) model carries **walls**, so it renders as solid cave passage. A
   > `.3d` (Survex) model is centerline only, so ThIDE synthesizes tubes from the LRUD data.

2. **Choose a preset** (recommended). The preset strip offers a gallery:
   - **Orbit showcase** — a full turntable with studio lighting.
   - **Helix descent** — a spiral from the top of the cave to the bottom.
   - **Full flythrough** — a head-torch fly-through of the longest passage.
   - **Documentation stills** — framed top / front / side / isometric images.

   A preset fills in the camera, materials, lighting and output all at once. You can then tweak
   any setting below it.

3. **Adjust settings** as needed:
   - **Camera** — the motion template.
   - **Terrain** — the terrain surface a *.lox* model can carry, drawn above the cave: *Off* (the
     default), *Solid* (one colour) or *Textured* (the surface image draped over it; one colour when
     the model has no image). It is see-through from above (40 % opaque by default) and invisible
     from below, casts no shadow, and the camera still frames the cave. A very large grid is
     thinned to at most 512 × 512 points.
   - **Engine** — *Cycles* (the default, works everywhere) or *EEVEE* (faster, but needs a
     display; on a headless Linux server use Cycles). **Device** picks the GPU backend for
     Cycles (Auto tries them all and falls back to CPU).
   - **Animation** — frames per second and duration; the panel shows the resulting **frame
     count**.
   - **Output** — a video (MP4/MKV/WebM), a numbered PNG sequence, or a single still; plus
     resolution and file name.
   - **Labels** — turn on station labels and lead markers. *Lead marker size* chooses how big a
     marker is: *Auto* sizes it from its passage in a flythrough and from the whole cave otherwise;
     *Cave*, *Passage*, *CameraDistance* (about the same size in the picture at any distance) and
     *HiddenInFlythrough* fix one way. *Labels above* chooses the roof a station label is lifted
     above: the surveyed height first, the walls first, the walls only, or none (on the station).
   - **Render folder** — where the video, frames or still are saved; by default *Documents/ThIDE/Renders*
     (your home folder's *ThIDE/Renders* where there is no Documents folder). Leave it empty to keep
     them in the temporary job folder, which the operating system may clean up.

   If anything is wrong (an odd video resolution, zero frames, …) it appears under **Fix before
   rendering** and the **Render** button stays disabled with the reason.

4. **Render.** Progress shows the phase and, while Blender renders, the frame count (or a spinner
   for phases without a percentage). Long renders are normal; **Cancel** stops Blender cleanly.
   When it finishes you get a notification, the output paths appear, and **Open folder** / **Open
   log** reveal them.

## Other ways to run it

- **Export script…** — instead of rendering, ThIDE writes the `render.py` script plus its
  `model.ply` and `scene-meta.json` (or a single self-contained `.py`) to a folder you pick, so
  you can run it yourself later with `blender -b --python render.py`, or archive/share it.
- **Open in Blender…** — opens the scene in Blender's *interactive* window so you can inspect or
  tweak it by hand before rendering.
- **Save preset** — store the current settings as your own preset; they appear in the strip
  alongside the built-ins and persist between sessions.

## From the command line

The same engine is available headless through the CLI, for scripting or CI:

```
therion-cli blender <model.lox|.3d> [--preset "Orbit showcase" | --spec spec.json] \
                    [--out <dir>] [--export <dir>] [--blender <path>]
```

`--export` writes the script and assets without running Blender; otherwise it renders. Produced
file paths are printed to standard output; the exit code is `0` on success, `1` on a render
failure, `2` on a usage or spec error.

### A render bundle, to render on another machine

`--bundle` writes everything Blender needs into one zip (or into a folder, when the path does not
end in `.zip`), for rendering on any computer with Blender 4.2 or newer and without ThIDE:

```
therion-cli blender <model.lox|.3d> [--preset "Full flythrough" | --spec spec.json] \
                    --bundle cave-bundle.zip [--draft] [--render] [--out <dir>] [--blender <path>]
```

The bundle holds the scene plan (`scene-plan.json`: the camera move, the look and the output, as
plain JSON), the cave's mesh (`model.ply`), the player that builds the scene in Blender
(`render.py` and `thide_player/`), the run scripts `render.sh` (Linux, macOS) and `render.cmd`
(Windows), and a `README.txt` that says how to render it. Whoever receives it unpacks it into a
folder of its own and runs `sh render.sh` or `render.cmd`; the result lands in the bundle's `out`
folder. The scripts find Blender on the PATH (and in its usual install folder on macOS and
Windows); the `BLENDER` environment variable names another one, for example
`BLENDER=/opt/blender-4.5/blender sh render.sh`.

- `--draft` adds a second plan, `scene-plan.draft.json`, with a fast look: Blender's solid view at
  half the picture size, a fraction of a second per frame. `sh render.sh --draft` (or
  `render.cmd --draft`) renders it as `<name>-draft`, to check the camera move before the long
  render.
- `--render` also renders the bundle here, with the Blender ThIDE finds (or `--blender`): the zip is
  unpacked into a temporary folder and what it renders is moved next to the zip, or into `--out`.
  With `--draft` it renders the draft.
- The same model and settings always give the same zip, byte for byte.
- A scene plan cannot yet say everything the panel can: overlays (title, scale bar, north arrow,
  depth legend), the timed showing and hiding of labels, the HDRI light rig and the terrain surface
  are left out. The command warns about each one it leaves out, and the bundle's `README.txt` lists
  them.

## Requirements & tips

- **Blender 4.2 or newer** — older versions are rejected with a clear message.
- **Cycles is the safe default.** EEVEE is faster but needs a graphics context, so it can fail on
  a display-less Linux box — the panel and CLI both fall back gracefully and tell you the device
  actually used.
- **Big caves** render fine but slowly; start with a low resolution and few samples to preview
  the framing, then raise them for the final pass.
- **Disk space** — frame sequences keep every PNG and can be large; ThIDE estimates the size and
  refuses up front if the drive is nearly full.

## Troubleshooting

| Symptom | What to do |
|---|---|
| *"Blender was not found"* | Install Blender 4.2+, or set its path in **Settings → External Tools**. |
| *"…too old…"* | Update Blender to 4.2 or newer. |
| The source dropdown is empty | Compile the project first (**Compile → Compile**), or tick **Use an external file**. |
| The render failed | Open the **job log** from the notification or the panel — it has Blender's full output. |
| EEVEE render fails on a server | Switch the engine to **Cycles**. |
