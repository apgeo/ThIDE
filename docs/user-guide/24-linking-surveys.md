# 24. Linking a survey file into the project

> [← Back to the User Guide home](README.md) · [ThIDE project home](../../README.md)

A new trip usually arrives as a standalone `.th` file — exported from TopoDroid, typed up after the
trip, or sent by another team. To make it part of the cave it needs two lines in the project: an
`input` that brings the file in, and an `equate` that ties one of its stations to the station it
continues from. **Link survey file** writes both, in the right place and with the right station
names.

---

## Opening it

- **Tools → Link survey file…** (also in the command palette), or
- right-click in a `.th` editor → **Generate code → Link survey file…** — the lines then go into
  *that* file, in the survey block around the cursor.

Choose the survey file with **Browse…** (or type its path and press **Enter**). The line under the
path shows how many stations and shots it has and whether it parsed cleanly.

## Choosing the two stations

**Station in the new survey** (left): filter the list by typing, or click a station in the small
preview under it. The chosen station is ringed in the preview. **Open in a window…** shows that
preview full size; the **Preview** button folds it away.

**Station in the project** (right): pick a **Survey** to narrow the list (surveys are shown as an
indented tree, with the file each one lives in), then the **Station**. **Show in full preview…**
opens the whole project's Mainline Preview with the new survey already attached at the two
stations — its legs drawn in magenta and both stations ringed — so you can check that it lands
where it should before writing anything. The window follows the pickers while it is open.

## What gets written, and where

The **Generated lines** box shows exactly what will be inserted, for example:

```
input trips/2026-10-21.th
equate 1@trip_2026_10_21 14@north
```

Station names are written relative to the survey the lines go into (`point@inner.outer`, as
Therion expects), so they resolve however deeply the files are nested.

**Insert into** lists the files that can hold the lines, best first:

| Reason shown | Meaning |
|---|---|
| *equates already here* | the survey block that already holds the `equate`s for that level |
| *file of equates* | a file of bare `equate` lines that is input from inside that survey |
| *survey block* | the survey block itself, with no equates yet |
| *at the cursor* | the place you right-clicked (Generate code entry) |

Enclosing surveys follow, lower in the list. The new survey becomes a *sibling* of the survey the
project station belongs to, so the `equate` is written one level up. Tick **Insert into the current
file** to use the file open in the editor when it is one of the candidates.

In the chosen block, the `input` goes after the last `input` and the `equate` after the last
`equate` (or right after the `input` when there are none yet), with the block's indentation and
line endings. **Add a comment** puts `# Added on <date> from source file <name>` above them.

## Copying the file into the project

A survey file from outside the project folder is copied in by default (a project should not depend
on files outside its tree); one already inside is input where it is. The suggested folder is the
one most of that block's other `input` files live in — edit it or use **Browse…**.
The `.th2` sketches in the same folder are listed; the ones the survey file itself inputs are
ticked, and they keep their position relative to it, so the survey's own `input` lines stay valid.

## Checks before writing

The dialog warns, in orange, when the file is **already input** somewhere, when an **equate with
the same stations already exists**, or when a sketch the survey inputs is **not being copied**; you
may insert anyway. It refuses, in red, when a station cannot be named from the chosen block (pick
one higher up), when the block has no end line, or when a copy target already exists with
different content.

## Inserting

**Insert** writes the lines (and the copies) in one step: into the editor if the file is open — so
**Undo** takes them back — otherwise straight into the file, keeping its encoding. The project is
then re-read, so the Mainline Preview and the diagnostics include the new survey at once. A file
that is open with unsaved changes has to be saved first when an `input` is added. **Copy to
clipboard** gives you the lines without changing anything.

*Today the dialog links one station on each side; the underlying planner already accepts equates
of more stations, which a later version will offer.*
