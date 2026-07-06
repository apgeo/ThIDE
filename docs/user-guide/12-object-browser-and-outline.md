# 12. Object Browser & Outline

> [← Back to the User Guide home](README.md)

These two panels turn your project's [object graph](04-core-concepts.md#the-object-graph-the-semantic-model)
into browsable tables and a structural tree — a great way to *find* things and audit what's in the
cave.

## The Outline

**View → Outline** shows the **survey/scrap structure of the current file** as a collapsible tree:
surveys within surveys, scraps, and (optionally) the stations inside them.

- **Filter outline…** narrows the tree as you type.
- **Include stations in the outline** adds station leaves under each centreline.
- Click an item to jump to it in the editor.

If a file has no survey/scrap structure, the outline says so. It's per-file and lightweight — think
of it as the table of contents for the document you're editing.

## The Object Browser

**View → Object Browser** is project-wide. It presents the whole cave as **tables**, one per entity
type, on tabs:

| Tab | Lists | Typical columns |
|---|---|---|
| **Stations** | Every station | Qualified name, Kind, Survey, Line |
| **Shots** | Every leg/splay | From, To, Length, Compass, Clino, Survey |
| **Fixes** | Georeferenced stations | Station, Coordinates, CRS |
| **Equates** | Stitched station pairs | The equated names |
| **Maps** | `map` constructs | Name, members |
| **Scraps** | `.th2` scraps | Name, projection, georef |
| **Points / Lines / Areas** | `.th2` drawing objects | Type, location |

Common to every table:

- **Filter** to narrow rows; **Columns ▾** to show/hide columns; **Fit** to auto-size to content.
- Right-click a row: **Go to Source / Go to Definition**, **Find References**, **Rename Symbol…**,
  **Copy Qualified Name**, and copy helpers (cell / row / all, plain or formatted).
- Double-click to jump to the source declaration.
- **Export CSV…** to take a table out of the app.

Because these tables come from the live model, they're the quickest way to answer "does this station
exist / how many splays are in this survey / which scraps aren't georeferenced".

> The Object Browser entity tabs are part of **Survey analytics** and can be turned off on very large
> workspaces — see [Settings → Survey analytics](19-settings-and-preferences.md#survey-analytics).

## The XVI panel

**XVI** lists the sketch (`.xvi`) files your scraps reference, with their **grid** and the **sketch**
they belong to — useful when wiring `.th2` drawings to their scanned/traced backgrounds. See also
[Import, export & GIS](17-import-export-and-gis.md).

## Measurements grid (recap)

Each open `.th`/`.th2` file also has its own **Measurements** tab (shots + stations for *that* file),
described in [The editor](07-editing.md#the-measurements-tab). Use the Object Browser for the whole
project; use the Measurements tab for the file in front of you.

---

Next: [Survey Overview & analytics →](13-survey-overview-and-analytics.md)
