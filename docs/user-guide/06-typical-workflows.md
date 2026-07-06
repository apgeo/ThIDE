# 6. Typical workflows

> [← Back to the User Guide home](README.md)

Recipes that string the features together the way you actually work. Each links to the detailed page
for the steps involved.

## A. Continue an existing project

1. **File → Open Folder…** (or Open thconfig) → pick your project.
2. If prompted or if several configs exist, set the **active thconfig**
   ([Core concepts](04-core-concepts.md#the-active-thconfig)).
3. Let it **index** (status bar), then skim the **Diagnostics** panel for anything already broken.
4. Edit — lean on **F2 rename**, **go to definition**, and the **Command Palette** (Ctrl+Shift+P).
5. **Compile** (Compile → Compile), fix any clickable errors, repeat.
6. Review the result in the **Map / 3D viewers**; check numbers in the **Overview** panel.

## B. Enter data from a fresh trip

1. Open the relevant survey `.th` (or make a new file: Workspace Explorer right-click → **New File**).
2. Type your `centreline` / `data` / shots. As you type:
   - Autocomplete and hover help guide the syntax.
   - The **Measurements** tab shows the parsed shots so you can eyeball them.
   - Diagnostics flag bad readings (non-numeric, out of range), duplicate fixes, unknown stations.
3. Use **Insert Today's Date** and **Insert Team Member** (editor context menu) for `date`/`team`
   lines.
4. Wrap the trip in a named, collapsible block with **Enclose in Region** (Ctrl+Shift+R) if you like
   — see [directives](../directives.md).
5. Check the trip joins the rest of the cave: open **Mainline Preview**, colour **by Component**; a
   piece stacked at the origin is disconnected — add the missing `equate`.

## C. Validate & clean up before sharing

1. Turn on **View → Strict parser mode** for a stricter pass.
2. Work the **Diagnostics** panel top to bottom: click a row to jump to it; use **Quick Fix**
   (Ctrl+.) where offered; **Suppress this code** for intentional patterns.
3. Watch for the workspace-level checks: disconnected surveys (TH_SEM_015), loop misclosure,
   blunders, dangling includes — see [Diagnostics](09-diagnostics-and-validation.md).
4. **Format Document** (Shift+Alt+F) to re-indent to block nesting; optionally enable *Format on save*.
5. Use the **Overview → Audit** tab to find orphan files, unreferenced scraps and unexported maps.

## D. Compile and export deliverables

1. Make sure the **active thconfig** exports what you want (`export map`, `export model`, …).
2. **Compile**. Watch **Compiler Output**; open **Generated Files** to see the artifacts.
3. Per-output actions in Generated Files: open externally, **view in internal 3D viewer**, reveal in
   file manager, go to the `export` line, or set a per-file **Auto-open** override.
4. Need a one-off that isn't in the thconfig? **Compile → Quick Export…** composes a temporary
   config, builds it, and opens the result. See [Compiling & output](10-compiling-and-output.md).

## E. Draw / edit passage art

1. Scaffold a scrap: **Tools → New scrap (.th2)…** (blank, or from an image / `.xvi` sketch).
2. Edit the `.th2` in ThIDE's editor, or hand it to **Mapiah** with **Edit with Mapiah** in the
   file header — changes reload automatically when you save there.
3. Cross-check with **Object Browser → Scraps / Points / Lines / Areas** and the **XVI** panel.

## F. Import from another program

1. **Tools → Import survey (Survex / Compass)…**, or **Import DEM as surface (.asc)…**, or
   **Import GPX waypoints (→ fix)…**.
2. ThIDE converts to Therion constructs (a `.th`, a `surface`, or `fix` lines).
3. Wire the new file into your thconfig and compile. See [Import, export & GIS](17-import-export-and-gis.md).

## G. Track exploration leads

1. Mark leads in your `.th` as you normally would (continuation flags, `QM`/lead/`?` comments,
   dead-ends).
2. Open **Overview → Leads**: ThIDE aggregates them into a register with status (Open / Checked /
   Pushed / Dead) and can overlay them on the preview.
3. Full lifecycle and the TODO/QM aggregator: [Leads, TODOs & metadata](14-leads-notes-and-metadata.md).

## H. Coordinator's health check of a big project

1. Open the whole folder; set the active thconfig.
2. **Overview → Dashboard** for length/depth/last-build at a glance; **Statistics / Charts** for the
   breakdown; **Team / Entrances / Quality** for people, ways in, and data problems.
3. **Overview → Audit** (click **Calculate**) for structural gaps.
4. **Relational Map** (View menu) to *see* how surveys, scraps and maps connect.
5. If it's sluggish, tune **Settings → Performance** and disable heavy previews you're not using —
   [Settings](19-settings-and-preferences.md).

---

Next: [The editor →](07-editing.md)
