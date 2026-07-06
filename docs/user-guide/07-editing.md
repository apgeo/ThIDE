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
| **Insert Today's Date** | Drop today's date (handy for `date`). |
| **Insert Team Member** | Insert a `team` member from your project. |

## Formatting

- **Format Document** — **Shift+Alt+F** re-indents the file to its block nesting.
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
