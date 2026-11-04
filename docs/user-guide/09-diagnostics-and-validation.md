# 9. Diagnostics & validation

> [← Back to the User Guide home](README.md)

ThIDE checks your files continuously and reports problems as **diagnostics** — before you ever
compile. This is one of the biggest reasons to edit Therion in ThIDE rather than a plain text editor.

## Where diagnostics show up

- **In the editor:** colored squiggles under the offending text, with the message on hover.
- **In the Diagnostics panel** (**View → Diagnostics**): a filterable list of every problem.
- **In the status bar:** a running count, with **F8 / Shift+F8** to jump to the next/previous one.

## The Diagnostics panel

| Control | What it does |
|---|---|
| **Errors / Warnings / Info** toggles | Filter by severity. |
| **Workspace scope** | Show problems from *all* thconfig-connected files, not just the open one (needs a workspace). |
| **References only** | Show only dangling/unresolved reference problems (e.g. a missing `input`). |
| Columns **Code · Severity · Message** | Click a row to jump to the source location (Enter does the same from the keyboard). The message and the **Details** strip below the grid are shown in the language of the app. |
| **Explain / Open docs ↗** | Open the documentation for a diagnostic **Code**. |
| **Suppress This Code** | Stop reporting that code (project-wide); **Clear suppressions** to undo. |

## Severity & modes

ThIDE is **lenient by default**: many issues are *warnings* so you can keep working with
work-in-progress files. When you want a stricter pass:

- **View → Strict Parser Mode** promotes the "lenient" issues to **errors**. Use it for a pre-share
  clean-up.

Each diagnostic carries a **stable code** (e.g. `TH0033`, `TH_SEM_015`, `TH2_004`). The complete
catalogue — every code, its severity, message and source — is in the reference doc
**[docs/diagnostics.md](../diagnostics.md)**. A quick orientation:

| Family | Covers | Examples |
|---|---|---|
| `TH00xx` | Core parser (`.th` / `.thconfig`) | unknown command, malformed `fix`/`equate`/`data`, unknown coordinate system |
| `TH2_xxx` | `.th2` drawings | unknown point/line/area type, unterminated `line`/`area`/`scrap` |
| `TH_XVI_xxx` | `.xvi` sketches | unknown `set XVI…`, missing `-sketch` target |
| `TH_SEM_xxx` | Cross-file semantics | unresolved station, duplicate fix, loop misclosure, blunder, **disconnected survey** |
| `THIDE_DIR_xxx` | `#@` application directives | unmatched `#@region` / `#@endregion` |
| `TH_WS_xxx` | Workspace | path not found, no thconfig in folder |

## Validation that goes beyond one file

The `TH_SEM_*` family is where ThIDE's whole-project understanding pays off. It flags things a single
file can't reveal:

- **Unresolved station references** (with "did you mean" hints), validated across the whole project.
- **Duplicate fixes**, **data rows** whose column count or values don't fit the reading order.
- **Loop misclosure** beyond tolerance, **blunder/outlier** shots, **foresight/backsight**
  disagreement.
- **File not found** — an `input`/`source` target that isn't on disk. Clicking the warning jumps to
  the line that names it.
- **Disconnected survey** (`TH_SEM_015`) — a piece of the cave that is neither joined to the main
  network nor georeferenced by a `fix`, so it *floats*. Both end stations and the source files are
  named so you can find and stitch it.

## Validate as you type (optional)

By default the deep, whole-thconfig re-validation runs on **save**. Turn on **Validate on type**
(toolbar toggle, or [Settings → Editor](19-settings-and-preferences.md#editor)) to re-analyse the
whole file-tree on a short typing pause instead. It's off by default because it can be heavy on large
workspaces — leave it off if you notice lag and rely on save-time validation.

## Quick fixes

Where ThIDE can offer a repair, **Quick Fix** (**Ctrl+.**) proposes it (also in the Command Palette).
When there's nothing to fix at the caret, it says so. A repair that would remove data never stands
alone and says what it costs: for a zero-length leg (`TH_SEM_012`) the first proposal is an
`equate` of the two stations, and *Comment out* is labelled with the station it would disconnect.
An `export … -output` path written with `\` (`TH0059`) is fixed by rewriting the separators to `/`.

In the **Diagnostics panel**, right-click a row for **Fix** (its first fix) and, for problems with a fix
that is safe to apply everywhere, **Fix all '<code>' in the workspace…**: an unknown station with a
single "did you mean" suggestion (renamed) and a block without its terminator (`endsurvey` and the
like, inserted after the block's body). A preview lists every change — file, line, before and after —
and you untick any you don't want; **Apply** writes them and the project is re-checked. Ambiguous
cases are counted and left alone. In an open file the fix is an editor change; a file that is not
open is **rewritten on disk**, and the whole run is one entry of the [workspace
undo](05-interface-tour.md#undoing-a-tool-that-wrote-many-files) (**Edit → Undo Multi-File Change**).

## Project-wide tidying (Tools menu)

These tools change many files at once. Each one shows a preview first, and when you apply it the
files that are not open are written to disk — one **Edit → Undo Multi-File Change** (named after the
tool) puts them all back. A file that is open with unsaved edits takes the change in its editor and
stays unsaved.

- **Add Unreferenced Scraps to Maps…** — lists the drawn scraps no map composes (so they are missing
  from every exported map), proposes a map for each (one in the file that inputs the sketch, else the
  closest name), and adds them after the map's last member, written `scrap@survey` when needed.
- **Suggest Equates…** — stations closer than a distance you choose but in different, separately
  georeferenced pieces of the cave: likely missing `equate`s. Positions are estimates, so each pair is
  a candidate to check; **Link…** opens [Link Survey File](24-linking-surveys.md) on the two stations
  and writes only the `equate`, where both can be named from.
- **Format All Files…** — every project file through the formatter, previewed first (each changed
  file shows what changes, for example *1 line: trailing spaces removed*); files that do not parse
  cleanly are left alone, line endings are kept. With nothing to change the dialog says so
  (*Nothing to change in 91 files*).
- **Normalize Project…** — Format All Files plus the house-keeping a shared repository wants: one line-ending
  style (LF or CRLF), files that declare a legacy encoding (`encoding iso8859-2`, say) converted to
  UTF-8, and — if you tick it — each run of consecutive `input` lines sorted naturally (`trip2` before
  `trip10`). Previewed like Format All Files.
- **Renumber Stations…** — a new prefix (`1, 2, 3` → `A1, A2, A3`), a prefix replaced or removed, or the
  numbers shifted (`+100`, zero padding kept) for every station of one survey, optionally its
  sub-surveys too. Stations whose names do not fit (no number to shift, a different prefix) are left
  alone and counted in the summary. Every place the station is written changes together — shots,
  `equate`, `fix`, `station`, and `@`-references in other files. The preview lists the changed lines
  as they will read afterwards, and the tool refuses a plan that would give two stations one name or
  reuse a name that stays. The preview is all or nothing. Station labels in sketches (`.th2` `-name`)
  are not renamed: check them afterwards.

## Tuning specific checks

A few checks are configurable on the **Visualization** page of Settings (**Settings → Visualization**,
its *Diagnostics* group) — for example, whether a bare local `fix` (no coordinate system) counts as
"grounding" a disconnected piece and thus suppresses `TH_SEM_015`. See
[Settings → Visualization](19-settings-and-preferences.md#visualization).

---

Next: [Compiling & output →](10-compiling-and-output.md)
