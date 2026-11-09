# 5. A tour of the interface

> [← Back to the User Guide home](README.md)

ThIDE is a **dockable, VS-style** workbench: an editor in the middle, tool panels around it, a menu
bar and toolbar on top, a status bar at the bottom. This page names every part so the rest of the
guide can refer to it.

## The overall layout

```
┌───────────────────────────────────────────────────────────────────────┐
│  Menu bar:  File  View  Edit  Search  Tools  Compile  Settings  Help    │
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
│  Status bar:  path breadcrumb · line/col · length · compile status · 🔔 │
└───────────────────────────────────────────────────────────────────────┘
```

Panels are **dockable**: drag a tab to re-dock it, float it into its own window (even onto another
monitor), or hide it. Your layout — and any floating windows — **persists** between sessions.
**View → Layout → Restore Default Layout** puts everything back. **Split Editor (Float)** pops the active
document into a floating editor.

## The menu bar

| Menu | Contains |
|---|---|
| **File** | New File…, New from Template…, Open File / thconfig / Folder…, Recent Files, Recent Directories, Reopen Closed Tab, Save, Save As…, Save All, Close Tab, Exit |
| **View** | **Language**, **Welcome**, a toggle for each panel (Object Browser, Workspace Explorer, Overview, Log, **Assistant**, Automation, Mainline Preview, Map Viewer, 3D Viewer, Structural Geology, Diagnostics, Outline), Relational Map…, **Strict Parser Mode**, whitespace and minimap toggles, Split Editor (Float), **Layout** (save, load, restore the default layout, split the side panels) |
| **Edit** | Undo / Redo, **Undo Multi-File Change** / Redo (the [workspace undo](#undoing-a-tool-that-wrote-many-files)), Cut / Copy / Paste, Paste as Shot Data…, Delete, Select All, UPPERCASE / lowercase, Toggle Comment, Enclose in Region, Fold All / Unfold All, Add Bookmark…, Go to Definition, Peek Definition, Find References, Rename Symbol… |
| **Search** | Find, Replace, Find in Files…, Replace in Files…, Go To Line…, Bookmarks… |
| **Tools** | Import Survey / DEM / GPX, Scaffold Project…, **Link Survey File…**, **New Trip…**, Watch the Incoming Folder, Add Unreferenced Scraps to Maps…, Suggest Equates…, Format All Files…, Normalize Project…, Renumber Stations…, Macros, Tasks, Export Entrances / Fixed Points, Export Data Table, Generate Survey Report (HTML)…, New Scrap (.th2)…, **Blender Animation…**, Calculators |
| **Compile** | Compile, Compile This thconfig (Focused Tab), Recompile, Cancel, Compile Multiple…, Build Target, Quick Export…, Open in Loch / Aven, Survex Tools, Therion Info, Open Last Output Folder |
| **Settings** | Preferences… (opens the [Settings](19-settings-and-preferences.md) window, Ctrl+,) and Keyboard Shortcuts… (the same window, at the shortcut list) |
| **Help** | User Guide, Therion Book, Debug Info, About ThIDE |

The menu shows each command's current shortcut next to it; a command with no shortcut shows none.

## The toolbar

A single row of quick actions with tooltips (a tooltip shows the command's current shortcut, once,
and none for a command that has no shortcut): open file/thconfig/folder,
**Back/Forward** navigation, Save, Cut/Copy/Paste, **Find in Files**, **Rename Symbol**,
**Go to File** and **Command Palette**, go-to-symbol (workspace/document), import/export,
generate report, new scrap, Open in Loch/Aven, word-wrap, full-screen, layout, **Compile**, and the
**notifications** bell.

## The panels (tool views)

Most panels below are toggleable from the **View** menu and the Command Palette. The exceptions:
**Compiler Output**, **Generated Files** and **XVI** have no View entry — they are tabs in their rails
— and **Blender Animation** opens from the **Tools** menu. Each has its own guide page:

| Panel | What it's for | Guide |
|---|---|---|
| **Workspace Explorer** | Your files as a nested source/input tree | [below](#the-workspace-explorer) |
| **Outline** | Survey/scrap structure of the current file | [Object Browser & Outline](12-object-browser-and-outline.md) |
| **Object Browser** | Tables of stations, shots, fixes, equates, maps, scraps, points, lines, areas | [Object Browser & Outline](12-object-browser-and-outline.md) |
| **Diagnostics** | Errors / warnings / info, with filters | [Diagnostics & validation](09-diagnostics-and-validation.md) |
| **Compiler Output** | Live Therion output (Output / Raw) | [Compiling & output](10-compiling-and-output.md) |
| **Generated Files** | Everything the last compilation produced, with per-file actions | [Compiling & output](10-compiling-and-output.md) |
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
Geology, some analytics) — enable them on the **Visualization** page of Settings (**Settings →
Visualization**). See [Settings](19-settings-and-preferences.md).

### The Workspace Explorer

The left-hand file tree. The drop-down above it picks the **active thconfig**. Right-click a file or
folder for: **Open**, **Reveal in File Explorer**, **Set as Active Workspace thconfig**, **New File /
New Folder**, **Delete** (named by your system: *Move to Recycle Bin* on Windows, *Move to Trash* on
Linux and macOS, *Delete Permanently* where there is no trash), **Copy Full/Relative Path**, **Refresh**, and (on Windows) the native shell menu. It
mirrors the `source`/`input` structure, so you can see how files include each other.

## The editor area

Documents open as **tabs**. A `.th`/`.th2` file shows **Source** and **Measurements** sub-tabs (the
Measurements grid lists that file's shots/stations). Tab right-click menu: **Pin**, **Close**,
**Close Others / to the Right / All**, **Float**, **Copy File Name**. Closed a tab by accident?
**File → Reopen Closed Tab**. Everything about editing is in [The editor](07-editing.md).

Notices about a file or the session sit in a banner above the editor, not in a toast that can fall
off a small screen: a file changed on disk by another program, a file over a size limit, and — after
a crash — the **recovery banner**, which names the files that kept unsaved changes and offers
**Restore** or **Discard copies** (see [Troubleshooting](22-troubleshooting-and-faq.md#reliability--safety-what-to-do-when-things-go-wrong)).

### Undoing a tool that wrote many files

Rename Symbol, Replace in Files, **New Trip…**, **Link Survey File…**, **Renumber Stations…**,
**Format All Files…**, **Normalize Project…** and *Fix all* change several files in one go. The files
that are **not open** are written to disk right away — a closed file has no editor history — and a
file that is open with unsaved edits takes the change in its editor instead, so it stays unsaved.
Every such run is one entry in the **workspace undo**: **Edit → Undo Multi-File Change** (the item
names the operation once there is one, for example *Undo Rename*) or the **Undo** button on the
notification puts all the files back in one step, on disk and in the editors, and **Redo** applies
them again. If one of the files has changed since, nothing is restored and the message names it.

## The status bar

Along the bottom: a **path breadcrumb** of the active file, cursor **Line/Col** and selection,
document **Length/Lines**, an **Indexing…** / **Compiling…** indicator, the result of the last
compilation, and a shortcut to the Diagnostics panel (with F8 / Shift+F8 to step through problems).

## The Command Palette & notifications

- **Command Palette** (**Ctrl+Shift+P**) — the fastest way to *anything*. Type a command name, or use
  the prefixes `@` (symbol in this document), `#` (symbol in the workspace), `:42` (go to line). See
  [Navigation & search](08-navigation-and-search.md).
- **Notifications** — non-blocking toasts (compilation finished, import failed…)
  collect under the **bell** in the toolbar so you can review them later.

## Themes & language

- **Settings → Theme & Colors**: System / Light / **Dark** themes; optional **custom syntax colors**.
- **View → Language** (or **Settings → General**): **English** and **Română**. Menus and panels switch
  at once (from Settings, when you click OK); a few labels in lists that are already open follow
  after a restart.

---

Next: [Typical workflows →](06-typical-workflows.md)
