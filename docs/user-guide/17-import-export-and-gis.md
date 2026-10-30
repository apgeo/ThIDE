# 17. Import, export & GIS

> [← Back to the User Guide home](README.md)

ThIDE bridges Therion and the other formats in a caver's toolbox — other survey programs, GIS, GPS,
and terrain data. Most of these live in the **Tools** menu.

## Import into Therion

**Tools → Import…** converts external data into Therion constructs you can then wire into your
project:

| Import | Source format | Becomes |
|---|---|---|
| **Import survey (Survex / Compass)…** | `.svx` (Survex), `.dat` (Compass), `.csv`/`.tsv` (a shot table) | A Therion `.th` survey |
| **Import DEM as surface (.asc)…** | ESRI ASCII grid | A Therion `surface` block |
| **Import GPX waypoints (→ fix)…** | `.gpx` | `fix` lines (georeferenced stations) |

After importing, add the new file to your thconfig (`input`/`source`) and compile. If an import
fails, a toast notification tells you why.

### Shot tables (spreadsheets, CSV, PocketTopo, DistoX)

**Edit → Paste as shot data…** (also in the editor's right-click menu) turns a table of shots into a
Therion `data` block: the selection when it is one, otherwise the clipboard. The dialog shows the
table and the block side by side; edit either side's settings and the block follows.

- **Columns** come from a header row (`From`, `To`, `Length (m)`, `Azimuth`, `Inclination`, `Left`…
  and the usual synonyms) or, without one, from the column count: 3 = length compass clino, 5 = from to
  length compass clino, 9 = the same plus left right up down. Type your own list (`to from tape bearing
  gradient -`, where `-` skips a column) when the guess is wrong.
- **Delimiters** — tabs, semicolons, commas (quoted cells allowed) or spaces; a decimal comma (`3,25`)
  is recognised.
- **Units** — `ft` / `feet` in the length header writes `units length feet`; compass values above 360
  (and none above 400) are read as grads. Override either in the dialog.
- **A table without stations** (an instrument dump) is named from the *first station* — the last
  station before the caret, suggested — counting up. Three near-identical shots in a row (the DistoX
  habit) become one averaged leg and single shots become splays from the current station; set *shots
  per leg* to 0 to make every shot a leg.
- **Checks** — negative or very long lengths, compass or clino out of range, a leg from a station to
  itself, cells that are not numbers: each listed with its line.
- Outside a `centreline` block the result is wrapped in `centreline … endcentreline`.

The same reader imports a `.csv`/`.tsv` file as a new survey (**Import survey…**, `import_survey`, or
`therion-cli import`).

### The incoming folder

Turn on **Tools → Watch the incoming folder** and ThIDE watches `incoming/` in the project (it creates
the folder; the setting is stored in the project's `.thide/project.json`, where `incoming.folder` can
name another folder). Point your phone sync or download folder there: when a survey export has
finished arriving, a notification offers **Import…** — a `.th` opens
[Link survey file](24-linking-surveys.md) on it, a Survex `.svx` or Compass `.dat` is converted first
and then linked, a `.gpx` becomes a fixed-stations file. A `.th2` sketch on its own is only announced:
link its survey first, and copy the sketch along. Dropping a `.svx`, `.dat` or `.gpx` on the window
takes the same route. Watching is off until you turn it on.

## Export out of Therion

### Entrances & fixed points → GIS

**Tools → Export entrances / fixed points** writes your georeferenced points to a GIS/GPS format:

- **KML** (Google Earth), **GeoJSON** (web GIS), **GPX** (hand-held GPS), or **CSV** (spreadsheets).

Great for sharing entrance locations, plotting on a base map, or loading into a GPS before a trip.

### Data tables → CSV

**Tools → Export data table** produces tabular exports:

- **Station table** — every station and its data.
- **Shot table** — every leg/splay.
- **Entrances / fixed points** — the same points as above, as a table.

(You can also **Export CSV…** directly from most grids in the Object Browser and analytics tabs.)

### Survey report → HTML

**Tools → Generate survey report (HTML)…** builds a shareable, self-contained **HTML report** of the
survey — a quick way to publish an overview without compiling maps. Table exports can also be produced
in HTML/LaTeX for inclusion in documents. From a terminal: `therion-cli report project.thconfig --out
report.html`.

## Drawing with Mapiah

For passage art in `.th2` files, ThIDE integrates the external **Mapiah** visual editor:

1. Open a `.th2` and click **Edit with Mapiah** in the file header (or scaffold one first with
   **Tools → New scrap (.th2)…**).
2. Draw in Mapiah; when you **save** there, ThIDE **reloads** the file automatically.
3. If Mapiah isn't installed, ThIDE points you to its download page and to
   **Settings → External Tools** to set the path.

New scraps can be scaffolded **blank**, or **from an image / `.xvi` sketch** so you can trace a
scanned or georeferenced background. The [XVI panel](12-object-browser-and-outline.md#the-xvi-panel)
shows which sketches your scraps reference.

## Coordinate systems

Therion georeferencing uses coordinate systems (EPSG codes / named systems). ThIDE validates the ones
you use and can convert between WGS84 lat/long and UTM for you — see
[Calculators & converters](18-calculators.md). Unknown coordinate systems are flagged as diagnostics
(`TH0043`).

---

Next: [Calculators & converters →](18-calculators.md)
