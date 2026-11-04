# 22. Troubleshooting & FAQ

> [← Back to the User Guide home](README.md)

Common questions and fixes. If something here doesn't match what you see, the app is in alpha and may
have moved — see [About this guide](about-this-guide.md) for how to report or fix it. For a machine
readout of your setup (versions, detected tools, paths), use **Help → Debug Info** and **Copy to
clipboard**.

## Setup & tools

**"Therion was not found" when I compile.**
ThIDE doesn't bundle Therion. Install it, then set its path in
**Settings → External Tools** and press **Test**. See [Installation](02-installation-and-setup.md).

**The 3D viewer is blank or says "unavailable".**
1. Enable it: **Settings → Visualization → Embedded 3D model viewer**.
2. Make sure a web engine is present — Windows: WebView2 (built into Windows 11 / modern Edge);
   Linux: `apt install libwebkit2gtk-4.1-0`; macOS: built in.
3. If it still can't render, use the pane's **Open externally (Loch / Aven)** button. Reference:
   [docs/3d-viewer.md](../3d-viewer.md).

**"Mapiah not found."**
Install Mapiah and set the `mapiah` path in **Settings → External Tools** (the dialog links there).

**Loch / Aven don't open.**
Set their paths in **Settings → External Tools**; ThIDE checks common install locations but can't
guess a custom one.

## Compiling

**The compilation fails with a missing-directory error.**
Keep **Create missing output folders before compiling** on
([Settings → Compile & Output](19-settings-and-preferences.md#compile--output)).

**It keeps asking to save before compiling.**
That's the unsaved-files prompt. Turn on **Save modified files automatically before compiling** to
skip it, or just choose **Save & compile**.

**My outputs look old / a "stale" banner appeared.**
An input is newer than the outputs (this survives a restart, because it goes by file times), or the
last compilation failed. **Compile → Recompile**. The
[Generated Files](10-compiling-and-output.md#the-results-the-generated-files-panel) panel flags stale
outputs.

## Numbers & the model

**ThIDE's length/depth doesn't match Therion's.**
Expected. In-app analytics and previews are **preview-quality** (no loop adjustment) for instant
feedback. **Therion is the source of truth** for adjusted figures — compile for the real numbers.
See [Core concepts](04-core-concepts.md#the-object-graph-the-semantic-model).

**"Disconnected survey" warning (TH_SEM_015) on data I think is fine.**
The piece isn't joined to the main network *and* isn't georeferenced. Either add the missing `equate`
to stitch it, or `fix` it. If it's grounded only by a bare `fix` (no coordinate system) and you're OK
with that, enable **Treat a local fix (no cs) as grounding a disconnected survey** in
[Settings → Visualization](19-settings-and-preferences.md#visualization) (its *Diagnostics* group). Color the
[Mainline Preview](11-viewers.md#mainline-preview) **by Component** to see the floating piece.

**The declination calculator says "No magnetic model loaded".**
Drop a `WMM.COF` (NOAA, public domain) in ThIDE's settings folder (the panel names the exact path),
or use **Load model…**. See
[Calculators](18-calculators.md#declination-calculator).

## Performance

**Everything is slow on a big project.**
- Turn off previews/analytics you aren't using
  ([Settings → Visualization](19-settings-and-preferences.md#visualization), including its *Survey
  analytics* group).
- Leave **Validate on type** and **Compile on save** off.
- Lower the **Performance** limits and the **Startup load timeout**
  ([Settings → Performance](19-settings-and-preferences.md#performance)).
- Narrow **Ctrl+P** scope to the active thconfig
  ([Settings → Workspace](19-settings-and-preferences.md#workspace)).

**A single huge file isn't highlighted or parsed.**
It exceeded a size guard. Use the in-file **Highlight anyway / Parse anyway** banner, or raise the
limits in Settings → Performance.

## Reliability & safety (what to do when things go wrong)

ThIDE tries hard not to lose your work:

- **Crash recovery / safe mode.** If the app closed unexpectedly with unsaved changes, on next launch
  a **recovery banner** is pinned above the editor, naming the files whose unsaved changes were kept,
  with **Restore** to bring those buffers back and **Discard copies** to drop them (it asks before it
  deletes them). It stays until you answer, so it cannot be missed on a small screen.
- **External-change banner.** If a file you have open changes on disk (another program, a `git`
  operation, Mapiah), ThIDE shows a banner: **Reload** (take the disk version), **Compare**
  (side-by-side diff), **Keep mine (save)** (write your version over the disk one) or **Dismiss**.
  Reloading a file with unsaved edits asks first (*Reloading will discard your unsaved changes*). It
  never silently overwrites your edits. A change that only touches the file's permissions raises no
  banner. (Auto-reload
  of externally-changed files is a [Settings → Performance](19-settings-and-preferences.md#performance)
  option.)
- **Delete confirmation.** Deleting a file from the Workspace Explorer confirms first (trash vs.
  permanent).
- **Multi-file tools can be undone.** Rename, Replace in Files, New Trip, Link Survey File, Renumber,
  Format All, Normalize and *Fix all* write files that are not open straight to disk; **Edit → Undo
  Multi-File Change** puts them all back ([details](05-interface-tour.md#undoing-a-tool-that-wrote-many-files)).
- **Open guards.** Binary or oversized files prompt before opening as text (**Open anyway**), so you
  don't accidentally load a huge blob.

## Layout & UI

**A panel disappeared / my layout is a mess.**
**View → Layout → Restore Default Layout**. Toggle any individual panel from the **View** menu or the Command
Palette. Unsaved edits are written to crash recovery before the layout is rebuilt.

**I closed a tab by accident.**
**File → Reopen Closed Tab**.

**Where are my settings stored?**
In ThIDE's settings folder (`%AppData%\ThIDE` on Windows, `~/.config/ThIDE` on Linux) — preferences, keybindings,
symbol index, crash-recovery buffers, plugins, and per-project sidecars. See
[Installation](02-installation-and-setup.md#6-where-thide-stores-your-settings).

## Still stuck?

- Check the **Log** panel (**View → Log**) for background errors.
- Grab **Help → Debug Info** and include it in any bug report.
- Consult the reference docs in [`docs/`](../README.md) and the bundled **Therion Book**
  (**Help → Therion Book**).

---

[← Back to the User Guide home](README.md)
