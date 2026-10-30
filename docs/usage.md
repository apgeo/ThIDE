# Usage

> [Documentation index](README.md) · [Project README](../README.md)

## External tools

ThIDE invokes these as **separate processes** (configurable under **Settings → External Tools**); none are bundled or linked:

| Tool | Used for | Notes |
|---|---|---|
| **Therion** (`therion`) | Compiling projects | Auto-discovered, or set the path manually. Pinned grammar target: v6.4.0. |
| **Loch / Aven** | External 3D / 2D viewers | Launched on compiled output. |
| **Mapiah** | Editing `.th2` sketches | Launches the `.th2` and auto-reloads on save. |

Because Therion (and Loch/Aven/Mapiah) are launched at arm's length over the command line, ThIDE does not link their GPL code — see [LICENSING.md](../LICENSING.md).

## Command-line tools

**CLI** — quick validation and reporting without the GUI:

```sh
dotnet run --project src/Therion.Cli -- validate path/to/project.thconfig
dotnet run --project src/Therion.Cli -- lint   path/to/file.th
dotnet run --project src/Therion.Cli -- format path/to/file.th --write
dotnet run --project src/Therion.Cli -- format caves --check            # exit 1 if any file would change
dotnet run --project src/Therion.Cli -- import raw/trip.csv --out caves/trip.th
dotnet run --project src/Therion.Cli -- report path/to/project.thconfig --out output/report.html
dotnet run --project src/Therion.Cli -- stats  path/to/project.thconfig
dotnet run --project src/Therion.Cli -- deps   path/to/project.thconfig --dot
dotnet run --project src/Therion.Cli -- gis    path/to/project.thconfig --format kml --out entrances.kml
dotnet run --project src/Therion.Cli -- build  caves --all --json --stop-on-failure
dotnet run --project src/Therion.Cli -- new    --list
dotnet run --project src/Therion.Cli -- new    expedition --out ./alba --var expedition=alba --var year=2026
```

`format` is the editor's *Format Document*: it re-indents to the block nesting and trims trailing
whitespace, never reordering or dropping a line. It takes files or folders (`.git`, `.thide` and other
dot folders are skipped); `--write` rewrites in place, keeping each file's encoding and line endings,
and `--check` only lists the files that would change and exits 1 — the form for a pre-commit hook or
a CI step. `--indent 4` or `--indent tab` picks the indentation (default two spaces). Files that do
not parse cleanly are skipped with a warning.

`import` converts Survex (`.svx`), Compass (`.dat`), GPX and shot tables (`.csv`/`.tsv`, or any text
with `--format csv`) to Therion; without `--out` it prints the result, with it it creates a new file and
never overwrites one. For shot tables: `--columns "from to length compass clino"` when the header or the
column count misleads, `--start A1` names the first station of a table without stations, `--merge 3`
(the default) turns three repeated shots into one leg (`0` = every shot is a leg), `--length-unit ft`
and `--angle-unit grad` override the units. Implausible readings are printed as warnings with their
line.

`report` writes the HTML survey report the app's *Generate survey report* makes (stdout without
`--out`; `--title` sets the heading, default the project folder's name).

`new` creates a project from a template: a built-in id, the id of a template imported in the app
(*File → New Project… → Import*), or a template folder. Each `--var` sets one of the template's fields;
values are checked as in the app, and nothing is overwritten.

**LSP** — point any LSP-capable editor at the `therion-lsp` executable for live diagnostics:

```sh
dotnet run --project src/Therion.Lsp
```

See [lsp.md](lsp.md) for client configuration.

## Configuration & data locations

User settings and per-project state live under your platform's application-data folder (`%AppData%/ThIDE` on Windows, the XDG/`~/Library` equivalents elsewhere), including:

- preferences, keyboard shortcuts, theme, and language;
- `snippets.json`, `templates/files/*.json` (your file templates) and `templates/projects/<id>/` (imported project templates);
- the persistent symbol index, parse cache, and crash-recovery buffers;
- semantic-rule config (`rules.json`), plugins (`plugins/*.dll`), and per-project sidecars (leads, metadata).

Performance- or parsing-heavy features have explicit on/off switches under **Preferences ▸ Performance / Extensions / Visualization**. OS file-association registration is described in [file-association.md](file-association.md).
