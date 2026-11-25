# 23. Blender animation renders

> [← Back to the User Guide home](README.md) · [ThIDE project home](../../README.md)

> **Experimental — work in progress.** This module is young and still changing. Expect rough
> edges, and settings or behaviour that move between releases.

The **Blender Animation** panel turns the 3D model your project already produces into a
presentation you can share: a fly-around video, a helical descent, a flythrough of the main
passage, a set of documentation stills, or a top-down map pass. ThIDE converts the model to a
Blender-ready mesh, describes the scene for Blender according to your choices, and (if you
want) runs Blender in the background to produce the finished file — all without you touching
Blender's interface.

> **You need Blender installed** — version **4.2 LTS or newer**. ThIDE finds it automatically
> (Windows *Program Files*, the `PATH`, snap/flatpak on Linux, `/Applications` on macOS); if it
> can't, point it at the executable in **Settings → External Tools**. ThIDE does not bundle
> Blender and never modifies your Blender install.

---

## Opening the panel

**Tools → Blender Animation…** opens the panel in the central area. Everything you set there
describes one *render job*; nothing runs until you press **Render** or **Preview**.

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
     the model has no image). It is see-through from above and invisible from below, casts no
     shadow, and the camera still frames the cave. **Terrain opacity**, beside it, sets how much
     of the cave shows through from above: 0 % hides the terrain, 100 % hides the cave under it;
     the default is 40 %. A very large grid is thinned to at most 512 × 512 points.
   - **Engine** — *Cycles* (the default, works everywhere) or *EEVEE* (faster, but needs a
     display; on a headless Linux server use Cycles). **Device** picks the GPU backend for
     Cycles (Auto tries them all and falls back to CPU).
   - **Animation** — frames per second and duration; the panel shows the resulting **frame
     count**.
   - **Output** — a video (MP4/MKV/WebM), a numbered PNG sequence, or a single still; plus
     resolution and file name.
   - **Labels** — turn on station labels and lead markers. *Lead marker size* chooses how big a
     marker is: *Auto* sizes it from its passage in a flythrough and from the whole cave otherwise;
     *Cave*, *Passage*, *CameraDistance* (about the same size in the picture at any distance, its
     caption with it) and *HiddenInFlythrough* fix one way. *Labels above* chooses the roof a station label is lifted
     above: the surveyed height first, the walls first, the walls only, or none (on the station).
     **Credits line** is a small line that stays in the bottom right corner of the picture (who
     surveyed, who drew, a licence), and **End card** a text on a black picture that the movie
     ends on, for the seconds beside it. More texts are described under
     [Texts over the picture](#texts-over-the-picture).
   - **Scene builder (advanced)** — what builds the scene in Blender; leave it on *Auto*. See
     [Two scene builders](#two-scene-builders).
   - **Render folder** — where the video, frames or still are saved; by default *Documents/ThIDE/Renders*
     (your home folder's *ThIDE/Renders* where there is no Documents folder). Leave it empty to keep
     them in the temporary job folder, which the operating system may clean up.

   If anything is wrong (an odd video resolution, zero frames, …) it appears under **Fix before
   rendering** and the **Render** button stays disabled with the reason.

4. **Preview** (optional, and worth it). The **Preview** button renders a quick draft of the
   same scene: half the size, in Blender's solid view (no lights, no shadows), a fraction of a
   second per frame where the real render may take a minute. Use it to check the camera move,
   the framing and the texts before the long render. The draft is saved beside the render with
   `-draft` added to its name. A terrain that is see-through is left out of a draft, which says so.

5. **Render.** Progress shows the phase and, while Blender renders, the frame count (or a spinner
   for phases without a percentage). Long renders are normal; **Cancel** stops Blender cleanly.
   When it finishes you get a notification, the output paths appear, and **Open folder** / **Open
   log** reveal them. A movie with texts over the picture is made in two stages, and the panel
   shows the second as *Composing the movie*.

## Texts over the picture

A render can carry texts: a **title**, a **scale bar**, a **north arrow**, a **depth legend**, a
**credits line**, **captions** and an **end card**. They are written over the finished frames in
a second stage, in two dimensions, so nothing in the cave can hide them: a title stays readable in
the narrowest passage of a flythrough.

| Text | Where it is | What it does |
|---|---|---|
| Title | top, centre | stays throughout; with a time it is a *title card* in the middle of the picture that fades out. A subtitle goes under it |
| Scale bar | bottom left | a round length (50 m, 2 km) at the distance of what the camera looks at; a set of stills gets one for each still |
| North arrow | top right | turns with the camera, its **N** at its tip |
| Depth legend | right edge | the colours of the depth tint, the top of the cave first |
| Credits line | bottom right | small, stays throughout |
| Corner line | top left | stays throughout; timed corner texts appear under it |
| Captions | bottom, centre | each for its time, on a dark backing, one at a time: a caption that starts while another is showing replaces it |
| End card | the whole picture | a text on black, for its seconds *after* the last frame of the scene: the movie is that much longer. It can let that frame show through, or come over the end of the scene instead (below) |
| Fades | the whole picture | the movie can come out of black at its start and go to black at its end |

The letters are Blender's own font (Romanian diacritics included), white with a dark outline, and
sized by the picture's height, so a movie reads the same at any resolution. Everything keeps a
margin of 5 % from the picture's edges.

The panel has fields for the credits line and the end card. The other texts are set in a render
spec (the JSON a preset holds, under `labels.overlays`), on the command line (`--title`,
`--credits`, `--end-card`, `--caption`) or by an assistant through the MCP tools.

**An end card over the scene.** In a render spec the end card has two more settings, under
`labels.overlays.endCard`. `backdrop` says how dark the card is: 1 (the default) is the black card,
0 leaves the last frame as it is and puts the text on a dark box of its own, and a number between
them dims the frame. `"overScene": true` brings the card over the last seconds of the scene, which
goes on under it, instead of holding the last frame: the movie is then no longer for it.

```json
"endCard": { "text": "Thank you", "seconds": 4, "backdrop": 0.5, "overScene": true }
```

**Captions are also written as subtitles**: `<name>.srt` and `<name>.vtt` beside the movie, for a
video player or a web page that can show them, turn them off or translate them.

**Two stages.** A render with texts first renders the scene as numbered PNG frames, then makes the
movie from those frames with the texts over them. The frames need disk space while the render
runs (ThIDE counts them before it starts) and are removed when the movie is written. Because the
texts are added last, changing one does not need the scene rendered again: see `--compose-only`
under [From the command line](#from-the-command-line).

## Labels that appear and go

Labels need not be there for the whole movie. A render spec (the JSON a preset holds) can say when
a group of them is shown and hidden, under `labels.events`:

```json
"events": [
  { "target": "ComponentLabels", "showFrame": 1, "fadeSeconds": 3 },
  { "target": "StationLabels", "showFrame": 60, "hideFrame": 240 }
]
```

`target` is `StationLabels`, `ComponentLabels`, `LeadMarkers` (the markers with their captions) or
`Overlays` (the title, the scale bar, the north arrow and the depth legend). The group is hidden
until `showFrame` and from `hideFrame` on; `fadeSeconds` is how long it takes to appear.

The player **fades** the labels in: they become solid little by little, at their full size, and a
lead marker goes on pulsing while it does. Hiding is at once. (The script generator shows and hides
on the same frames, but instead of a fade it grows the labels from nothing.) A preview shows the
fade as a cut: the labels are simply there from `showFrame` on.

## Two scene builders

ThIDE has two ways of building the scene in Blender, and **Scene builder (advanced)** chooses
between them:

- **Auto** (the default) uses the *player* whenever it can show everything the scene asks for, and
  the *script generator* otherwise. When the script generator was used, the panel says why.
- **Player** — the newer builder. ThIDE writes the scene as data (a *scene plan*) and a fixed
  program in Blender plays it. It writes the texts in the second stage, renders previews, and can
  go on with a render that was stopped. Asked for by name, it refuses a scene it cannot show
  completely and says what it cannot show: at present an HDRI image as lighting.
- **Generator** — the earlier builder, kept as it was for comparison and for what the player does
  not do yet. It writes one Python script per scene. It draws the title, the scale bar, the north
  arrow and the depth legend as objects in the scene (where a wall can hide them) and does not
  draw the credits line, captions, the end card or the fades.

A preview is always the player's, whatever is chosen here. **Export script…** and **Open in
Blender…** use the script generator.

## Other ways to run it

- **Export script…** — instead of rendering, ThIDE writes the `render.py` script plus its
  `model.ply` and `scene-meta.json` to a folder you pick, so you can run it yourself later with
  `blender -b --python render.py`, or archive/share it. Two options under **Output** change what
  the script needs beside it:
  - **Self-contained script** puts the scene's data (labels, legend) inside the script, so it no
    longer reads `scene-meta.json`. It still reads the model's mesh, `model.ply`, from beside it.
  - **Embed the meshes in the script**, which can be ticked only while the script is
    self-contained, puts the mesh in as well (and the terrain with its image, when the scene draws
    it): one `.py` file that renders anywhere, but a much larger one. It is off unless you tick it.
- **Open in Blender…** — opens the scene in Blender's *interactive* window so you can inspect or
  tweak it by hand before rendering.
- **Save preset** — store the current settings as your own preset; they appear in the strip
  alongside the built-ins and persist between sessions.

## From the command line

The same engine is available headless through the CLI, for scripting or CI:

```
therion-cli blender <model.lox|.3d> [--preset "Orbit showcase" | --spec spec.json] \
                    [--out <dir>] [--export <dir>] [--blender <path>] \
                    [--draft] [--builder auto|player|generator] \
                    [--keep-frames] [--compose-only] [--frames <dir>] \
                    [--title <text>] [--credits <text>] [--end-card <text> [--end-card-seconds <n>]] \
                    [--caption "<start>-<end>:<text>"]...
```

`--export` writes the script and assets without running Blender; otherwise it renders. Produced
file paths are printed to standard output; the exit code is `0` on success, `1` on a render
failure, `2` on a usage or spec error.

- `--draft` renders the quick preview instead of the render, as the panel's **Preview** does.
- `--builder` chooses the scene builder; `auto` is the default. With `player`, a scene the player
  cannot show completely is a usage error that says what it cannot show.
- `--title`, `--credits`, `--end-card` (with `--end-card-seconds`) and `--caption` add texts to the
  preset's or the spec's. A caption is one argument, its times in seconds from the start of the
  movie: `--caption "2.5-6:Sala Mare"`; give the option once for each caption.
- **A render that was stopped goes on.** A render with texts keeps its frames in
  `<out>/<name>-frames` while it runs. Run the same command again after it was stopped (or after
  the computer was): the frames that are there are kept and only the missing ones are rendered.
  Frames of another scene (another camera, another model, another look) are not used: they are
  rendered again.
- `--keep-frames` keeps the frames when the movie is written (and renders in two stages even
  without texts). `--frames <dir>` puts them elsewhere.
- `--compose-only` renders nothing: it writes the movie again from the frames that are there,
  with the texts as they are now. Changing a title or a caption then costs seconds instead of the
  whole render:

  ```
  therion-cli blender cave.lox --preset "Full flythrough" --out renders --title "Peștera Mare" --keep-frames
  therion-cli blender cave.lox --preset "Full flythrough" --out renders --title "Peștera Mare, 2026" --compose-only
  ```

  It refuses frames that were rendered from another scene, so it cannot put new texts over an old
  picture by mistake.

### A render bundle, to render on another machine

`--bundle` writes everything Blender needs into one zip (or into a folder, when the path does not
end in `.zip`), for rendering on any computer with Blender 4.2 or newer and without ThIDE:

```
therion-cli blender <model.lox|.3d> [--preset "Full flythrough" | --spec spec.json] \
                    --bundle cave-bundle.zip [--draft] [--render [--render-plan full|draft|both]] \
                    [--out <dir>] [--blender <path>] [--readme-language en|ro]
```

The bundle holds the scene plan (`scene-plan.json`: the camera move, the look and the output, as
plain JSON), the cave's mesh (`model.ply`), the player that builds the scene in Blender
(`render.py` and `thide_player/`), the run scripts `render.sh` (Linux, macOS) and `render.cmd`
(Windows), a `README.txt` that says how to render it, and the player's licence: `LICENSE` (the
Mozilla Public License 2.0) and a `NOTICE` that says what the player is and that its source is in
the ThIDE repository on GitHub. The zip holds one folder named after
it (`cave-bundle` for `cave-bundle.zip`), so unpacking it anywhere makes that folder and nothing
else. Whoever receives it unpacks it and runs `sh render.sh` or `render.cmd` in that folder; the
result lands in the bundle's `out` folder. The scripts find Blender on the PATH (and in its usual install folder on macOS and
Windows); the `BLENDER` environment variable names another one, for example
`BLENDER=/opt/blender-4.5/blender sh render.sh`.

A bundle carries the terrain surface and its image when the scene draws them (`surface.ply`,
`surface.png`), and the texts over the picture, in the plan's `texts`. A scene with texts renders
in two stages there too, into the bundle's `frames` folder and then `out`; started again after it
was stopped, it goes on. `sh render.sh --keep-frames` keeps the frames, and
`sh render.sh --compose` (or `render.cmd --compose`) writes the movie again from them after a
text in `scene-plan.json` was changed. Captions are in the bundle as subtitles already
(`out/<name>.srt`, `.vtt`).

- `--draft` adds a second plan, `scene-plan.draft.json`, with a fast look: Blender's solid view at
  half the picture size, a fraction of a second per frame. `sh render.sh --draft` (or
  `render.cmd --draft`) renders it as `<name>-draft`, to check the camera move before the long
  render.
- `--render` also renders the bundle here, with the Blender ThIDE finds (or `--blender`): the zip is
  unpacked into a temporary folder and what it renders is moved next to the zip, or into `--out`.
  Without `--draft` it renders the full plan; with `--draft` it renders the draft, because a
  bundle made with `--draft` is usually made for a quick look at the camera move before the long
  render. The full render stays one option away: `--render-plan` chooses `full`, `draft` or
  `both` (the draft first, then the full plan). `draft` and `both` need `--draft`.
- `--readme-language ro` writes the bundle's `README.txt` in Romanian; without it, it is in English.
- The same model and settings always give the same zip under the same name, byte for byte.
- A scene plan cannot yet say everything a render spec can: the HDRI light rig is left out. The
  command warns about what it leaves out, and the bundle's `README.txt` lists it. The timed showing
  and hiding of labels is in the plan (its `changes`), so a bundle plays it.

**What a scene plan can say.** `scene-plan.json` is plain JSON that any text editor can change and
any program can write, and it can say more than the scenes ThIDE makes use of:

- Under `changes`, what happens while the clip runs, as keys in seconds: a thing is shown, hidden
  or faded (`visible`), is there in part (`opacity`), takes a colour (`tint`); a model is
  stretched (`scale`) and moved (`offset`); a light is turned up and down (`energy` — a sun that
  goes out as the camera goes underground); the world changes its colour and its strength
  (`worldColor`, `worldStrength`). A key may lie after the end of the clip, two keys on one frame
  are a move that ends in a jump, and a colour can go from key to key as a screen mixes colours
  (`"mix": "srgb"`).
- Under `lines`, lines through given points, drawn as thin tubes: the legs of a survey, a route.
- A ramp of colours over height can be measured in the model itself (`"space": "model"`), so the
  colours stay with the rock while a model is stretched or moved.
- A camera pose can have a clip range of its own (`clipStart`, `clipEnd`), for a flight from far
  away to close by.

A draft shows a model's tint, and how much of a fading model is there by the share of its faces
it draws. The schema that describes every field is in the ThIDE repository
(`src/Therion.Blender/Plan/scene-plan.schema.json`).

## Requirements & tips

- **Blender 4.2 or newer** — older versions are rejected with a clear message.
- **Cycles is the safe default.** EEVEE is faster but needs a graphics context, so it can fail on
  a display-less Linux box — the panel and CLI both fall back gracefully and tell you the device
  actually used.
- **Big caves** render fine but slowly; use **Preview** to check the camera move and the framing
  in seconds before the final pass.
- **Disk space** — frame sequences keep every PNG and can be large, and so do the frames a movie
  with texts is made from; ThIDE estimates the size and refuses up front if the drive is nearly
  full.

## Troubleshooting

| Symptom | What to do |
|---|---|
| *"Blender was not found"* | Install Blender 4.2+, or set its path in **Settings → External Tools**. |
| *"…too old…"* | Update Blender to 4.2 or newer. |
| The source dropdown is empty | Compile the project first (**Compile → Compile**), or tick **Use an external file**. |
| The render failed | Open the **job log** from the notification or the panel — it has Blender's full output. |
| EEVEE render fails on a server | Switch the engine to **Cycles**. |
| *"The player cannot make this scene…"* | The scene asks for something the player does not show yet (the message names it). Set **Scene builder** to *Auto* or *Generator*. |
| The preview has no terrain | A see-through terrain is left out of previews; the render draws it. |
| A title or the north arrow is hidden by the walls | The script generator built the scene (it draws them in the scene). Set **Scene builder** to *Auto* or *Player*. |
