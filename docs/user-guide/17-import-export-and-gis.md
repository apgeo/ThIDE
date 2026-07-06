# 17. Import, export & GIS

> [← Back to the User Guide home](README.md)

ThIDE bridges Therion and the other formats in a caver's toolbox — other survey programs, GIS, GPS,
and terrain data. Most of these live in the **Tools** menu.

## Import into Therion

**Tools → Import…** converts external data into Therion constructs you can then wire into your
project:

| Import | Source format | Becomes |
|---|---|---|
| **Import survey (Survex / Compass)…** | `.svx` (Survex), `.dat` (Compass) | A Therion `.th` survey |
| **Import DEM as surface (.asc)…** | ESRI ASCII grid | A Therion `surface` block |
| **Import GPX waypoints (→ fix)…** | `.gpx` | `fix` lines (georeferenced stations) |

After importing, add the new file to your thconfig (`input`/`source`) and compile. If an import
fails, a toast notification tells you why.

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
in HTML/LaTeX for inclusion in documents.

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
