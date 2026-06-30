# Usage

> [Documentation index](README.md) · [Project README](../README.md)

## External tools

TherionProc invokes these as **separate processes** (configurable under **Settings → External Tools**); none are bundled or linked:

| Tool | Used for | Notes |
|---|---|---|
| **Therion** (`therion`) | Compiling projects | Auto-discovered, or set the path manually. Pinned grammar target: v6.4.0. |
| **Loch / Aven** | External 3D / 2D viewers | Launched on compiled output. |
| **Mapiah** | Editing `.th2` sketches | Launches the `.th2` and auto-reloads on save. |

Because Therion (and Loch/Aven/Mapiah) are launched at arm's length over the command line, TherionProc does not link their GPL code — see [LICENSING.md](../LICENSING.md).

## Command-line tools

**CLI** — quick validation and reporting without the GUI:

```sh
dotnet run --project src/Therion.Cli -- validate path/to/project.thconfig
dotnet run --project src/Therion.Cli -- lint   path/to/file.th
dotnet run --project src/Therion.Cli -- format path/to/file.th --write
dotnet run --project src/Therion.Cli -- stats  path/to/project.thconfig
dotnet run --project src/Therion.Cli -- deps   path/to/project.thconfig --dot
dotnet run --project src/Therion.Cli -- gis    path/to/project.thconfig --format kml --out entrances.kml
```

**LSP** — point any LSP-capable editor at the `therion-lsp` executable for live diagnostics:

```sh
dotnet run --project src/Therion.Lsp
```

See [lsp.md](lsp.md) for client configuration.

## Configuration & data locations

User settings and per-project state live under your platform's application-data folder (`%AppData%/TherionProc` on Windows, the XDG/`~/Library` equivalents elsewhere), including:

- preferences, keyboard shortcuts, theme, and language;
- the persistent symbol index, parse cache, and crash-recovery buffers;
- semantic-rule config (`rules.json`), plugins (`plugins/*.dll`), and per-project sidecars (leads, metadata).

Performance- or parsing-heavy features have explicit on/off switches under **Preferences ▸ Performance / Extensions / Visualization**. OS file-association registration is described in [file-association.md](file-association.md).
