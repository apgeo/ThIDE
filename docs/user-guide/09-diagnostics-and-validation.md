# 9. Diagnostics & validation

> [← Back to the User Guide home](README.md)

ThIDE checks your files continuously and reports problems as **diagnostics** — before you ever
compile. This is one of the biggest reasons to edit Therion in ThIDE rather than a plain text editor.

## Where diagnostics show up

- **In the editor:** coloured squiggles under the offending text, with the message on hover.
- **In the Diagnostics panel** (**View → Diagnostics**): a filterable list of every problem.
- **In the status bar:** a running count, with **F8 / Shift+F8** to jump to the next/previous one.

## The Diagnostics panel

| Control | What it does |
|---|---|
| **Errors / Warnings / Info** toggles | Filter by severity. |
| **Workspace scope** | Show problems from *all* thconfig-connected files, not just the open one (needs a workspace). |
| **References only** | Show only dangling/unresolved reference problems (e.g. a missing `input`). |
| Columns **Code · Severity · Message** | Click a row to jump to the source location. |
| **Explain / Open docs ↗** | Open the documentation for a diagnostic **Code**. |
| **Suppress this code** | Stop reporting that code (project-wide); **Clear suppressions** to undo. |

## Severity & modes

ThIDE is **lenient by default**: many issues are *warnings* so you can keep working with
work-in-progress files. When you want a stricter pass:

- **View → Strict parser mode** promotes the "lenient" issues to **errors**. Use it for a pre-share
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
When there's nothing to fix at the caret, it says so.

## Tuning specific checks

A few checks are configurable under **Settings → Diagnostics** — for example, whether a bare local
`fix` (no coordinate system) counts as "grounding" a disconnected piece and thus suppresses
`TH_SEM_015`. See [Settings → Diagnostics](19-settings-and-preferences.md#diagnostics).

---

Next: [Compiling & output →](10-compiling-and-output.md)
