# 5. A tour of the interface

> [← Back to the User Guide home](README.md)

ThIDE is a **dockable, VS-style** workbench: an editor in the middle, tool panels around it, a menu
bar and toolbar on top, a status bar at the bottom. This page names every part so the rest of the
guide can refer to it.

## The overall layout

```
┌───────────────────────────────────────────────────────────────────────┐
│  Menu bar:  File  Edit  Search  View  Compile  Tools  Settings  Help    │
│  Toolbar:   open · nav · save · edit · find · go-to · import/export · … │
├───────────────┬───────────────────────────────────────┬───────────────┤
│  Left rail    │            Editor (document tabs)       │  Right rail   │
│  Workspace    │   ┌───────────────────────────────┐     │  Object       │
│  Explorer     │   │  Source  |  Measurements       │     │  Browser      │
│  Outline      │   │                                │     │  3D Viewer    │
│               │   └───────────────────────────────┘     │  Map Viewer   │
├───────────────┴───────────────────────────────────────┴───────────────┤
│  Bottom rail:  Diagnostics · Compiler Output · Generated Files · Log     │
├───────────────────────────────────────────────────────────────────────┤
│  Status bar:  path breadcrumb · line/col · length · build status · 🔔   │
└───────────────────────────────────────────────────────────────────────┘
```

Panels are **dockable**: drag a tab to re-dock it, float it into its own window (even onto another
monitor), or hide it. Your layout — and any floating windows — **persists** between sessions.
**View → Reset to Default Layout** puts everything back. **Split Editor (Float)** pops the active
document into a floating editor.

## The menu bar

| Menu | Contains |
|---|---|
| **File** | Open File / thconfig / Folder, Recent Files, Recent Directories, Reopen Closed Tab, Exit |
| **Edit** | Cut/Copy/Paste, case toggles, Toggle Comment, Enclose in Region, Fold/Unfold All, Add Bookmark |
| **Search** | Find, Replace, Find/Replace in Files, Go To Line, Bookmarks |
| **View** | Toggle each panel (Object Browser, Workspace, Diagnostics, Overview, Log, Mainline Preview, Map Viewer, 3D Viewer, Structural Geology, Outline, **Assistant**), Relational Map, whitespace/minimap toggles, Split Editor, **Language**, **Strict parser mode** |
| **Compile** | Compile / Recompile / Cancel, Build target, Quick Export, Survex tools, Therion info, Open in Loch/Aven, Open last output folder |
| **Tools** | Import (Survey/DEM/GPX), Export (entrances/table), Generate report, New scrap, Calculators, **Blender Animation** |
| **Settings** | Opens the [Settings](19-settings-and-preferences.md) window |
| **Help** | Therion Book, About ThIDE, Debug Info, Welcome |

## The toolbar

A single row of quick actions with tooltips (each names its shortcut): open file/thconfig/folder,
**Back/Forward** navigation, Save, Cut/Copy/Paste, **Find in Files**, **Rename Symbol**,
**Go to File** and **Command Palette**, go-to-symbol (workspace/document), import/export,
generate report, new scrap, Open in Loch/Aven, word-wrap, full-screen, layout, **Compile**, and the
**notifications** bell.

## The panels (tool views)

Every panel below is toggleable from the **View** menu and the Command Palette (the one exception is
**Blender Animation**, which opens from the **Tools** menu). Each has its own guide page:

| Panel | What it's for | Guide |
|---|---|---|
| **Workspace Explorer** | Your files as a nested source/input tree | [below](#the-workspace-explorer) |
| **Outline** | Survey/scrap structure of the current file | [Object Browser & Outline](12-object-browser-and-outline.md) |
| **Object Browser** | Tables of stations, shots, fixes, equates, maps, scraps, points, lines, areas | [Object Browser & Outline](12-object-browser-and-outline.md) |
| **Diagnostics** | Errors / warnings / info, with filters | [Diagnostics & validation](09-diagnostics-and-validation.md) |
| **Compiler Output** | Live Therion output (Output / Raw) | [Compiling & output](10-compiling-and-output.md) |
| **Generated Files** | Everything the last build produced, with per-file actions | [Compiling & output](10-compiling-and-output.md) |
| **Overview** | Dashboard, surveys, statistics, charts, team, entrances, quality, audit, leads, TODOs, metadata, media | [Survey Overview & analytics](13-survey-overview-and-analytics.md) |
| **Mainline Preview** | Plan/elevation centreline sketch, no compile needed | [The viewers](11-viewers.md#mainline-preview) |
| **Map Viewer** | In-app PNG/SVG/PDF map viewer | [The viewers](11-viewers.md#map-viewer) |
| **3D Viewer** | Embedded 3D model (CaveView.js) | [The viewers](11-viewers.md#3d-viewer) |
| **Structural Geology** | Plane strike/dip calculator + 3D plot | [Structural geology](15-structural-geology.md) |
| **Log** | Internal app log (indexing, background work, errors) | — |
| **XVI** | Sketch (.xvi) references and their grids | [Object Browser & Outline](12-object-browser-and-outline.md) |
| **Assistant** *(experimental)* | Chat with a **local** AI model about your project | [Extensibility](20-extensibility.md#ai-assistants-mcp) |
| **Blender Animation** *(experimental)* | Render a video / stills of your cave via Blender | [Blender animation renders](23-blender-animation.md) |

Some panels are **off by default** to keep large projects light (Map Viewer, 3D Viewer, Structural
Geology, some analytics) — enable them under **Settings → Visualization / Survey analytics**. See
[Settings](19-settings-and-preferences.md).

### The Workspace Explorer

The left-hand file tree. Right-click a file or folder for: **Open**, **Reveal in File Explorer**,
**Set as Active Workspace thconfig**, **New File / New Folder**, **Delete**, **Copy Full/Relative
Path**, **Refresh**, and (on Windows) the native shell menu. It mirrors the `source`/`input`
structure, so you can see how files include each other.

## The editor area

Documents open as **tabs**. A `.th`/`.th2` file shows **Source** and **Measurements** sub-tabs (the
Measurements grid lists that file's shots/stations). Tab right-click menu: **Pin**, **Close**,
**Close Others / to the Right / All**, **Float**, **Copy File Name**. Closed a tab by accident?
**File → Reopen Closed Tab**. Everything about editing is in [The editor](07-editing.md).

## The status bar

Along the bottom: a **path breadcrumb** of the active file, cursor **Line/Col** and selection,
document **Length/Lines**, an **Indexing…** / **Build in progress…** indicator, the last build
result, and a shortcut to the Diagnostics panel (with F8 / Shift+F8 to step through problems).

## The Command Palette & notifications

- **Command Palette** (**Ctrl+Shift+P**) — the fastest way to *anything*. Type a command name, or use
  the prefixes `@` (symbol in this document), `#` (symbol in the workspace), `:42` (go to line). See
  [Navigation & search](08-navigation-and-search.md).
- **Notifications** — non-blocking toasts (build finished, import failed, unsaved changes recovered…)
  collect under the **bell** in the toolbar so you can review them later.

## Themes & language

- **Settings → Theme & Colors**: System / Light / **Dark** themes; optional **custom syntax colors**.
- **View → Language** (or Settings → General): **English** and **Română**, applied immediately.

---

Next: [Typical workflows →](06-typical-workflows.md)
