# 7. The editor

> [← Back to the User Guide home](README.md)

ThIDE's editor is built on AvaloniaEdit and tuned for Therion. This page covers the writing
experience; jumping *around* code is in [Navigation & search](08-navigation-and-search.md).

## Syntax highlighting

All four file types are colour-coded (keywords, identifiers, numbers, strings, comments, options,
punctuation). Inside `layout … endlayout` blocks, the embedded **MetaPost** and **TeX** code is
highlighted with their own lexers (see [docs/layout-and-embedded-code.md](../layout-and-embedded-code.md)).
You can recolour everything under **Settings → Theme & Colors → Use custom syntax colors**, or just
switch to the **Dark** theme, which adapts both the UI and the syntax palette.

## Typing help

- **Autocomplete** — context-aware suggestions as you type (commands, options, known stations).
- **Hover cards** — hover a station/survey/file reference for a summary and quick actions
  (go to definition, find references, rename, documentation, show in 3D, open).
- **Bracket/quote auto-pairing** and automatic indentation.

## Snippets and new files

Type a snippet's trigger at the start of a line and press **Tab** (or pick it from autocomplete, or
from **Generate code ▸ Insert snippet** in the right-click menu). The snippet is inserted with its
**fields** highlighted: type to fill the first, **Tab** / **Shift+Tab** to move between them, and
**Enter** or **Esc** when done. A field used twice changes in both places.

| Trigger | Inserts |
|---|---|
| `survb` | a `survey … endsurvey` block with today's date |
| `clt` | a `centreline` with today's date, and the team and data line of the last trip above the cursor |
| `leg` | the next leg: from the last station to the next number (`A07` → `A08`) |
| `eq`, `inp` | an `equate`, an `input` |
| `scrapb`, `mapb` | `scrap` (choose the projection) and `map` blocks |
| `thconf`, `layout-plan-500`, `layout-copy`, `export-plan-pdf`, `export-model-lox` | thconfig skeleton and fragments (offered in thconfig files) |

**Your own snippets** go in `snippets.json` — in ThIDE's settings folder for yourself, or in the
project's `.thide/` folder to share them with everyone who has the project (where project files
live is a choice in **Preferences ▸ General**). It is a list of entries:

```json
[
  { "trigger": "fixutm", "description": "fix in UTM 34N", "scope": "th",
    "template": "fix ${1:station} ${2:easting} ${3:northing} ${4:altitude}$0" }
]
```

Templates use the VS Code snippet syntax: `$1`, `${1:default}`, `${1|plan,elevation|}` (a choice),
`$0` (where the cursor ends), and variables — `$DATE`, `$TIME`, `$YEAR`, `$FILE_NAME`,
`$FILE_BASENAME`, `$CURRENT_SURVEY`, `$PARENT_SURVEY`, `$TEAM_RECENT`, `$LAST_UNITS`,
`$LAST_CALIBRATE`, `$LAST_INSTRUMENT`, `$LAST_DATA`, `$LAST_STATION`, `$NEXT_STATION`, or
`${NAME:fallback}`. Write `\$` for a literal dollar. `scope` is `th`, `th2`, `thconfig` or `any`. A
broken file is reported once and the rest of your snippets still work.

**Quick Export ▸ Save as fragment…** saves the export block you composed as a thconfig snippet in the
project's file.

**File ▸ New File…** (also on the Welcome page, in the command palette and in the Workspace
Explorer's right-click menu) creates a file from a template — survey trip, connection survey,
thconfig, th2 sketch, layout library or blank — previews it, and can add the `input` line to a survey
file in the same folder. Your own templates are JSON files in `templates/files/` (settings folder or
the project's `.thide/`), each `{ "id", "title", "description", "extension", "defaultName", "body" }`
with a body in the same template syntax.

## Folding

- Therion blocks (`survey…endsurvey`, `scrap…endscrap`, `centreline…`, `map…`, `layout…`) fold.
- **`#@region … #@endregion`** blocks fold too — an application-directive layer that lives *inside*
  Therion comments (so Therion ignores it). Select lines and run **Enclose in Region**
  (**Ctrl+Shift+R**, also in the Edit menu) to wrap them with an optional title. Full reference:
  [Application directives (`#@`)](../directives.md).
- **Edit → Fold All / Unfold All** collapse or expand everything at once.

## Comments & case

- **Toggle Comment** — **Ctrl+/** on the line or selection.
- **UPPERCASE** / **lowercase** the selection (Edit menu or the Command Palette).

## Line operations

From the editor context menu (or the Command Palette):

| Action | What it does |
|---|---|
| **Duplicate Line(s)** | Copy the current line/selection below itself. |
| **Move Line(s) Up / Down** | Shift lines without cut-and-paste. |
| **Sort Selected Lines** | Alphabetically sort the selected lines. |
| **Paste as shot data…** | Turn a table of shots (spreadsheet, CSV, DistoX) into a `data` block — see [Import](17-import-export-and-gis.md#shot-tables-spreadsheets-csv-pockettopo-distox). |
| **Insert Today's Date** | Drop today's date (handy for `date`). |
| **Insert Team Member** | Insert a `team` member from your project. |

## Formatting

- **Format Document** — **Shift+Alt+F** re-indents the file to its block nesting. It never reorders or
  drops a line; `therion-cli format --check` applies the same rule in CI.
- **Format on save** — enable in **Settings → Editor** to auto-format every save.

There's also a **region directive formatter** so *Enclose in Region* produces clean, correctly-quoted
directive lines.

## Bookmarks

- **Edit → Add Bookmark…** marks the current line (with an optional title).
- **Search → Bookmarks…** lists them all; click **Go to** to jump. Great for parking spots in a long
  file.

## Whitespace & display

**View** menu toggles: **Show Spaces & Tabs**, **Show End of Line**, **Show Indentation Guides**,
**Show Minimap**, plus **word wrap** (toolbar). A **minimap** gives a bird's-eye scroll for long
files.

## The Measurements tab

Every `.th`/`.th2` document has a **Measurements** sub-tab beside **Source**: a live grid of that
file's shots and stations parsed from the object graph.

- **Filter** by from/to/survey/flags/comment (shots) or name/survey/kind (stations).
- **Group by** survey/other; toggle columns; mark **Surf / Dup / Splay / Approx**.
- Right-click a row: **Go to Declaration** (of From/To), **Show in 3D View**, **Rename Station…**.
- Double-click to jump to the source line.

It's the fastest way to sanity-check freshly-entered data without leaving the file.

## Working with included files

Therion projects are trees of `input`/`source` includes. In the editor:

- **Step Into Included File** — **Alt+↓** on an `input` line opens the target.
- The file header offers **Go to the parent file** (the file that includes this one).
- **Go to Matching Block** — **Ctrl+]** jumps between a block opener and its `end…`.

## Handing a scrap to Mapiah

For `.th2` files, the header has **Edit with Mapiah** — it launches the sketch in
[Mapiah](17-import-export-and-gis.md#drawing-with-mapiah) and auto-reloads when you save there.

## Editor behaviour options

Under **Settings → Editor** you can tune: font & indent size, line numbers, current-line highlight,
convert-tabs-to-spaces, **require double-click to go to definition** (vs single-click hyperlink),
**validate on type**, rename preview, and format-on-save. Individual editor enhancements can also be
switched off there. See [Settings](19-settings-and-preferences.md#editor).

---

Next: [Navigation & search →](08-navigation-and-search.md)
