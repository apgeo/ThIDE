# 11. The viewers: Map, 3D & Mainline preview

> [← Back to the User Guide home](README.md)

ThIDE has three ways to *see* your cave without leaving the app. Two show **compiled** output (the
Map Viewer and the 3D Viewer); one shows a **live preview** straight from the model with no compile at
all (the Mainline Preview). All three are dockable panels under the **View** menu, and each can be
enabled/disabled in **Settings → Visualization**.

---

## Map Viewer

An in-app viewer for compiled **2D maps** — **PNG, SVG and PDF**. Open it from **View → Map Viewer**
(or the Command Palette → *Toggle Map Viewer*).

**Loading a map**

- **Open…** loads any PNG/SVG/PDF file directly.
- The **outputs picker** lists the maps your active thconfig declares via `export map` that exist on
  disk — choose one to display it.
- **Auto-show:** with *Auto-show the rendered map after a build* (or *…first map on workspace load*)
  enabled, ThIDE opens the first suitable map for you. A per-file Auto-open override can force a
  specific one.

**Getting around a map**

- **Zoom** with the `–` / `+` buttons or **Ctrl+scroll**; **100%** resets; **Fit** shrinks to the
  window (never enlarges past 100%).
- **PDF page navigation** (**◀ / ▶**) appears for multi-page PDFs.
- **Refresh** re-renders (e.g. after a rebuild). Rendering happens off the UI thread, so a big PDF
  won't freeze the app — a spinner shows while it works.
- **Full screen**, **float on another monitor**, and **move to the central panel** buttons are in the
  top-right.
- The footer shows the file name, page, zoom %, file size, and last-modified time.

> Prefer clicked PDFs to open here rather than externally? Enable *Open clicked PDFs in the in-app
> viewer* in [Settings → Visualization](19-settings-and-preferences.md#visualization).

---

## 3D Viewer

An **embedded 3D model viewer** that renders compiled `.lox` / `.3d` cave models right inside ThIDE
using [CaveView.js](https://github.com/aardgoose/CaveView.js) in your OS's native web engine (no
bundled browser). Open it from **View → 3D Viewer**.

> **Off by default.** Enable *Embedded 3D model viewer* (and optionally *Auto-show the 3D model after
> a build*) in **Settings → Visualization**. It needs a platform web engine — see
> [Installation](02-installation-and-setup.md#1-prerequisites).

**Loading a model**

- The **Model** dropdown lists every model the active thconfig produces (each `export model` whose
  output is a `.lox`/`.3d`), plus any such file next to the thconfig; unbuilt targets show *"— not
  built"*.
- **Open…** loads any `.lox`/`.3d` directly. After a build the newest model auto-loads when auto-show
  is on (`.lox` preferred over `.3d`).
- A model older than its sources is flagged **stale** — rebuild to refresh.

**Controls** (mirroring the public CaveView galleries): **orientation** (Plan / profile N·E·S·W),
**camera** (Perspective / Orthographic), and feature toggles — **Walls, Splays, Surface, Stations,
Labels, Names, Box** (bounding box), **Auto-rotate, Light, HUD**, and **Menu** (CaveView's own
sidebar). **Color by:** Height · Survey · Length · Inclination · Plain. **Reset view** re-frames;
**↺ Defaults** restores every switch; **Full screen** goes borderless (Esc to exit).

**The killer feature — click-to-source**

- **Click a station** in the 3D view to jump the editor to where that station is declared in the
  `.th` source.
- Going the other way, **Show in 3D View** — right-click a station (or a shot's *From*/*To*) in the
  Measurements grid, or use the editor hover card / right-click on a station or survey reference — to
  select and frame it in 3D (a survey gets a bounding box).

For the deeper reference (how it maps stations to source, requirements, fallbacks), see
**[docs/3d-viewer.md](../3d-viewer.md)**.

---

## Mainline Preview

A **live plan/elevation sketch of your centrelines**, drawn directly from the object graph — **no
compile needed**. Open it from **View → Mainline Preview**. It's the fastest way to see whether your
data makes geometric sense as you type. (Enable *Mainline centreline preview* in Settings →
Visualization.)

**Views**

- **Plan** (looking down, north up), or a **projected profile** along **N / E / S / W**, or a
  **Custom** bearing via a slider (0–359°).

**What to show**

| Toggle | Effect |
|---|---|
| **Splays** (as lines) | Show wall shots — as faded lines, or just the far-edge points. |
| **Labels** / survey.station | A name at each station, optionally qualified with its survey. |
| **Stations** | The dot at each station. |
| **Junctions** | Clickable markers where surveys are stitched by `equate`. |
| **Legend** | The visibility list — hover a row to locate that group. |
| **Separate** | Spread disconnected pieces into a grid instead of stacking them at the origin. |

**Colour by** — **None**, **Survey**, **File**, or **Component**. Colouring **by Component** is the
quick way to spot disconnected pieces: anything stacked at the origin (or off on its own) isn't
joined to the main network. The **Groups** row (all / none / invert / **main**) lets you show/hide
individual pieces.

Under the hood the preview merges `equate`d stations, anchors on `fix`es, and handles cross-file and
`@`-qualified equates — so it reflects how the cave actually stitches together. It remains
**preview-quality** (no loop adjustment); compile with Therion for the adjusted map.

---

Next: [Object Browser & Outline →](12-object-browser-and-outline.md)
