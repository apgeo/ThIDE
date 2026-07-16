# Blender animation renders

The **Blender Animation** panel turns the 3D model your project already produces into a
presentation you can share: a fly-around video, a helical descent, a flythrough of the main
passage, a set of documentation stills, or a top-down map pass. ThIDE converts the model to a
Blender-ready mesh, writes a complete Blender Python script tuned to your choices, and (if you
want) runs Blender in the background to produce the finished file — all without you touching
Blender's interface.

> **You need Blender installed** — version **4.2 LTS or newer**. ThIDE finds it automatically
> (Windows *Program Files*, the `PATH`, snap/flatpak on Linux, `/Applications` on macOS); if it
> can't, point it at the executable in **Preferences ▸ External tools**. ThIDE does not bundle
> Blender and never modifies your Blender install.

---

## Opening the panel

**Tools ▸ Blender Animation…** opens the panel in the central area. Everything you set there
describes one *render job*; nothing runs until you press **Render**.

## The workflow, step by step

1. **Pick a source.** By default the panel lists the `.lox`/`.3d` models discovered in your
   project's last build (newest first) — choose one from the dropdown. Or tick **Use an external
   file** and give a path to any `.lox` or `.3d`. If the dropdown is empty, build your project
   first (**Build ▸ Compile**), or use an external file.

   > A `.lox` (Therion *loch*) model carries **walls**, so it renders as solid cave passage. A
   > `.3d` (Survex) model is centre-line only, so ThIDE synthesizes tubes from the LRUD data.

2. **Choose a preset** (recommended). The preset strip offers a gallery:
   - **Orbit showcase** — a full turntable with studio lighting.
   - **Helix descent** — a spiral from the top of the cave to the bottom.
   - **Full flythrough** — a head-torch fly-through of the longest passage.
   - **Map reveal** — a near-top-down pass with the survey pieces labelled.
   - **Documentation stills** — framed top / front / side / isometric images.

   A preset fills in the camera, materials, lighting and output all at once. You can then tweak
   any setting below it.

3. **Adjust settings** as needed:
   - **Camera** — the motion template.
   - **Engine** — *Cycles* (the default, works everywhere) or *EEVEE* (faster, but needs a
     display; on a headless Linux server use Cycles). **Device** picks the GPU backend for
     Cycles (Auto tries them all and falls back to CPU).
   - **Animation** — frames per second and duration; the panel shows the resulting **frame
     count**.
   - **Output** — a video (MP4/MKV/WebM), a numbered PNG sequence, or a single still; plus
     resolution and file name.
   - **Labels** — turn on station labels and lead markers.

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

## Requirements & tips

- **Blender ≥ 4.2** — older versions are rejected with a clear message.
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
| *"Blender was not found"* | Install Blender 4.2+, or set its path in **Preferences ▸ External tools**. |
| *"…too old…"* | Update Blender to 4.2 or newer. |
| The source dropdown is empty | Build the project first, or tick **Use an external file**. |
| The render failed | Open the **job log** from the notification or the panel — it has Blender's full output. |
| EEVEE render fails on a server | Switch the engine to **Cycles**. |
