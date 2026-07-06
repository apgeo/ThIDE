# 8. Navigation & search

> [← Back to the User Guide home](README.md)

Because ThIDE understands your whole project (the [object graph](04-core-concepts.md#the-object-graph-the-semantic-model)),
it can move you around by *meaning*, not just text. This is where a lot of the time savings live.

## The Command Palette

**Ctrl+Shift+P** opens the Command Palette — a single search box for everything. Start typing a
command name (Compile, Toggle Diagnostics, Format Document, Switch to Romanian…). It's grouped by
category (File, Build, View, Go, Search, Edit, Editor, Settings, Docs, …).

It also doubles as a jump box via **prefixes**:

| Type | Does |
|---|---|
| *(text)* | Fuzzy-match a command to run |
| `@name` | Go to a symbol **in the current document** |
| `#name` | Go to a symbol **in the whole workspace** (surveys, stations, scraps, maps) |
| `:42` | Go to **line 42** of the current file |

## Opening & switching files

| Action | Shortcut | Notes |
|---|---|---|
| **Go to File** | **Ctrl+P** | Fuzzy file open. Scope is configurable: only files connected to the active thconfig, or every Therion file in the workspace ([Settings → Workspace](19-settings-and-preferences.md#workspace)). |
| Switch open documents | **Ctrl+Tab** | Cycles the open tabs. |
| **Reopen Closed Tab** | — | File menu; brings back the last closed document. |
| Reveal active file in the tree | — | Command Palette → *Reveal Active File in Workspace*. |

## Go to symbol

- **Go to Symbol in Document** and **Go to Symbol in Workspace** (toolbar buttons, Command Palette, or
  the `@` / `#` prefixes). Searches surveys, stations, scraps and maps.
- Very large caves: the number of stations listed is capped for responsiveness
  ([Settings → Performance](19-settings-and-preferences.md#performance)).

## Jump by meaning

From an identifier in the editor (hover card or right-click):

| Action | Shortcut | What it does |
|---|---|---|
| **Go to Definition** | click / Ctrl+click / double-click* | Jump to where a station/survey/file is declared. |
| **Go to Equate** | — | Follow an `equate` to the stitched station. |
| **Go to Aggregating Map** | — | From a scrap/map, jump to the map that includes it. |
| **Find All References** | — | List every use across the project. |
| **Peek Definition** | **Alt+F12** | See the definition inline without leaving your spot. |
| **Go to Matching Block** | **Ctrl+]** | Between a block opener and its `end…`. |
| **Step Into Included File** | **Alt+↓** | Open the file an `input` line points to. |

\* Whether a single click navigates or just places the caret is a preference
(**require double-click to go to definition**) — Ctrl+click always navigates. See
[Settings → Editor](19-settings-and-preferences.md#editor).

## Caret history: Back & Forward

**Alt+Left** / **Alt+Right** (also toolbar arrows) walk your caret/navigation history — jump to a
definition, then hop straight back to where you were, across files.

## Rename a symbol everywhere

**F2** (or *Rename Symbol* in the hover card / palette) renames a station/survey **across every file
that references it**. If enabled, a **preview** first lists all occurrences (count across N files),
which you can double-click to inspect before applying. Turn the preview on/off under
[Settings → Editor](19-settings-and-preferences.md#editor). Stations can also be renamed from the
Measurements grid (right-click → *Rename Station…*).

## Find & replace

- **In the current file:** **Find** and **Replace** (Search menu) — with match-case, whole-word and
  regex options.
- **Across files:** **Find in Files** (**Ctrl+Shift+F**) and **Replace in Files** — choose a
  **directory** and a filename **mask**, with the same match options. A cross-file **Replace All**
  can be **undone/redone** as a single operation (buttons in the dialog).

## Stepping through diagnostics

With the **Diagnostics** panel, **F8** / **Shift+F8** jump to the next/previous problem — a quick way
to sweep a file. See [Diagnostics & validation](09-diagnostics-and-validation.md).

---

Next: [Diagnostics & validation →](09-diagnostics-and-validation.md)
