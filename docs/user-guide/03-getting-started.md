# 3. Getting started: your first session

> [← Back to the User Guide home](README.md)

This is a guided first run. It assumes you already have a Therion project (a folder with `.th` files
and at least one `.thconfig`). If you don't, grab any sample Therion project to follow along.

## Step 1 — Open your project

You can open at three levels of scope, all from the **File** menu (or the toolbar / Welcome page):

| Command | Use it when… | Result |
|---|---|---|
| **Open Folder…** | You want the whole project | Loads the folder as a **workspace** and indexes every Therion file in it. |
| **Open thconfig…** | You know your configuration file | Opens it *and* adopts its folder as the workspace. |
| **Open File…** | You just want to look at/edit one file | Opens a single file; a workspace may still be inferred from it. |

For real work, **Open Folder** (or Open thconfig) is what you want — that's what gives ThIDE the
cross-file understanding described in [Core concepts](04-core-concepts.md).

## Step 2 — Choose the active thconfig

A project can contain several `.thconfig` files. ThIDE builds and analyzes against **one active
thconfig** at a time. If your workspace has exactly one, it's chosen automatically. Otherwise, set it:

- In the **Workspace Explorer**, right-click a `.thconfig` → **Set as Active Workspace thconfig**, or
- Open the file and use its header action **Set this file as the active thconfig**.

The active thconfig drives the whole app: what compiles, which diagnostics are "workspace scope",
which outputs the viewers offer.

## Step 3 — Look around

Give ThIDE a moment to index (watch the **Indexing…** hint in the status bar). Then:

- The **Workspace Explorer** (left) shows your files as a nested source/input tree.
- The **Diagnostics** panel lists any problems it already found.
- The **Overview** panel summarizes the cave (length, depth, files, last build).
- Open a `.th` file and you'll get syntax colouring, and a **Measurements** tab next to the source.

A full map of the interface is in [A tour of the interface](05-interface-tour.md).

## Step 4 — Edit with confidence

Try these to feel the editor's "understanding" (details in [The editor](07-editing.md) and
[Navigation & search](08-navigation-and-search.md)):

- Hover a station or survey name for a **hover card**; click **Go to definition** (or Ctrl+click).
- Press **F2** on a station to **rename it everywhere** it's referenced (with an optional preview).
- Press **Ctrl+Shift+P** for the **Command Palette** — type to run any command, or prefix with
  `@` (symbol in this file), `#` (symbol in the workspace), or `:42` (go to line 42).

As you type, ThIDE re-checks the file and surfaces problems as coloured squiggles and in the
Diagnostics panel — see [Diagnostics & validation](09-diagnostics-and-validation.md).

## Step 5 — Compile

Run **Compile** (menu **Compile → Compile**, the toolbar hammer, or the palette). ThIDE:

1. Offers to save any unsaved files that are part of the build.
2. Runs Therion, streaming its output into the **Compiler Output** panel.
3. Makes every `file:line` message clickable so you can jump to the cause.
4. Lists everything Therion produced in the **Generated Files** panel.

If the build fails, the errors are right there and clickable. Full details:
[Compiling & output](10-compiling-and-output.md).

## Step 6 — Look at the result

Depending on what your thconfig exports:

- **2D maps** (PDF/SVG/PNG) open in the [Map Viewer](11-viewers.md#map-viewer).
- **3D models** (`.lox` / `.3d`) open in the [embedded 3D viewer](11-viewers.md#3d-viewer) — where you
  can click a station to jump back to its `.th` source — or externally in Loch/Aven.
- You don't even need to compile to eyeball the centrelines: the
  [Mainline Preview](11-viewers.md#mainline-preview) draws them straight from the model.

## Step 7 — Understand your survey

Open the **Overview** panel and browse the **Dashboard / Surveys / Statistics / Charts / Team /
Entrances / Quality / Audit** tabs for a live read-out of the cave. See
[Survey Overview & analytics](13-survey-overview-and-analytics.md). Track continuation with the
[Leads register](14-leads-notes-and-metadata.md).

## Where to go next

- Learn the vocabulary properly: [Core concepts & vocabulary](04-core-concepts.md).
- See the whole UI: [A tour of the interface](05-interface-tour.md).
- Follow end-to-end recipes: [Typical workflows](06-typical-workflows.md).

---

Next: [Core concepts & vocabulary →](04-core-concepts.md)
