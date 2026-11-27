# 25. A survey derived from a 3-D model

> [← Back to the User Guide home](README.md) · [ThIDE project home](../../README.md)

A laser scan or a photogrammetry model of a cave has walls but no survey: no stations, no legs, no
loops to close. **Derive Survey from 3-D Model** places a Therion centreline in such a model — a
mainline, as many side galleries as you ask for, legs near the length you prefer that never cut
through rock, LRUD and splays — and writes it as a `.th` file you can compile, draw over and extend.

> **A derived survey is not a measured one.** Every file it writes starts with a comment saying so,
> and its `instrument` line says the data came from a model. Do not send it to a cave registry as a
> survey.

---

## What it reads

| Format | Notes |
|---|---|
| **STL** (binary or ASCII), **OBJ**, **PLY** mesh | No unit and no "up" in the file: ThIDE guesses (millimetres when the model is huge, metres otherwise, Z up) and tells you — check it. |
| **glb** (binary glTF) | Metres and Y up by the format's own rules. Draco-compressed files are not read. |
| **PLY** point cloud, **XYZ**, **PTS** | Points only. Gaps between scan points narrower than about 0.4 m are closed (`void.holeClosing` in a settings file); give a start point inside the cave if no enclosed space is found. |

The model should be the cave's inside surface — a closed or nearly closed mesh. Holes from the scan are
fine; the largest hole is taken as the entrance.

## Opening it

**Tools → Derive Survey from 3-D Model…**, the same item in the toolbar's *Import* drop-down, or the
command palette.

The window has three tabs: **Model**, **Settings** and **Guide**.

## 1. The model

**Browse…** to the file, then check the **Input** settings: **Units**, **Up axis** and **North
rotation** (the compass direction the model's +Y axis points to; 0 if +Y is north). If the model is in
real map coordinates, put its **Coordinate system** (e.g. `UTM34N`) — it is written with the fix.
Changing them later needs **Open** again.

**What the model looks like** then shows its size in metres, whether the unit was guessed, how many
holes it has, how many loops it has (when it is closed), and how many voxels and how much memory the
next step will need. If the size looks wrong, the unit is wrong.

## 2. The settings

Start from a **Preset** and change what you need:

| Preset | For |
|---|---|
| **Scan backbone** (default) | Stations in the middle of the passages, a ring of splays, side galleries until 90 % of the walls are seen. |
| **Mainline only** | One line from the start to the farthest end. |
| **Mainline and major galleries** | The mainline and the galleries that leave it, if at least 10 m long and 1 m wide. |
| **Thorough** | Every level of gallery, niches, 98 % of the walls, adaptive splays. |
| **Synthetic survey** | What a team would have brought back: stations on the floor, a DistoX-style ring of splays, instrument error added. Useful for testing and teaching. |

**Legs** — the **Preferred length**, **Shortest** and **Longest**, and the **Strategy**:

- *Close to the preferred length*: legs as close to it as the passage allows;
- *Fewest legs*: as few legs as possible (long shots);
- *Equal spacing*: evenly spaced stations;
- *At junctions and bends*: stations only at junctions, bends, squeezes and chamber entrances.

Whatever the strategy, a leg is never allowed to pass closer to a wall than the **Clearance**, to leave
its passage, or to cut a corner through rock. Where nothing fits, the limits are relaxed step by step
and the result says where. **Longest vertical** limits legs down shafts (empty: a pitch can be one leg).

**Stations** — **Station position**: on the passage axis, on the floor, on the roof, or on the wall.

**Side galleries** — how far the survey branches off the mainline:

- **Depth**: *Mainline only*, *1 level* (galleries off the mainline), *2 levels* (and their galleries), or *Unlimited*;
- **Shortest gallery** and **Narrowest gallery**: smaller ones are not surveyed;
- **Most galleries** and **Length budget**: hard limits;
- **Loops**: *Close loops*, *Close loops if cheap* (only when worth it like any gallery), or *Never close loops*;
- **Side stations into niches**: one short leg into an alcove, cheaper than a gallery.

**Coverage** — the **Target**: the share of the walls that must be visible from some station. Galleries
are added one at a time, the one that shows the most new wall per metre of survey first, until the target
is reached or a limit stops it. Not everything can be covered — that would need a gallery into every
crack — so pockets shallower than the **Smallest feature** do not count. **Range** is how far a station
"sees" (an effective splay distance).

**Splays** — *None*, *LRUD (4 shots)*, a *Ring* across the passage, a *Sphere*, *Adaptive* (more shots
where the wall is irregular) or *Even on the wall* (ends spread evenly on the wall); **Shots per station**.

**Void and chambers** — **Voxel size** (0 = automatic: a quarter of the narrowest passage), **Narrowest
passage** (narrower connections are treated as rock — every such cut is reported), **Smallest pillar**
(thinner rock in the passage, such as a scanned person or a rope, is ignored), and what to do in **Large
chambers**: *Hub (one central station)*, *Along the long axis*, *Round the perimeter*, or *Keep the
skeleton as it is*.

**Readings and output** — **Station names** (*Sequential* 1, 2, 3 … or *By branch*: a letter prefix per gallery), sub-surveys of a set
size, cartesian readings instead of tape/compass/clino, and **synthetic instrument error** (off by
default; the file says when it is on).

## 3. The guide — your own start point and legs

Everything is automatic unless you say otherwise. On the **Guide** tab you can:

- give a **Start point** (it becomes the fixed station) — clear *Automatic* and type its coordinates;
- add **Points** the mainline must pass through;
- add **Legs** between points — some of them, or all of them.

As soon as a leg's two points are valid, the leg is **checked against the model**: green when it is
clear, amber when it passes too close to a wall or is out of the length range, red when it cuts through
rock (with how far along it hits and how much rock it crosses) or when a point is not in the passage.
**Chain points into legs** joins the start and the points in order. **Snap** moves a point onto the
passage axis, the floor, the nearest wall, or (for a point picked on the wall) into the passage.

The **Mode** says what happens to the rest: *Complete the rest automatically* (your legs are kept as they
are and the passages they survey are not surveyed again) or *Only the given legs* (just yours, with LRUD
and splays). **On a blocked leg**: *Stop with an error*, *Keep and warn*, or *Leave out*; **Force** on a leg keeps it whatever its check says (a scanned rope may look like rock).

Points can be given in *Model coordinates* (metres, Z up, north applied — what the result uses) or in
*File coordinates* (as another viewer shows the file) — **Coordinates**. **Load guide…**
reads a guide saved before, a Therion centreline with a `fix` in the model's coordinates, or a `.lox` /
`.3d` survey — the last gives you splays and LRUD from the scan for a survey you already have.

## 4. Run, look, save

**Run** builds the model's void and skeleton (the slow part — seconds to minutes; it is reused while you
change only the leg, gallery, coverage and splay settings) and places the survey. **Result** shows the
stations, legs and length, galleries and loops, leg lengths, the coverage reached and why it stopped, the
splays, and anything blocked, tight or relaxed.

**Preview in 3-D** shows the model with the derived survey in the 3-D viewer. **Save .th…** writes the
survey and, beside it, `<name>.mesh-survey.json` with every setting and finding; *Also write debug layers*
adds the skeleton and the regions no station sees as `.ply` files and a `.lox` preview.

## From the command line and from an assistant

`therion-cli mesh-survey inspect|check-leg|run <model>` does the same headless; run it without arguments
for the options. The MCP tools `mesh_survey_inspect`, `mesh_survey_check_leg` and `mesh_survey_run` give
an assistant the same, returning numbers only (see [MCP host setup](../mcp-host-setup.md)).

## Good to know

- Real data stays on your machine: nothing is uploaded anywhere.
- A model in the wrong unit gives absurd sizes on the Model tab; fix the unit before running.
- Big scans need memory: the Model tab says how much. Raise the voxel size (or the narrowest passage)
  to need less.
- The derived file is a starting point. Rename stations, add a `team` and dates, and keep its header.
