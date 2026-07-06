# 19. Settings & preferences

> [← Back to the User Guide home](README.md)

Open **Settings** from the menu bar (or *Preferences…* in the Command Palette). The window has a
**search box** (*Search settings…*) — type to jump to any option. Sections down the side group the
options; this page walks through each. **Reset all to defaults** is at the bottom.

Most performance-sensitive features have an explicit on/off switch here, so you can tune ThIDE for a
small tidy project or a huge multi-cave system.

## General

| Setting | Notes |
|---|---|
| **Reopen files from last session on startup** | Restore your open tabs. |
| **Show the welcome page on startup** | The [Welcome](03-getting-started.md) start page. |
| **Language** | English / Română, applied immediately. |
| **Auto-save** | Off · **After a delay** (set seconds) · **On focus loss**. |
| **Record anonymous usage & crash reports** | **Off by default.** When on, events are written under `%AppData%/ThIDE/telemetry` and **never leave your machine** — it's local-only. |

## Theme & Colors

- **App theme:** System default · Light · **Dark** (adapts both controls and editor syntax colours).
- **Use custom syntax colors** — override the palette with your own `#RRGGBB` values for keyword,
  identifier, number, string, comment, option, punctuation (each with a live swatch).

## Editor

| Setting | Notes |
|---|---|
| **Font size**, **Indent size** | Editor text and indentation. |
| **Show line numbers**, **Highlight current line** | Display aids. |
| **Convert tabs to spaces** | Indentation style. |
| **Format document on save** | Re-indent to block nesting on every save. |
| **Show a preview before applying a symbol rename** | The F2 rename confirmation list. |
| **Validate on type (file + whole thconfig file-tree)** | Deep re-validation on a typing pause, not just on save. **Off by default** — can be heavy on big workspaces. |
| **Require a double-click to go to definition** | On: single click places the caret, double-click navigates. Off: single click navigates like a hyperlink. Ctrl+click always navigates. |
| **Editor Features** | Enable/disable individual editor enhancements (a greyed one was switched off at build time). |

## Performance

Guards that keep large caves responsive:

| Setting | Effect |
|---|---|
| **Max highlight lines / size (KB)** | Above this, syntax highlighting & hover are disabled for the file. |
| **Max parse lines / size (KB)** | Above this, a file isn't parsed and stays out of the object graph. |
| **Max stations in search** | Caps how many stations *Go to Symbol* lists. |
| **Startup load timeout (seconds)** | Budget for reopening last-session files; the rest are skipped (and logged). `0` = no limit. |
| **Reload open files changed outside the editor** | Auto-pick-up external edits. |
| **Update the object graph when files change on disk** | Keep analysis current with on-disk changes. |

Individual files that hit a limit show a banner with **Parse anyway / Highlight anyway** so you can
override case by case.

## Workspace

- **Go to File (Ctrl+P): search only files connected to the active thconfig** — when off, Ctrl+P
  searches *every* Therion file in the workspace folder (history is always included). See
  [Navigation & search](08-navigation-and-search.md#opening--switching-files).

## Build & Output

| Setting | Notes |
|---|---|
| **After a successful build, open:** `.lox` / `.3d` / `.pdf` | What auto-opens after a compile; **Open every matching output** vs just the first. |
| **Compile on save** | Debounced background rebuild of the active thconfig on save. Off by default. |
| **Create missing output folders before compiling** | Avoids Therion failing on an absent directory. On by default. |
| **Save modified files automatically before compiling** | Skip the save prompt. Off by default. |
| **Recompute the Leads register as you type** | Live [Leads](14-leads-notes-and-metadata.md) refresh from unsaved buffers. On by default; turn off on huge workspaces. |

## Visualization

Enable/disable the read-only preview panels (disabling one hides its panel and menu):

- **Mainline centreline preview** (+ it's the plan/elevation sketch).
- **In-app map viewer (PNG / SVG / PDF)** — plus **Auto-show the rendered map after a build** and
  **Auto-show first map on workspace load**.
- **Embedded 3D model viewer** (CaveView.js) — plus **Auto-show the 3D model after a build**.
- **Structural Geology module**.
- **Open clicked PDFs in the in-app viewer**.

See [The viewers](11-viewers.md) and [Structural geology](15-structural-geology.md).

## Survey analytics

Turn off any of these on very large workspaces to skip work on each graph rebuild:

- **Statistics, charts, team, entrances & data-quality** (the [Overview](13-survey-overview-and-analytics.md) tabs).
- **Object Browser entity tabs** (surveys, fixes, equates, scraps, maps, points, lines, areas).
- **TODO / FIXME / QM comment scan**.
- **Media file scan** (referenced `.xvi` + on-disk orphans).

## Diagnostics

- **Treat a local fix (no cs) as grounding a disconnected survey** — when **off** (default), a piece
  anchored only by a bare `fix 0 0 0` (no coordinate system) is still warned about (`TH_SEM_015`);
  turn on to treat any fix as grounding and suppress that warning. See
  [Diagnostics](09-diagnostics-and-validation.md).

## External Tools

Where you point ThIDE at Therion, Loch, Aven, Mapiah, Survex. Each row shows **Tool · Detected ·
Version · Source · Override · Result**. Set an **Override** path and **Test** it. See
[Installation](02-installation-and-setup.md#4-point-thide-at-your-external-tools).

## Extensions

Power-user hooks (off by default; see [Extensibility](20-extensibility.md)):

- **Enable script hooks** — run a command **on open / save / build** (use `{file}` for the path).
- **Load plugins** — custom semantic rules (`ISemanticRule` DLLs) from `%AppData%/ThIDE/plugins`,
  loaded at startup.
- A note that the **CLI** (`therion-cli`) and **LSP** (`therion-lsp`) are separate executables.

## Keyboard Shortcuts

Rebind any command: click its **Gesture** field and press the key combination. Columns show
**Command · Gesture · Default**; **Reset** a row or all. The current defaults are listed in
[Keyboard shortcuts](21-keyboard-shortcuts.md).

## File Associations

Make Therion file types open in ThIDE (per-user, no admin). **Associate** / **Remove** per type, or
**Associate all**. Platform notes on the page and in
[docs/file-association.md](../file-association.md).

---

Next: [Extensibility (CLI, LSP, plugins, hooks) →](20-extensibility.md)
