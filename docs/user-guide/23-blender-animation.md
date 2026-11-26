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
   - **Map reveal** — a pass from almost straight above in which the cave appears little by
     little, from its entrances inwards (see
     [Revealing the cave part by part](#revealing-the-cave-part-by-part)).
   - **Documentation stills** — framed top / front / side / isometric images.

   A preset fills in the camera, materials, lighting, output and texts all at once. You can then
   tweak any setting below it.

   Every built-in preset **writes the cave's name over the picture**: *Orbit showcase*, *Map
   reveal* and *Documentation stills* at the top throughout, *Helix descent* and *Full
   flythrough* on a card the movie opens on, for three seconds. The name is the one the model
   has; over a model without a name nothing is written. Change it, or clear it, in the panel's
   **Title** field: see [Texts in the panel](#texts-in-the-panel).

3. **Adjust settings** as needed:
   - **Camera** — the motion template.
   - **Terrain** — the terrain surface a *.lox* model can carry, drawn above the cave: *Off* (the
     default), *Solid* (one colour) or *Textured* (the surface image draped over it; one colour when
     the model has no image). It is see-through from above and invisible from below, casts no
     shadow, and the camera still frames the cave. **Terrain opacity**, beside it, sets how much
     of the cave shows through from above: 0 % hides the terrain, 100 % hides the cave under it;
     the default is 40 %. A very large grid is thinned to at most 512 × 512 points.
   - **Light** — the *light rig*: a head lamp on the camera, a sun and sky, three studio lights, or
     an image all round the scene (*HdriFile*), with its **strength**. See
     [Lighting by an image (HDRI)](#lighting-by-an-image-hdri).
   - **Engine** — *Cycles* (the default, works everywhere) or *EEVEE* (faster, but needs a
     display; on a headless Linux server use Cycles). **Device** picks the GPU backend for
     Cycles (Auto tries them all and falls back to CPU).
   - **Animation** — frames per second and duration; the panel shows the resulting **frame
     count**.
   - **Reveal** — the cave appears part by part while the clip runs, by distance, survey order
     or survey date: see [Revealing the cave part by part](#revealing-the-cave-part-by-part).
   - **Output** — a video (MP4/MKV/WebM), a numbered PNG sequence, or a single still; plus
     resolution and file name.
   - **Labels** — turn on station labels and lead markers. *Lead marker size* chooses how big a
     marker is: *Auto* sizes it from its passage in a flythrough and from the whole cave otherwise;
     *Cave*, *Passage*, *CameraDistance* (about the same size in the picture at any distance, its
     caption with it) and *HiddenInFlythrough* fix one way. *Labels above* chooses the roof a station label is lifted
     above: the surveyed height first, the walls first, the walls only, or none (on the station).
   - **Annotations** — labels, callouts, markers and photos of your own, loaded from a file: see
     [Annotations](#annotations-labels-callouts-markers-and-photos).
   - **Texts over the picture** — the **Title** and its **Subtitle**, the **Credits line**, the
     **End card**, the **Scale bar**, the **North arrow**, the **Depth legend**, the fades and the
     **Captions**, each for its time: see [Texts in the panel](#texts-in-the-panel).
   - **Scene builder (advanced)** — what builds the scene in Blender, for a render, an export
     and a scene opened in Blender; leave it on *Auto*. See
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

In a picture that is **higher than wide** (a phone's 9:16), the title, its subtitle and the north
arrow are smaller by the picture's shape, and the title's lines end before the arrow begins: a
title of a few words stays on one line and clear of the arrow. A picture as wide as 3:2 or wider
is laid out as described above.

### Texts in the panel

Under **Texts over the picture** the panel has the texts of the table above:

- **Title** — the title. The preset decides whether it stays at the top of the picture or is a
  card the movie opens on; what you type here is the text. Empty, there is none.
- **Subtitle** — a smaller line under the title. Without a title it is not shown.
- **Credits line** — a small line that stays in the bottom right corner (who surveyed, who drew,
  a licence).
- **End card** — a text on a black picture that the movie ends on, for the seconds beside it.
- **Scale bar**, **North arrow**, **Depth legend** — tick the ones the picture should have.
- **Fade in** and **Fade out** — how long the movie takes to come out of black at its start and
  to go to black at its end, in seconds; 0 is no fade.
- **Captions** — lines at the bottom of the picture, each for its time. **Add caption** adds a
  row after the last one, three seconds long: type its **text**, and set the seconds it is shown
  **from** and **to**, counted from the start of the movie. The **✕** at the end of a row removes
  it. A row without a text is left out; a caption that ends before it starts is listed under
  **Fix before rendering**.

Under the rows a **strip** shows the captions along the clip, each where its seconds fall, with
its number. The grey part is the clip itself: a caption that reaches past it would run on after
the scene has ended, and the strip grows to show how far. One caption is shown at a time, so of
two that overlap on the strip the later one cuts the earlier short.

**The cave's name in a text.** Write `{cave}` where the cave's name should stand: in the title,
the credits line, the end card or a caption. It is filled in from the model when the render
starts: a `.3d` file's title, or the title of the survey at the top of a `.lox` file (the
`-title` of your outermost `survey`). The built-in presets use it for their titles, which is why
the Title field shows `{cave}` until you type something else. A text that is nothing but the
name is left out when the model has none.

**Two numbers of the model.** `{length}` stands for the length of the model's centreline and
`{depth}` for its vertical range, each in whole metres with its unit ("1234 m", "87 m"). They are
what the model itself holds: `{length}` is the sum of its legs, without the legs it flags as
surface, splay or duplicate, and `{depth}` is the height from its lowest station to its highest.
That is not always the surveyed length and depth a report states (a model may hold part of a
cave, or surface legs that are not flagged); type the numbers yourself where they must be the
official ones. A text that is nothing but a number the model does not give is left out.

**Texts in presets.** A preset keeps the texts with everything else. **Save preset** stores the
title and its subtitle, the credits line, the end card, the scale bar, the north arrow, the depth
legend, the fades and the captions as they are in the panel, and choosing the preset later brings
them back, the captions as rows you can edit. Use `{cave}` in a preset you
mean to use for more than one cave.

What the panel has no field for (the corner line and its timed texts, the seconds of a title
card) is set in a render spec (the JSON a preset holds, under `labels.overlays`); the title, the
credits line, the end card and captions also on the command line (`--title`, `--credits`,
`--end-card`, `--caption`) or by an assistant through the MCP tools. A preset that has them keeps
them while you change the texts above.

**An end card over the scene.** In a render spec the end card has two more settings, under
`labels.overlays.endCard`. `backdrop` says how dark the card is: 1 (the default) is the black card,
0 leaves the last frame as it is and puts the text on a dark box of its own, and a number between
them dims the frame. `"overScene": true` brings the card over the last seconds of the scene, which
goes on under it, instead of holding the last frame: the movie is then no longer for it.

```json
"endCard": { "text": "Thank you", "seconds": 4, "backdrop": 0.5, "overScene": true }
```

**How long they fade.** In a render spec, `"fade"` on the end card says how many seconds it takes
to appear (0 is at once; without it, half a second), and `"titleFade"` beside the title says how
long the title takes to appear and, when it is a card, to go (without it a title is there from
the first frame and a card goes in 0.8 s):

```json
"title": "{cave}", "titleSeconds": 3, "titleFade": 1.5,
"endCard": { "text": "Thank you", "seconds": 4, "fade": 2 }
```

`"titleFadeIn"` and `"titleFadeOut"` give the two their own times: `"titleFadeIn": 0.3,
"titleFadeOut": 1.5` has the card come quickly and go slowly. Each, when set, wins over
`"titleFade"` for its half; `titleFadeOut` goes with `titleSeconds`, because only a card goes.

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

## Revealing the cave part by part

A cave need not be all there from the first frame. With a **reveal** it appears part by part while
the clip runs, in an order the survey itself gives:

| Reveal | The cave appears… | It needs |
|---|---|---|
| `distance` | from its entrances inwards, each place when the reveal has got as far along the passages as that place lies | nothing: every model has it. A cave without a marked entrance starts at the first station of its first leg; `from` names another station to start at |
| `order` | leg by leg, in the order the survey has its legs in, each leg growing from the end that is already there | nothing |
| `date` | in the order of the **survey dates**: the history of the exploration, with a **counter of the years** in a corner of the picture | survey dates |

The preset **Map reveal** reveals by distance. Any other preset takes a reveal in the panel:

- **Reveal the cave by** — *Off* (the whole cave from the first frame), *Distance*, *Order* or
  *Date*. The fields below it appear once a reveal is chosen.
- **From station** (by distance) — the station the distances are measured from; empty, they are
  measured from the entrances.
- **Dates file** (by date) — the `.3d` file that has the survey dates, typed or picked with
  **Browse…**; empty, a `.3d` of the same name beside the model is used (see *Where the dates
  come from*, below).
- **Reveal starts** — the second the reveal begins at. **Reveal until the end of the clip** is
  ticked at first; untick it and **Reveal ends** takes the second at which the whole cave is
  there. Give the cave a second or two at the end.
- **Reveal edge** — how soft the edge is (0 is a clean cut), and **Glow where the cave
  appears**.

A file that is not there, a reveal that ends before it starts or starts after the clip are listed
under **Fix before rendering**. **Save preset** keeps the reveal.

A reveal also comes from the command line (`--reveal date`, see [below](#from-the-command-line)),
from the MCP tools (`reveal`), or stands in the render spec itself, which has two settings more
than the panel (`glowColor` and `counter`; the panel keeps a preset's):

```json
"reveal": { "by": "Date", "start": 1, "end": 9, "edge": 0.03, "glow": true, "counter": true }
```

- `start` and `end` are when the reveal begins and when the whole cave is there, in seconds;
  without `end` it ends with the clip. Give the cave a second or two at the end.
- `edge` is how soft the edge is, as a share of the whole reveal: what appears takes that much of
  it to become solid. `0` is a clean cut.
- `glow` makes what has just appeared shine (in `glowColor`, a warm orange unless you say
  otherwise) until the reveal has moved on.
- `counter` (for `date`) writes the year the reveal has got to above the scale bar. **Map
  reveal** has it on: switch that preset to `date` and it counts the years. With
  `"counterFormat": "{} m"` a reveal by `distance` also has a counter, of the metres from the
  entrance, and one by `order` counts the legs; `{}` is where the number goes, and
  `counterDecimals` (0 to 6) how many decimals it has.
- `from` (for `distance`) is the name of the station the distances are measured from.
- `datesPath` (for `date`) names the file that has the dates.

**Where the dates come from.** A `.3d` file carries the day each leg was surveyed; a `.lox` file
does not. For a `.3d` model the dates are simply there. For a `.lox` model, which is the one with
the real walls, ThIDE takes the dates from the `.3d` of the same cave: a `.3d` of the same name
beside the `.lox`, or the one you name (`--reveal-dates cave.3d`, `datesPath`). The legs of the two
files are matched by where their stations are, so both must come from the same survey data.

- A leg without a date is shown from the start of the reveal; the render says how many there are.
- A model with no dates at all is rendered whole, and the render says why.
- A cave surveyed in three expeditions appears in three steps. With surveyed walls (a `.lox`),
  each piece of wall appears with the leg it lies nearest to.

**What to know.**

- **Only the player draws a reveal.** A scene the script generator builds shows the whole cave
  throughout, and the result says so.
- A still, and a set of stills, show the whole cave.
- A preview shows the reveal as a cut along the faces of the model, without its soft edge or its
  glow, and says so.
- Labels and annotations are not revealed with the cave: give them times of their own
  (`labels.events`, an annotation's `from`), as the preset **Map reveal** does for its labels.

## Annotations: labels, callouts, markers and photos

Beside the labels ThIDE makes from the model (its stations, its leads), a scene can have
**annotations** of your own: things that point something out, each at a station or at a point.

| Kind | What it is |
|---|---|
| `label` | a text on its station, turned to the camera |
| `callout` | a text that stands away from its station, with a line from the station to it |
| `marker` | a glowing ball, or a pin, a cone, a cube, a ring or a small picture |
| `outline` | a line along a list of points, for the outline of a passage or a room on the surface it lies on |
| `photo` | a picture from a PNG or JPEG file, on its station or above it with a line to it |

They are written as a JSON list, in a file of their own:

```json
[
  { "kind": "callout", "station": "main.0", "text": "Intrarea" },
  { "kind": "label", "station": "main.14", "text": "Sala Mare", "from": 4, "to": 9 },
  { "kind": "photo", "station": "main.14", "image": "photos/hall.jpg", "size": 6,
    "offset": [0, 0, 5], "framed": true, "within": 40 },
  { "kind": "marker", "at": [512040.5, 5120010.2, 1180], "color": [1, 0.8, 0] }
]
```

| Key | Meaning |
|---|---|
| `kind` | `label`, `callout`, `marker`, `photo` or `outline` |
| `station` *or* `at` | where: the name of a station, as the model has it, or a point `[east, north, up]` in the model's own coordinates |
| `text` | what a label or a callout says |
| `image` | a photo's file, a `.png` or a `.jpg`, named from where the annotations file lies |
| `size` | in metres: the height of a text's letters, the radius of a marker, the width of a photo. Left out, it suits the cave: a text as large as a station label, a photo a tenth of the cave's extent |
| `offset` | `[east, north, up]` in metres: where a callout's text or a photo stands from its station. A callout without one stands four letter heights above; a photo without one is on its station, and with one it has a line to it |
| `color` | `[red, green, blue]`, each 0 to 1, of a text or a marker |
| `symbol` | of a marker: `ball` (the default), `pin`, `cone`, `cube`, `ring` or `image`. A pin and a cone stand with their point on the station; a ring turns to the camera; `image` is a small picture (`image` names its file) as wide as twice `size` |
| `sizeBy`, `sizeFactor`, `minSize`, `maxSize` | of a marker: `"sizeBy": "distance"` makes its radius `sizeFactor` metres for every metre between it and the camera, `"screen"` makes it as wide in the picture as `sizeFactor` times the picture's width, wherever the camera is; both stay between `minSize` and `maxSize` (a quarter and four times `size` unless you say) |
| `points` | of an outline: the points `[east, north, up]` it runs along, in the model's own coordinates; its thickness is `size`, and it is where its first point is unless you give a `station` or `at` |
| `framed` | `true` puts a border about a photo |
| `facing`, `normal` | `"facing": "fixed"` makes a photo a card that faces along `normal` (`[east, north, up]`) and stays so; without it a photo faces the camera |
| `from`, `to` | when it is there, in seconds from the start of the movie; without them, throughout |
| `within` | it is there only while the camera is within this many metres of its station: a photo that appears as a flythrough comes to its place |
| `fade` | how long it takes to appear and to go, in seconds (0.5 unless you say; 0 is a cut) |
| `id`, `role` | its name in the scene, and a group name, for a scene plan's `changes` |

A photo that faces the camera stays parallel to the picture and upright in it, so it is always
seen as the rectangle it is. It shows its image as a screen would, whatever the light in the cave,
and lights nothing itself. A text and a marker glow, like the labels.

**Photos.** A photo is a PNG or a JPEG of at most 32 MiB, 16,384 pixels a side and 64 million
pixels; anything else is refused before Blender starts. A picture 2,000 pixels wide is more than
a movie shows: smaller files make smaller bundles and faster renders. The files stay where they
are; ThIDE copies them for the render, and into a bundle (its `photos` folder). Where a PNG is
clear (a picture cut out of its background), the photo is not there and the cave behind it is
seen; a preview's mosaic shows the whole rectangle.

**In the panel.** Under **Annotations**, type the file's path or pick it with **Browse…**, and
press **Load**: the scene takes the file's annotations *in place of* those it had, and the line
below says how many there are and of which kinds. What is wrong with them is listed in red
under it, and under **Fix before rendering**: an annotation without a text or a station, a photo
whose file is not there or is not a PNG or a JPEG. A file that is not a list of annotations is
not loaded, and the scene keeps the ones it had. The panel reads the file when you press
**Load**: press it again after changing the file. **Clear** takes the annotations out of the
scene (the file stays). **Save preset** keeps them, and a preset that has annotations brings
them back; choosing a preset replaces the ones of a file.

**Elsewhere.** On the command line, `--annotations notes.json` (once for each file); through the
MCP tools, `annotations` names such a file in the workspace; in a render spec, the same list
stands under `annotations`.

- A station the model does not have stops the render, with its name.
- An annotation that is never shown (its time lies after the movie, the camera never comes that
  near) is reported as a warning.
- **Only the player draws annotations.** A scene the script generator builds leaves them out, and
  the result says so.
- A preview shows a photo as a mosaic of its colours (Blender's solid view draws no image), and a
  fade as a cut.

## Lighting by an image (HDRI)

A scene can be lit by a picture of a whole surrounding: a sky, a studio, a quarry at dusk. Such a
picture (an *HDRI*) is laid round the scene, is what the camera sees behind the cave, and lights
the cave with its own colours and from its own bright places. ThIDE ships none; many are free to
download.

**In the panel**, under **Light**: choose *HdriFile* as the **Light rig** and name the image in
**HDRI image**, or press **Browse…**, which picks the file and chooses the rig for you. **Light
strength** says how brightly it shines and **HDRI rotation** turns it. An image that is not
there, or is neither a `.hdr` nor an `.exr`, is listed under **Fix before rendering**. **Save
preset** keeps the image's path with the rest.

It is also a setting of the render spec, under `lighting`, of the command line, and of the MCP
tools (`hdri`, `hdriStrength`, `hdriRotation`, for an image in the workspace):

```json
"lighting": { "rig": "HdriFile", "hdriPath": "skies/evening.hdr", "strength": 1.5, "hdriRotation": 90 }
```

```
therion-cli blender cave.lox --hdri skies/evening.hdr --hdri-rotation 90 --hdri-strength 1.5 --out renders
```

- **The image** is a Radiance **`.hdr`** or an OpenEXR **`.exr`** file, the usual formats of such
  pictures: twice as wide as it is high, the whole horizon across and the sky at the top. ThIDE
  checks the file before Blender starts, by what it holds and not by its name, and refuses one
  that is neither, or that is larger than 256 MiB, 16,384 pixels a side or 16,384 × 8,192 pixels.
  An image 4,096 pixels wide lights a scene as well as a larger one; a larger one only makes the
  background sharper.
- **`hdriRotation`** turns the image about the vertical, in degrees, anticlockwise seen from
  above: use it to bring the sun of the picture round to where you want the light to come from.
  Not turned, the middle of the picture lies in the east.
- **`strength`** makes it brighter or dimmer: 1 is the image as it is.
- The file stays where it is. ThIDE copies it for the render, and into a
  [bundle](#a-render-bundle-to-render-on-another-machine) (its `hdri` folder), so a bundle renders
  on another machine without it.
- A preview draws no image: its background is a plain grey, as strong as the image is set.

## A cave seen from far away: the lamp's reach

The *Headlamp* rig is a lamp that rides the camera, and its light falls off with the square of
the distance: it lights a wall three metres away well, one 25 m away to about a twelfth of that,
and one 140 m away not at all. A flythrough is always near a wall. A camera that stands back from
the cave (an orbit, a still of the whole cave, a flight that follows from 30–140 m) sees it dark.

**The lamp reaches as far as the camera looks** turns the lamp up by the square of the distance
from the camera to what it looks at, beyond 3 m, so that the cave is lit from far as it is from
near. Nearer than 3 m the lamp keeps the strength it has, and **Light strength** still scales it.

- **In the panel**, under **Light**: tick **The lamp reaches as far as the camera looks** (it can
  be ticked while the rig is *Headlamp*).
- **In a render spec**: `"lighting": { "rig": "Headlamp", "lampReach": true }`.
- **On the command line**: `--lamp-reach`, which also chooses the head lamp as the light.

Only the player draws it; a script written by the script generator leaves it out and says so. By
the numbers of a test render: an invented cave in one still from 148 m is 0.1 of 255 bright by the
lamp as it is, and 154 with the reach.

Whoever writes scene plans with the library has two more tools for this (see the library's
`CHANGES.md`): `LampReach`, which keys the lamp's energy from distances of their own, and a
material that is **unlit** (`"unlit": true`), which shows its colours as a screen shows them
whatever the light, for survey lines and routes that have to read at any distance.

**The *SunSky* rig is the same in every Blender.** Blender 5 has a newer sky model beside the one
of Blender 4, bluer and brighter; the player asks for the sky of Blender 4 in both, so a scene
looks the same in Blender 4.2, 4.5 and 5.0. (Up to ThIDE's player 1.8 a scene under the sun and
the sky looked very different in Blender 5.)

## Flying a route between stations

The *Flythrough* template flies the cave's longest passage. The **Route** template flies the way
you name: from one station to another along the passages, stopping at stations on the way. The
stations are joined by the shortest passages between them, so a route out to the end of a side
passage and back is three names.

- **Speed by passage size.** The camera flies at twice the passage's radius a second, held
  between 1.5 and 12 m/s: a crawl 1.4 m wide is flown at 1.5 m/s, a gallery 10 m wide at 10 m/s.
  The radius is read from the passage's walls as the survey's left, right, up and down give them;
  a passage drawn from real scrap walls is flown at 4 m/s. Between passages of different size the
  speed changes gradually.
- **Stops.** At every stop the camera slows down to rest over about a second, holds (1 s at a stop
  on the way, unless you give another time), turns its view in place, at most 100° a second, to
  the way it goes on, and speeds up again. At the end of a dead end it turns right round, in about
  two seconds.
- **The clip's length.** A route takes the time its travel, holds and turns take, and on the
  command line that is the clip's length. Given a length instead, the travel is sped up or slowed
  down to fit it; the holds and turns keep theirs.
- **Two names stand for stations the model gives:** `entrance`, the first station flagged as an
  entrance, and `farthest`, the station farthest from the route's start along the passages. A
  route with no stations named runs from the entrance to the farthest station. A station named in
  more than one survey is named with its survey, `5@upper`.

An invented example: the cave's entrance is `e1`, a side passage leaves the main passage at `s12`
and ends at `b7`, and the main passage goes on to `s40`. To fly in, look into the side passage
to its end, come back and go on:

```
therion-cli blender cave.lox --preset "Full flythrough" --route e1:b7:s40
```

The same in a render spec:

```json
"camera": {
  "template": "Route",
  "route": { "from": "e1", "via": ["b7"], "to": "s40", "holdSeconds": 1.5, "endHoldSeconds": 2, "timing": "Natural" }
}
```

`stations` (`["e1", "s12", "s40"]`) lists stations to pass instead of `from`, `via` and `to`; the
camera then stops only at the first and the last. `startHoldSeconds`, `holdSeconds` and
`endHoldSeconds` are the holds at the start, at each stop on the way and at the end;
`lookAheadMeters`, `clearanceMeters` and `smoothingIterations` are the flythrough's. `speeds`
(`[1, 2]`) says how many times the passage's own speed each stretch between two stops is flown at:
in at the passage's speed and back out at twice that. With
`"timing": "Fit"` (the default) the route is fitted to the spec's duration. In the panel, the
**Route** template flies from the entrance to the farthest station in the clip's length. A
station the model does not have stops the render with its name in the message.

## Two scene builders

ThIDE has two ways of building the scene in Blender, and **Scene builder (advanced)** chooses
between them:

- **Auto** (the default) uses the *player*, which shows everything a scene can ask for.
- **Player** — the newer builder. ThIDE writes the scene as data (a *scene plan*) and a fixed
  program in Blender plays it. It writes the texts in the second stage, renders previews, and can
  go on with a render that was stopped.
- **Generator** — the earlier builder, kept as it was for comparison. It is used only when you
  choose it here. It writes one Python script per scene. It draws the title, the scale bar, the
  north arrow and the depth legend as objects in the scene (where a wall can hide them) and does
  not draw a title card, the credits line, captions, the end card, the fades, the
  [annotations](#annotations-labels-callouts-markers-and-photos) or a
  [reveal](#revealing-the-cave-part-by-part), and it does not turn an
  [HDRI image](#lighting-by-an-image-hdri). The result says what it left out.

A preview is always the player's, whatever is chosen here. **Render**, **Export script…** and
**Open in Blender…** all use the builder chosen here, so the three show the same scene.

## Other ways to run it

- **Export script…** — instead of rendering, ThIDE writes the scene **Render** would show into
  the **Export folder**, for you to render later, on another machine, or to archive and share. It
  is the same folder a [render bundle](#a-render-bundle-to-render-on-another-machine) holds:

  | File | What it is |
  |---|---|
  | `scene-plan.json` | the scene: models, camera, look, texts, annotations, output |
  | `render.py`, `thide_player/` | the player, which builds the scene from the plan in Blender |
  | `model.ply`, `photos/`, `hdri/` … | the meshes and the images the plan names |
  | `render.sh`, `render.cmd`, `README.txt` | scripts that start Blender, and how to use them |
  | `out/` | where the render writes; a movie's captions are there already as `.srt` and `.vtt` |

  Run it with `sh render.sh` (`render.cmd` on Windows) or
  `blender -b --factory-startup --python render.py`. Nothing in the folder needs ThIDE, and the
  folder can be moved.

  **Self-contained script (one file)**, under **Output**, makes the export one `render.py` that
  carries the plan, the player, the meshes and the images inside itself: a much larger file that
  renders anywhere, into `out` beside it.

  With the *Generator* chosen as the scene builder, the export is the script generator's instead:
  `render.py` with `model.ply` and `scene-meta.json`, without what that builder does not draw (the
  panel lists it). *Self-contained script* then puts the scene's data inside the script, and
  **Embed the meshes in the script** the mesh as well; that second option is for the script
  generator only, since the player's one file always carries its meshes.
- **Open in Blender…** — opens the scene **Render** would show in Blender's own window, seen
  through its camera, so you can look round it or change it by hand. Nothing is rendered; a
  render you start there writes to the **Render folder**. The player builds the scene, so
  everything in it is there: the cave, the labels, the annotations, the reveal, the light. **The
  texts over the picture are not**: they are written over the finished frames in the second
  stage, and Blender's window shows the scene before that. The panel says so when it opens a
  scene that has texts.
- **Save preset** — store the current settings as your own preset, the texts and captions
  included; they appear in the strip alongside the built-ins and persist between sessions.

## From the command line

The same engine is available headless through the CLI, for scripting or CI:

```
therion-cli blender <model.lox|.3d> [--preset "Orbit showcase" | --spec spec.json] \
                    [--out <dir>] [--export <dir>] [--blender <path>] \
                    [--draft] [--builder auto|player|generator] \
                    [--keep-frames] [--compose-only] [--frames <dir>] \
                    [--title <text>] [--credits <text>] [--end-card <text> [--end-card-seconds <n>]] \
                    [--caption "<start>-<end>:<text>"]... [--annotations <file.json>]... \
                    [--reveal date|order|distance [--reveal-from <station>] \
                     [--reveal-dates <file.3d>] [--reveal-seconds <start>-<end>]] \
                    [--hdri <image.hdr|.exr> [--hdri-rotation <degrees>] [--hdri-strength <n>]] \
                    [--leads <file.json>]... [--lamp-reach] \
                    [--route <from>[:<via>...]:<to> [--route-seconds <n>]]
```

`--export` writes the script and assets without running Blender; otherwise it renders. Produced
file paths are printed to standard output; the exit code is `0` on success, `1` on a render
failure, `2` on a usage or spec error.

- `--draft` renders the quick preview instead of the render, as the panel's **Preview** does.
- `--builder` chooses the scene builder; `auto` is the default and is the player. `generator`
  asks for the earlier builder.
- `--title`, `--credits`, `--end-card` (with `--end-card-seconds`) and `--caption` add texts to the
  preset's or the spec's. A caption is one argument, its times in seconds from the start of the
  movie: `--caption "2.5-6:Sala Mare"`; give the option once for each caption. `{cave}` in any of
  them stands for the cave's name. The built-in presets write that name as their title:
  `--title ""` renders without one.
- `--hdri evening.hdr` lights the scene by an image all round it, which is the background too:
  see [Lighting by an image (HDRI)](#lighting-by-an-image-hdri). `--hdri-rotation 90` turns it
  and `--hdri-strength 1.5` makes it brighter. It goes with `--bundle` too.
- `--lamp-reach` lights the scene by the lamp on the camera and turns it up with the distance
  the camera looks over: see
  [A cave seen from far away](#a-cave-seen-from-far-away-the-lamps-reach). It does not go with
  `--hdri`.
- `--route e1:b7:s40` flies the camera from `e1` through `b7` to `s40` along the passages,
  whatever the preset's camera was: see [Flying a route between stations](#flying-a-route-between-stations).
  The route sets the clip's length; `--route-seconds 45` fits it into 45 seconds instead. It goes
  with `--bundle` too.
- **Lead markers.** A preset or spec that marks leads (*Full flythrough* does) marks the stations
  the model itself flags as continuations, which a `.lox` file carries (Therion's
  `station <name> "<comment>" continuation`); a `.3d` file has no such flags. `--leads leads.json`
  names the stations to mark instead (once for each file): a JSON list whose entries are a
  station's name, or `{ "station": "main.12", "note": "draughts" }`. A station the model does not
  have is left out. It goes with `--bundle` too.
- `--annotations notes.json` adds the labels, callouts, markers and photos of a file to the
  preset's or the spec's: see
  [Annotations](#annotations-labels-callouts-markers-and-photos). It goes with `--bundle` too. A
  script written with `--export` is the script generator's and leaves them out, as it leaves out
  captions; the command says so.
- `--reveal date`, `--reveal order` or `--reveal distance` shows the cave part by part: see
  [Revealing the cave part by part](#revealing-the-cave-part-by-part). `--reveal-from main.12`
  measures a reveal by distance from that station instead of the entrances; `--reveal-dates
  cave.3d` names the file with the survey dates for a model that has none; `--reveal-seconds 1-9.5`
  says when the reveal starts and when the whole cave is there. Over a preset that reveals
  already, `--reveal` changes what it reveals by and keeps its times. It goes with `--bundle` too;
  a script written with `--export` leaves it out, and the command says so.
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
                    [--out <dir>] [--blender <path>] [--readme-language en|ro] \
                    [--annotations <file.json>]... [--reveal date|order|distance ...]
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
`surface.png`), the annotations with their photos (`photos/`), the image the scene is lit by
(`hdri/`), and the texts over the picture, in the plan's `texts`. A scene with texts renders
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
- A scene plan says everything a render spec can, so a bundle leaves nothing out: the timed
  showing and hiding of labels is in the plan (its `changes`), and the image of an HDRI light rig
  is among its files.

**What a scene plan can say.** `scene-plan.json` is plain JSON that any text editor can change and
any program can write, and it can say more than the scenes ThIDE makes use of:

- Under `changes`, what happens while the clip runs, as keys in seconds: a thing is shown, hidden
  or faded (`visible`), is there in part (`opacity`), takes a colour (`tint`); a model is
  stretched (`scale`) and moved (`offset`); a light is turned up and down (`energy` — a sun that
  goes out as the camera goes underground); the world changes its colour and its strength
  (`worldColor`, `worldStrength`: the strength of an HDRI image too, so a sky can dim as the
  camera goes in). A key may lie after the end of the clip, two keys on one frame
  are a move that ends in a jump, and a colour can go from key to key as a screen mixes colours
  (`"mix": "srgb"`).
- Also under `changes`, a model or a line is shown part by part (`reveal`): the keys are a
  threshold, and at every moment the parts whose **reveal value** lies below it are there. The
  values are one number for every vertex of the model's mesh: the vertex property `reveal` of a
  PLY file (ThIDE writes it when a spec has a reveal; `scene-meta.json` says what the values are
  and what range they span, under `reveal`), the attribute `_REVEAL` of a GLB file, or the `reveal`
  of a line's strips, one number for every point. They are in units of their own (years, metres, a
  count), and so is the threshold. `revealFront` says how the edge looks: `edge` (how far behind
  the threshold a part is solid), `glow` (`color`, `strength`, `width`) and `attribute` (another
  name for the values). A text with a `counter` shows the number the threshold has got to, in
  place of the `{}` in it.
- Under `annotations`, the labels, callouts, markers and photos, each with its place and its size
  in metres. An annotation can belong to a model (`"model"`): it then goes where its point of the
  model goes while the model is stretched or moved. When one is there is a change of it, by its
  `id`: its `visible` keys, with a `fade`.
- Under `lines`, lines through given points, drawn as thin tubes: the legs of a survey, a route.
- A ramp of colours over height can be measured in the model itself (`"space": "model"`), so the
  colours stay with the rock while a model is stretched or moved.
- A camera pose can have a clip range of its own (`clipStart`, `clipEnd`), for a flight from far
  away to close by.
- Under `look.world`, the surrounding: one colour, a daylight sky, or an image (`"kind":
  "image"`, with its `image` file, its `strength` and its `rotation` in degrees).

A draft shows a model's tint, and how much of a fading model is there by the share of its faces
it draws; a reveal is a cut along the faces; a photo is a mosaic of its colours there. The schema that describes every field is in the ThIDE repository
(`src/Therion.Blender/Plan/scene-plan.schema.json`).

## A film of several shots: the storyboard

One render is one camera move with one look. A **storyboard** is a film made of several of them:
a list of **shots**, each a camera move with a look of its own, joined with cuts, dips to a colour
and dissolves, with texts over one shot or over the whole film. It is a JSON file
(`*.storyboard.json`) that you write, or change, in any text editor, and render from the command
line:

```
therion-cli blender <model.lox|.3d> --storyboard film.storyboard.json [--out <dir>] \
                    [--draft] [--shot <n>] [--keep-frames] [--compose-only] [--frames <dir>] \
                    [--bundle film-bundle.zip [--render [--render-plan full|draft|both]]] \
                    [--blender <path>] [--leads <file.json>]...
```

An invented example, of four shots:

```json
{
  "version": 1,
  "name": "{cave}: a short film",
  "fps": 30,
  "defaults": {
    "materials": { "rock": "DepthGradient" },
    "labels": { "overlays": { "scaleBar": true, "northArrow": true } }
  },
  "texts": {
    "title": "{cave}", "titleSeconds": 3,
    "credits": "Survey: the Invented Caving Club",
    "endCard": { "text": "{length} surveyed, {depth} deep", "seconds": 4 },
    "fadeFromBlack": 1
  },
  "shots": [
    { "name": "From outside", "preset": "Orbit showcase", "seconds": 12 },
    { "name": "Down the shaft", "preset": "Helix descent", "seconds": 15,
      "in": { "kind": "Dissolve", "seconds": 1 },
      "texts": { "corner": "The entrance shaft", "northArrow": true } },
    { "name": "How it was found", "preset": "Map reveal", "seconds": 20,
      "in": { "kind": "Dip", "seconds": 0.6 } },
    { "name": "The main passage", "preset": "Full flythrough", "seconds": 30,
      "reveal": "hold" }
  ],
  "output": { "kind": "Video", "container": "Mp4", "width": 1920, "height": 1080,
              "baseName": "invented-film" }
}
```

**What a shot says.** A shot has a `name`, its length in `seconds`, and any of the sections a
render spec has: `camera`, `materials`, `lighting`, `engine`, `labels`, `annotations`, `reveal`.
What it leaves out it takes from the `preset` it names, or from the storyboard's `defaults`. A
preset brings its camera move, materials, lighting, engine, labels in the scene and reveal, and
its length when the shot gives none; it does not bring its title, because a film has its own
texts. The words of a list (`Dissolve`, `Orbit`, `DepthGradient`) are read in any case, and a key
that is not known is refused with its name, so a mistyped key is not passed over in silence. A
shot's camera can fly a route (see [Flying a route between
stations](#flying-a-route-between-stations)): with `"timing": "Natural"` and no `seconds` the shot
is as long as the route takes, and with `seconds` the route is fitted to them.

**How a shot begins** (`in`): with a cut when it says nothing; with a `Dip`, where the picture
goes to a colour (`color`, black by default) over the last frames of the shot before and comes
out of it over the first frames of this one; or with a `Dissolve`, where the picture goes evenly
from the one shot to the other. A dissolve of one second starts the shot one second before the
other ends, and both are rendered for that second, so the film is that much shorter than its
shots together. `seconds` is how long the dip (both halves) or the dissolve takes; it cannot be
longer than the shots it joins. The first shot begins the film; a fade out of black is one of the
film's texts.

**Texts.** The storyboard's own `texts` are written over the whole film, with times from the
film's start: a title or a title card, credits, a corner text, captions, the end card after the
last shot, the fades out of black and to black. A shot's `texts` are written over that shot, with
times from the shot's start: a title, captions, a corner text, and the scale bar and the north
arrow, which are measured through that shot's camera and are hidden while a dip or a dissolve
runs. A shot without `texts` has those of the defaults (`defaults.labels.overlays`): in the
example the first, third and fourth shots have a scale bar and a north arrow, and the second,
which has `texts` of its own, a corner text and a north arrow. The captions of the film and of its
shots are written beside the film as subtitles (`.srt`, `.vtt`).

**The reveal.** A shot can show the cave part by part (see [Revealing the cave part by
part](#revealing-the-cave-part-by-part)), with times from the shot's start. The shot after it says
what becomes of that: `"reveal": "hold"` keeps the cave as the shot before left it, `"reveal":
"none"` shows all of it, and a reveal of its own starts again. The counter counts on through the
film. All the reveals of one storyboard are by the same thing (all by date, or all by distance
from the same station), because the shots share one model.

**Rendering.** Every shot is rendered into a folder of its own (`<out>/<name>-frames/s01`,
`s02`, …), each in a Blender of its own; then the film is written from those frames in one more
pass, which joins the shots and writes the texts.

- If the render is stopped, run the same command again: the shots that are finished are kept, and
  it goes on in the shot it stopped in.
- With `--keep-frames`, change one shot in the storyboard and run the command again: only that
  shot is rendered again. Change a text of the film, or how two shots are joined, and nothing is
  rendered again: the film is written from the frames that are there.
- `--draft` drafts every shot (Blender's solid view, half the size): an animatic of the whole
  film in a fraction of the time, to check the moves, the lengths and the joins before the long
  render.
- `--shot 3` renders the third shot alone, as any scene is rendered, with the shot's own texts;
  the options that add to one scene (`--title`, `--caption`, `--reveal`, …) go with it.
- `--bundle` writes the film as a [render bundle](#a-render-bundle-to-render-on-another-machine):
  the plan of every shot and of the film, with one copy of the mesh for all of them. `sh
  render.sh` (or `render.cmd`) renders it shot by shot there, and `--draft`, `--keep-frames` and
  `--compose` do for the film what they do for one scene.

A photo, an HDRI image and a file of survey dates are named relative to the storyboard's file. A
storyboard is a film: for stills, or a Blender file of the scene, render one shot (`--shot`) or
use a render spec. The schema that describes the file is in the ThIDE repository
(`src/Therion.Blender/storyboard.schema.json`).

## Branch tours

A **tour** is a film of the cave made by rules instead of by hand: the main passage is flown from
the entrance inwards, and at every junction worth it the camera flies into the side passage,
shows its end and comes back. ThIDE works the tour out from the model, brings it within a time
you give, and writes it as a [storyboard](#a-film-of-several-shots-the-storyboard), which is
rendered, drafted or bundled like any other, and can be edited by hand first.

```
therion-cli blender cave.lox --tour --draft
therion-cli blender cave.lox --tour --write-storyboard tour.storyboard.json
therion-cli blender cave.lox --storyboard tour.storyboard.json
```

The first line drafts the whole tour, to look at before the long render. The second writes the
storyboard and renders nothing; the third renders it, after you changed what you wanted. Every
`--tour` command prints the table of the side passages and what it decided about each.

**The main passage** (`--trunk`). By default it runs from the first station flagged as an entrance
to the station farthest from it along the passages (`farthest`); a model without an entrance gets
its longest passage, as the *Flythrough* template flies it (`longest`). `deepest` runs to the
lowest station, `through` to another entrance (or to `--tour-end`), and `via` through the stations
you name (`--tour-via s20 --tour-via s77 --tour-end s140`). `--tour-start` names another start. The
main passage is flown once and passes no station twice.

**Side passages.** With the main passage's legs taken out, every connected remainder of the cave
is one side system. One that hangs on the main passage at a single station is a **branch**: it is
flown from its junction to its own farthest station. One that hangs on it at two stations is a
**bypass** (a loop shows up as one): it is flown through from its first junction to its second,
then the film goes back to the first and carries on along the main passage, so nothing is flown
twice and nothing is missed. They are numbered **B1, B2, …** along the main passage; the numbers do
not change with what is chosen. The table says for each: its junction, its length (all its legs),
its **reach** (how far from the main passage it gets), its depth change, its mean width (left +
right of the survey), its leads, its surveys and its rank.

**Which are visited.**

| Rule | Default | Option |
|---|---|---|
| Least length | the greater of 50 m and 2 % of the main passage | `--min-length 30` |
| How many | 12, the best ranked first | `--max-branches 8` |
| Rank | by reach, then by length | |
| Leads | a side passage with a lead is visited whatever its length | `--no-lead-branches` |
| By hand | wins over every rule: a branch's number, a station in it, or a survey's name | `--include B2`, `--exclude side2` |
| Depth | 1: what hangs on the main passage; 2 also what hangs on the visited branches (B3.1, B3.2) | `--tour-depth 2` |

The leads are the stations the model flags as continuations, or those of `--leads <file.json>`.

**How a side passage is visited** (`--branch-mode` for all of them, `--branch B5=return` for one):

| Mode | What the camera does | Film time |
|---|---|---|
| `cut` (default) | flies in, slows down, stops a passage radius short of the end and holds 1.5 s; then the film cuts back to the junction, through a dip of 0.3 s | the flight in |
| `return` | as `cut`, then turns round and flies back out at twice the speed | about 1.6 times |
| `peek` | stays at the junction and looks into the side passage for 3 s | 3 s |
| `skip` | passes by | none |

A bypass is always flown through. `--peek-short` gives the side passages that are left out as too
short a look from their junction. While a side passage is on, its number and length stand in the
corner of the picture ("Side passage B3 · 96 m"), and every lead on the camera's way has a marker
and its note while the camera is near (`--tour-language ro` writes these texts in Romanian). A
tour's shots show no station labels of the cave.

**Time.** The camera flies at the speed the passage's size gives (see [Flying a route between
stations](#flying-a-route-between-stations)), so the film has a natural length. Two budgets hold
it: `--tour-seconds` (180) for the film and `--branch-seconds` (20) for one side passage. When
something is over its budget, this is done, in this order:

1. a side passage is flown faster, up to twice the speed; then it is flown only as far as its
   time allows, and the corner says how far it goes on ("continues 60 m"). A flight back that
   does not fit becomes a cut first;
2. the whole tour is flown faster, up to twice the speed;
3. flights back become cuts, the lowest-ranked first;
4. the lowest-ranked side passages are left out (looks first; those you chose by hand last);
5. the main passage itself is cut short.

Whatever was left out or cut short is said in the notes the command prints, and in the README of
a bundle.

An invented example: a main passage of 620 m from `e1` to `s140`, nine side systems, 180 s.

| Branch | Junction | Length | Reach | Leads | Chosen | Mode | Seconds |
|---|---|---|---|---|---|---|---|
| trunk | e1 → s140 | 620 m | — | 1 | yes | — | 96 |
| B1 | s12 | 140 m | 110 m | 0 | yes | cut | 20 (speed 1.4×) |
| B2 | s31 | 12 m | 10 m | 0 | no (short) | skip | 0 |
| B3 | s48 | 96 m | 80 m | 2 | yes | cut | 17 |
| B4 | s48 → s77 | 190 m | bypass | 0 | yes | through | 20 (cut short at 130 m: continues 60 m) |
| B5 | s90 | 22 m | 20 m | 1 | yes (lead) | return | 10 |
| B6–B9 | … | 8–30 m | … | 0 | no (short) | skip | 0 |

The film: `e1` to `s12`, into B1 and a cut back, on to `s48`, into B3, through B4 as far as 20 s
allow, back at `s48` and on past `s77` to `s90`, into B5 and out again, and on to `s140`.

**A tour file** says the same as the options, and travels with a project (`--tour-file
tour.json`; an option given beside it wins):

```json
{
  "trunk": "farthest", "start": "e1",
  "minLength": 30, "maxCount": 8, "include": ["B2"], "exclude": ["B7"],
  "mode": "cut", "modes": { "B5": "return", "B9": "peek" },
  "tourSeconds": 180, "branchSeconds": 20,
  "preset": "Full flythrough", "language": "en",
  "output": { "width": 1920, "height": 1080, "baseName": "cave-tour" }
}
```

`preset` (`--preset`) is where the film takes its materials, lighting and render engine from; they
are written into the storyboard's `defaults`, where you can change them for every shot at once.
`--bundle` writes the tour as a [render bundle](#a-render-bundle-to-render-on-another-machine),
with the table and the notes in its README. An agent has the same through the tools `plan_tour`
(the table, nothing written) and `write_tour_storyboard` (see [the agent tools](../mcp-host-setup.md)).

Good to know:

- The tour stays in the part of the cave its start is in; parts that no leg joins to it are named
  in a note and get no visit.
- A side passage flown at depth 1 is flown to its farthest station; its own side passages are
  counted in its length and its leads, and visited only with `--tour-depth 2`.
- The storyboard names every station the camera passes, so it belongs to this model: after the
  survey changed, make the tour again rather than editing an old storyboard.

## Large caves, long renders

**The walls in parts.** A large cave (more than half a million wall triangles, or more than two
kilometres across) has its walls written in parts: the passages, a few hundred metres of them to a
part (by a grid of 100 m for walls drawn from scraps). The parts together are the one mesh, vertex
for vertex, with the same depth colours and the same reveal, so the picture does not change. What
changes is that a camera **inside** the cave (a flythrough, a route) shows only the parts within
200 m of its path; a camera outside (an orbit, a still) shows all of them. `--parts on` splits any
cave, `--parts off` never splits one.

**Labels near the route.** An inside camera labels only the stations, and marks only the leads,
within 200 m of its path; the cap of 200 station labels then counts those. In a film, each shot
picks its own. An annotation whose station lies in a part a shot leaves out is left out with it, so
nothing floats where there are no walls.

**What it will cost.** Before a render starts, the progress says what it will cost: the frames,
the time at the seconds a frame is assumed to take (or as measured), the disk for the frames and
the movie, and the memory Blender will need. A scene of more than 20 million triangles gets a
warning (a graphics card may not hold it). While it renders, the progress says the seconds a frame
takes and the time left, from the frames written so far. On the command line, `--estimate` prints
the estimate and renders nothing:

    therion-cli blender cave.lox --storyboard tour.storyboard.json --estimate

**A range of frames.** `--frame-range 1-600 --frames <dir>` renders frames 1 to 600 of a scene
alone into the frames folder, and nothing else. Several renders can share one folder, each with a
range of its own, at the same time; the movie is written once all of them are there:

    therion-cli blender cave.lox --spec tour.json --frames frames --frame-range 1-900
    therion-cli blender cave.lox --spec tour.json --frames frames --frame-range 901-1800
    therion-cli blender cave.lox --spec tour.json --frames frames --compose-only

This pays on a machine with **several graphics cards**: start one render per card, each seeing its
own card (`CUDA_VISIBLE_DEVICES=0`, `=1`, … for NVIDIA), each with its range. On one computer
without them it gains little, as Cycles already uses every core. A film's shots are already
rendered one by one; for a range of one shot, render that shot alone (`--shot`).

**One render at a time.** In ThIDE, renders run one at a time, whoever starts them: a render
started in the panel while an agent renders (or the other way round) waits for its turn, and the
panel says so. An agent's render that ends is listed in the panel's history. If ThIDE stops in the
middle of a render (not when it is cancelled), the render goes on with its frames when ThIDE starts
again.

## The render worker

`therion-cli render-worker` renders [render bundles](#a-render-bundle-to-render-on-another-machine)
that arrive in a folder, one at a time, oldest first, and leaves what they render in another. It is
for a machine with Blender and a good graphics card that has nothing else of ThIDE; how the folders
are filled (a synced share, an SFTP drop) is up to you.

    therion-cli render-worker --inbox /srv/render/in --outbox /srv/render/out

- Put a bundle in the inbox as `<job>.zip`. While it renders, `<job>.status.json` in the outbox says
  how far it is: the shot, the frame, the seconds a frame takes and the time left. When it is done,
  the outbox has `<job>.mp4` (or the stills), its subtitles and `<job>.log`.
- To stop a job, put an empty `<job>.cancel` beside its zip. Its frames are kept; remove the cancel
  file and it goes on with them. A worker that is stopped (Ctrl+C) also goes on with its frames
  when it starts again.
- `--once` stops when the inbox is empty; `--blender` names the Blender; `--max-hours`,
  `--max-triangles` and `--max-bundle-gb` set the limits of a job; `--scratch` is where bundles are
  unpacked and their frames kept.

**Security.** A bundle is data, and the worker treats it so. It never runs the bundle's own
`render.py`, `render.sh` or `thide_player`: it reads the plans, the meshes and the images, and plays
them with its own player. It refuses, before Blender starts, a bundle whose plans name module
folders or features outside the player's own, a path that leaves the bundle, a link, a plan the
checks do not pass, and a bundle over its limits (its size, the unpacked size, the triangles, the
frames × pixels × samples, the time). Extra modules come only from a folder the operator names
(`--modules`). Blender runs with `-b --factory-startup` (no add-ons, no start-up scripts), and only
the files the worker names go to the outbox. A fault in Blender's own readers of meshes and images
is the risk that remains: run the worker as a user that can read and write the two folders and
little else.

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
| *"…the player lights a scene with a Radiance .hdr or an OpenEXR .exr image…"*, *"…cannot travel with the plan…"* | The image of the HDRI lighting is another kind of picture, is damaged or is too large (the message says which). Use a `.hdr` or `.exr` file; or set **Scene builder** to *Generator*, which loads whatever picture Blender reads. |
| Every render has the cave's name across the top | The built-in presets write it. Clear the **Title** field (or `--title ""`). |
| The title is a short code, not the cave's name | It is the name the model has: give the outermost `survey` of your project a `-title`, or type the title you want. |
| The preview has no terrain | A see-through terrain is left out of previews; the render draws it. |
| A title or the north arrow is hidden by the walls | The script generator built the scene (it draws them in the scene). Set **Scene builder** to *Auto* or *Player*. |
