# Features

> [Documentation index](README.md) · [Project README](../README.md)

## Language intelligence
- Per-file parsers for `.th`, `.th2`, `.thconfig`, `.xvi` with typed ASTs; **lenient by default**, strict mode opt-in.
- Typed centreline model (`units`, `calibrate`, `declination`, `sd`, `grade`, `cs`, `extend`, `break`, `walls`, `station`, `mark`, …), typed `thconfig` (`select` / `export` / `maps` / `layout`), and a deep `.th2` model (point/line/area types and options).
- `layout … endlayout` parsing (incl. opaque `code … endcode`), symbol-set / legend directives, and EPSG/named coordinate-system validation.
- Cross-file **connectivity graph**, naming-convention lints, and **pluggable semantic rules** (`rules.json` + plugin DLLs).
- A **Diagnostics** panel with stable diagnostic codes — see [diagnostics.md](diagnostics.md) and [layout-and-embedded-code.md](layout-and-embedded-code.md).

## Editing & navigation
- AvaloniaEdit-based editor: bracket/quote auto-pairing, bookmarks, caret back/forward history, comment & case toggles, go-to-definition flash, line operations, insert-date/team.
- **Workspace Explorer** (nested source/input tree), **Object Browser** (stations / shots / scraps / maps / XVI references and entity tables), and an **Outline** of centrelines and their stations.
- **Command palette** (`Ctrl+Shift+P`), quick-open, and go-to-symbol.
- **Relational Map** — an interactive diagram of object relationships (surveys, scraps, maps).
- Find / replace and find / replace **in files**; drag-and-drop and "open with" file arguments.

## Build & visualization
- **Therion compile pipeline** with live output, cancel, and clickable diagnostics; a **Generated Files** panel with per-output actions and auto-open overrides.
- **Live 2D preview** of centrelines — equate-merged and fix-anchored, color-by survey/file/component, with clickable junction markers and per-component visibility.
- **Embedded 3D viewer** — renders compiled `.lox` / `.3d` models via [CaveView.js](https://github.com/aardgoose/CaveView.js) inside a native web view (no bundled Chromium); orientation/camera/feature toggles, color-by overlays, full-screen, and **click-a-station → jump to `.th` source**. See [3d-viewer.md](3d-viewer.md).
- **Loch / Aven** launchers and **thbook** PDF page lookup.

## Survey analytics & notes
- Project statistics: length breakdown (surface / underground / duplicate / splay), vertical range with hi/lo stations, horizontal extent; length by survey and by date; team members & expeditions; fixed points; data-quality checks.
- Charts, team, entrances, and quality tabs; export to **CSV / Markdown / HTML / LaTeX**; on-demand **HTML survey report** and station/shot **table export**.
- **Exploration leads** register and map overlay (continuation flags, QM / lead / `?` comments, dead-ends) with a per-project lifecycle.
- **TODO / QM aggregator** and a per-project **metadata** sidecar.

> Analytics are computed **in-app** from the semantic model so they work live without a compile. They are *preview-quality* (no loop adjustment) — Therion remains the source of truth for adjusted lengths.

## Import / export & GIS
- **Import:** Survex (`.svx`), Compass (`.dat`), DEM (ESRI ASCII → `surface`), and GPX waypoints (→ `fix`).
- **Export:** entrances and fixed points to **KML / GeoJSON / GPX / CSV**.
- `.th2` scaffolding (new scrap; sketch-from-image / `.xvi`) and coordinate transforms (WGS84 ↔ UTM, lat-long).

## Utilities
- Coordinate converter, unit converter, and a **magnetic declination** calculator (WMM/IGRF spherical-harmonic synthesis).

## IDE shell, performance & reliability
- VS-style **dockable** shell with layout and floating-window persistence; toast **notifications** with a history bell; tab pin / close-others / reopen-closed; auto-save; pinned & clearable recents.
- Tree virtualization, background indexing, a **persistent symbol index**, large-file guards, and string interning for big projects.
- **Crash recovery** / safe mode with autosaved dirty buffers; opt-in, **local-only** telemetry (off by default).
- Many heavier features are **toggleable** (Preferences ▸ Performance / Extensions / Visualization) so large projects stay responsive.

## Trust & safety
- Binary / oversized-file open guard, delete confirmation (trash vs. permanent), and an external-change banner with side-by-side diff.

## Extensibility & automation
- **`therion-cli`** — headless `validate` / `lint` / `format` / `stats` / `deps` / `gis` / `dump-ast` / `list-stations`. See [Usage](usage.md#command-line-tools).
- **`therion-lsp`** — an editor-agnostic Language Server (diagnostics over stdio). See [lsp.md](lsp.md).
- **Script hooks** (on open / save / build) and a **semantic-rule plugin loader** (`ISemanticRule` DLLs). See [plugins.md](plugins.md).
