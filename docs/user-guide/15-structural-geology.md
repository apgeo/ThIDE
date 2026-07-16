# 15. Structural geology

> [← Back to the User Guide home](README.md) · [ThIDE project home](../../README.md)

The **Structural Geology** module turns groups of survey shots that lie on a geological plane (a
fault, bedding plane, joint…) into **strike / dip** measurements, and plots the resulting planes in
3D. Open it from **View → Structural Geology**.

> **Off by default.** Enable *Structural Geology module* in
> [Settings → Visualization](19-settings-and-preferences.md#visualization). For the method and
> maths, see the reference doc **[docs/structural-geology.md](../structural-geology.md)**.

The panel is a **four-step workflow** across tabs:

## 1 · Detect

ThIDE finds the shots you took *along* a plane, based on **detection signals** you can tune to your
survey's convention. A shot counts as structural if it matches **any enabled** signal:

- **From-station name contains keyword(s)** — e.g. you name plane stations `fault1`, `bed2`.
- **Shot comment contains marker(s)** — e.g. a `; bedding` comment.
- **From/To station carries flag(s)** — a station flag you use for structural points.

Set the **Scope** (active file or the whole workspace) and press **Detect ↻**.

## 2 · Measurements

The detected points, grouped into candidate planes. Tick the points to include in each plane fit —
**toggling recomputes that plane live**. Options:

- **Group a plane by** — how points are clustered into a plane (e.g. by station).
- **Splay shots** — include splays.
- **Include the from-station (origin) in the fit** — only when the station itself lies on the plane;
  watch the **RMS** column.
- **Magnetic declination** — optionally rotate strike / dip-direction to true north from a chosen
  **source** and value (dip is unaffected). Off by default so you don't double-count a survey's own
  declination.

Bulk selection helpers: **☑ All**, **☐ None**, **⇄ Invert**, group by station. Double-click a row to
jump to its source shot. **Export** the grid to CSV.

## 3 · Resulted planes

The fitted planes, one row each: **Dip °**, **Strike °**, **Dip dir °**, **North ref**, **Points**,
and **RMS** (fit quality — smaller is better). Toggle each plane's **Visible** flag to show/hide its
disc in the 3D plot; **Export** the grid to CSV.

## 4 · 3D plot

The planes drawn as **discs** in 3D against the cave.

- **Fit view** re-frames; **Disc size** enlarges the discs (they're tiny next to a cave); **White
  background** for print/export.
- **Export image…** saves the current view as a PNG.

> The 3D plot needs the bundled three.js assets and a system web engine (WebView2 / WebKit) — the
> same requirement as the [3D Viewer](11-viewers.md#3d-viewer). If unavailable, the tab shows a note
> instead.

---

Next: [The Relational Map →](16-relational-map.md)
