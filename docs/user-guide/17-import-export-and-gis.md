# 17. Import, export & GIS

> [← Back to the User Guide home](README.md)

ThIDE bridges Therion and the other formats in a caver's toolbox — other survey programs, GIS, GPS,
and terrain data. Most of these live in the **Tools** menu.

## Import into Therion

The three **Import** items of the **Tools** menu convert external data into Therion constructs you
can then wire into your project:

| Import | Source format | Becomes |
|---|---|---|
| **Tools → Import Survey (Survex / Compass)…** | `.svx` (Survex), `.dat` (Compass), `.csv`/`.tsv` (a shot table) | A Therion `.th` survey |
| **Tools → Import DEM as Surface (.asc)…** | ESRI ASCII grid | A Therion `surface` block |
| **Tools → Import GPX Waypoints (→ fix)…** | `.gpx` | `fix` lines (georeferenced stations) |

After importing, add the new file to your thconfig (`input`/`source`) and compile. If an import
fails, a toast notification tells you why.

### Shot tables (spreadsheets, CSV, PocketTopo, DistoX)

**Edit → Paste as Shot Data…** (also in the editor's right-click menu) turns a table of shots into a
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
  itself, cells that are not numbers: each listed with its line. A row one value short is read as a
  splay only in a table where no row is full length (PocketTopo); in any other table it is reported as
  a leg with a missing value and left out. Mark splays with `-` in the *to* column.
- **Where it goes** — the dialog names the place above the block (*Insert into: file, centreline at
  line N*). With the caret (or the selection) inside a `centreline` the block goes there; elsewhere
  in a survey it goes into that survey's last `centreline`, before its `endcentreline`, or into a new
  one at the end of the survey. Outside any survey it refuses and says so, rather than writing the
  block above the `encoding` line.

The same reader imports a `.csv`/`.tsv` file as a new survey (**Import survey…**, `import_survey`, or
`therion-cli import`).

### The incoming folder

Turn on **Tools → Watch the Incoming Folder** and ThIDE watches `incoming/` in the project (it creates
the folder; the setting is stored in the project's `.thide/project.json`, where `incoming.folder` can
name another folder). Point your phone sync or download folder there: when a survey export has
finished arriving, a notification offers **Import…** — a `.th` opens
[Link Survey File](24-linking-surveys.md) on it, a Survex `.svx` or Compass `.dat` is converted first
and then linked, a `.gpx` becomes a fixed-stations file. A `.th2` sketch on its own is only announced:
link its survey first, and copy the sketch along. Dropping a `.svx`, `.dat` or `.gpx` on the window
takes the same route. Watching is off until you turn it on.

### A project around a bare survey (TopoDroid)

A survey exported from TopoDroid (or any lone `.th` with a single `survey` block) does not compile on
its own until it has a thconfig around it. **Tools → Scaffold Project from Survey…** builds that
project for you; **Scaffold Project from Active File** does the same for the `.th` open in the editor.

The dialog is prefilled from the file: the project name is the survey's name with a trailing `_YYYY`
dropped (TopoDroid often names surveys `cave_2025`), the title and a likely entrance station come from
the survey. You choose:

- **Georeferencing (optional)** — entrance station, coordinate system (`lat-long`, `UTM35N`,
  `EPSG:4326`, …), the two coordinates in that system's order, and altitude. Needed for KML,
  shapefile and other GIS output. Leave it blank and the wrapper gets a commented `fix` template to
  fill in later.
- **Layout & project tree** — a `layout` block with map scale and legend, and optionally a graphics
  folder.
- **Export targets** — tick the outputs the thconfig should build: 3D models (Loch `.lox`, Survex
  `.3d`, KML, shapefile, Compass `.plt`, VRML, DXF, 3DMF), maps (PDF plan or extended elevation, SVG,
  XHTML), the SQL or CSV database, and a survey list.

**Create project** then asks for the project folder, copies the survey into `th/`, and writes a
wrapper `.th` and a thconfig with the chosen exports. ThIDE refuses, before writing anything, a folder
where the wrapper would overwrite the survey itself (the survey's own folder) or one that already
holds any of the files it would write — pick an empty folder.

## Export out of Therion

### Entrances & fixed points → GIS

**Tools → Export Entrances / Fixed Points** writes your georeferenced points to a GIS/GPS format:

- **KML** (Google Earth), **GeoJSON** (web GIS), **GPX** (hand-held GPS), or **CSV** (spreadsheets).
  GPX coordinates are written with 7 decimals, as the GPX import reads them, so a round trip keeps
  full precision; KML, GeoJSON and CSV use 6 (about 0.1 m).

Great for sharing entrance locations, plotting on a base map, or loading into a GPS before a trip.

### Data tables → CSV

**Tools → Export Data Table** produces tabular exports:

- **Station table** — every station and its data.
- **Shot table** — every leg/splay.
- **Entrances / fixed points** — the same points as above, as a table.

(You can also **Export CSV…** directly from most grids in the Object Browser and analytics tabs.)

### Survey report → HTML

**Tools → Generate Survey Report (HTML)…** builds a shareable, self-contained **HTML report** of the
survey — a quick way to publish an overview without compiling maps. Table exports can also be produced
in HTML/LaTeX for inclusion in documents. From a terminal: `therion-cli report project.thconfig --out
report.html`.

### The whole project as one .th file

**Tools → Export as a Single .th File…** merges every survey of the project — every file the
configuration's `source` lines read, and every `input` inside them — into one `.th` file that holds
**one survey with one centreline**. It is a way to hand the whole cave to someone (or to another
program) as a single file, or to look at all the data in one place. Files open in the editor are read
as they are there, unsaved changes included.

- **One station per point.** Stations joined by `equate` become one station, named after the one the
  project names first; `point@survey` references are resolved the way Therion resolves them. Choose
  how the names are written: kept as they are (where two surveys use the same name, those stations
  get their survey's name in front, e.g. `sump_12`), always with the survey's name in front, or
  numbered 1, 2, 3 …; an optional prefix goes in front of every name. **Also save the list of old and
  new station names** writes a `.names.csv` next to the file.
- **Readings in metres and degrees.** `units`, `calibrate`, backsights and topofil counters are worked
  out; diving, cylindrical-polar and cartesian shots stay in their own style; station-interleaved data
  becomes from/to rows; passage dimensions (left/right/up/down) are kept.
- **Magnetic declination.** Each centreline's declination is worked out as Therion does it: the one the
  centreline or an enclosing survey states (`declination`, `grid-angle`, `survey -declination`), else
  the IGRF field at the centreline's date and the mean position of the fixed points; undated
  centrelines take the one of the earliest date in the project. It is added to every compass reading,
  and the file says `declination 0`, so it compiles the same without any dates. The comment before
  each part of the file names the declination used. When the fixed points are in a coordinate system
  ThIDE cannot place on the globe, give the cave's latitude and longitude. A project without fixed
  points in a global coordinate system gets no date-based declination (Therion applies none either)
  unless you give its position.
- **Fewer splays.** Keep a share of each station's splays (in %) and/or at most a number per station.
  The splays kept are the ones that cover the passage best (spread round the station), not the first
  ones; a splay that ends at a named station used anywhere else is always kept.
- **Standalone.** With **Include everything the file needs on its own**, the file also carries the
  coordinate system and fixed points, the entrances and the other station comments and flags
  (continuations, springs …), station marks, the dates and the team. Without it, it holds the
  measurements only: shots, splays, dimensions, shot flags and extended-elevation directions.
- **Not carried:** drawings (`.th2`), maps, surfaces, data brought in with `import` (a `.3d`), and the
  configuration's `select` choices. The configuration's output coordinate system (`cs`) cannot be
  written in a `.th`; the file's header names it.

Compiled by Therion, the merged file gives every station the position it has in the project (to
Therion's centimetre). From a terminal: `therion-cli flatten project.thconfig --out cave-single.th
--standalone --splays 50 --max-splays 4 --names cave-single.csv --thconfig single.thconfig`
(`--thconfig` also writes a configuration that compiles it; `therion-cli --help` lists the options).

## Drawing with Mapiah

For passage art in `.th2` files, ThIDE integrates the external **Mapiah** visual editor:

1. Open a `.th2` and click **Edit with Mapiah** in the file header (or scaffold one first with
   **Tools → New Scrap (.th2)…**).
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
