# 4. Core concepts & vocabulary

> [← Back to the User Guide home](README.md)

A handful of ideas explain how everything in ThIDE fits together. Learn these five and the rest of
the app makes sense.

## Workspace

A **workspace** is the folder (and its subfolders) that ThIDE treats as your project. When you
**Open Folder** (or open a `.thconfig`), ThIDE scans it, indexes every Therion file, and keeps that
index live. The workspace is the scope for project-wide features: cross-file navigation, "workspace
scope" diagnostics, the Overview analytics, find/replace in files, and the symbol search.

> ThIDE uses **workspace** for the project folder throughout the UI. You'll occasionally still see
> the word "project" in analytics contexts — it means the same thing.

## The active thconfig

A `.thconfig` is Therion's **configuration** file: it says which sources to read (`source` /
`input`), what to `select`, and what to `export` (maps, models, lists). A workspace can hold several.
ThIDE always has **one active thconfig** — the one it will compile and the one that defines "the
project" for analytics and viewers. You choose it (right-click in the Workspace Explorer → *Set as
Active Workspace thconfig*, or from a file's header), and it's shown in the status bar breadcrumb.

## The four file types

ThIDE understands each Therion file type natively:

| Type | What it holds | Highlights in ThIDE |
|---|---|---|
| **`.th`** | Surveys: centrelines, stations, shots, fixes, equates, maps | Full centreline model, measurements grid, station navigation |
| **`.th2`** | Drawings: scraps made of points, lines and areas | Point/line/area model, scrap navigation, hand-off to Mapiah |
| **`.thconfig`** | Project configuration: source/select/export/layout | Export targets, "set active", drives the build |
| **`.xvi`** | Sketch backgrounds referenced by scraps | Grid/scale model, linked from the scraps that use them |

Inside a `layout` block, ThIDE even highlights the **embedded MetaPost and TeX** code — see
[docs/layout-and-embedded-code.md](../layout-and-embedded-code.md).

## The object graph (the "semantic model")

This is the heart of ThIDE. As you open and edit files, ThIDE parses them and builds an in-memory
**object graph**: every survey, station, shot, scrap, map, fix and equate, plus the relationships
between them (who includes whom, which stations are the same via `equate`, which scraps a map
aggregates). It updates as you type.

Almost every "smart" feature reads from this graph:

- **Go to definition / find references / rename** across files.
- **Diagnostics** — unresolved stations, duplicate fixes, disconnected surveys, loop misclosure…
- **Analytics** — lengths, depths, teams, entrances, data quality.
- **Previews** — the Mainline Preview and the Object Browser tables.
- **The Relational Map** — a diagram of the graph itself.

Because it's computed in-app, it's instant — but it's **preview-quality** (no loop adjustment).
For final adjusted numbers and rendered maps, **compile with Therion**.

## Diagnostics: lenient vs strict

ThIDE continuously validates your files and reports **diagnostics** (errors, warnings, info) with
stable codes. By default it runs in **lenient mode** — many issues are warnings so you can keep
working. Turn on **View → Strict parser mode** to promote lenient issues to errors (useful for a
final clean-up pass). Every code is catalogued in [docs/diagnostics.md](../diagnostics.md); the
[Diagnostics & validation](09-diagnostics-and-validation.md) page explains the panel.

## The vocabulary of a cave (Therion terms you'll see)

These are Therion's own concepts, surfaced throughout ThIDE (e.g. as Object Browser tabs):

| Term | Meaning |
|---|---|
| **Station** | A named survey point (e.g. `1.2`, or `entrance.0`). |
| **Shot / leg** | A measured connection between two stations (length, compass, clino). |
| **Splay** | A wall shot from a station (used for passage shape, not the mainline). |
| **Survey** | A named `survey … endsurvey` block grouping stations and shots. |
| **Fix** | A station pinned to real-world coordinates (georeferencing). |
| **Equate** | A declaration that two station names are the same point (stitches surveys together). |
| **Scrap** | A drawing unit in a `.th2` file (points/lines/areas making up passage art). |
| **Map** | A `.thconfig`/`.th` construct aggregating scraps/other maps into output. |
| **Component** | ThIDE's term for a *connected piece* of the survey network. A cave that's all stitched together is one component; a floating survey is its own component. |

Understanding **components** helps read the previews and the "disconnected survey" warning: pieces
that aren't joined by shared stations/equates (and aren't georeferenced by a `fix`) float apart.

---

Next: [A tour of the interface →](05-interface-tour.md)
