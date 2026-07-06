# 16. The Relational Map

> [← Back to the User Guide home](README.md)

The **Relational Map** is an interactive diagram of how your project's objects relate — surveys,
scraps and maps, and (optionally) the files that host them. Where the [Object Browser](12-object-browser-and-outline.md)
gives you tables, this gives you the *shape* of the project. Open it from **View → Relational Map…**
(or the toolbar).

## What it shows

- **Nodes** for each **survey**, **map** and **scrap** (colour-coded by kind).
- **Links** for the relationships between them (a map aggregating scraps, a survey including another,
  and so on).
- Turn on **Include files** to add the host `.thconfig` / `.th` / `.th2` files as nodes too — useful
  for seeing which file defines what.

## Interacting

| Do this | To… |
|---|---|
| **Drag** a node | Rearrange the diagram by hand. |
| **Double-click** a node | Open its definition in the editor. |
| **Hover** a link | See what the relation is. |
| **Double-click** a link | Jump to where that relation is defined. |
| **Ctrl+scroll** / zoom buttons | Zoom in/out; **Reset zoom** / **Fit** to re-frame. |

## Layout options

- **Layout** picks the diagram arrangement.
- **Size by level** makes nodes near the root bigger and leaves smaller — this scales better to large
  workspaces, where a flat layout gets crowded.
- **Refresh** rebuilds the diagram from the current workspace after you've edited files.

## When it's useful

- Understanding an **unfamiliar project** you've just opened.
- Spotting a **scrap that no map includes**, or a **map with no scraps** — though the
  [Overview → Audit](13-survey-overview-and-analytics.md#the-audit-tab) tab reports those explicitly.
- Explaining the project structure to a collaborator.

---

Next: [Import, export & GIS →](17-import-export-and-gis.md)
