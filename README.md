# TherionProc

A cross-platform desktop **workbench / IDE for [Therion](https://therion.speleo.sk/) cave-survey projects** — modern editor tooling, a cross-file semantic model, survey analytics, syntax checking / highlighting for the `.th` / `.th2` / `.thconfig` / `.xvi` file formats, Therion compile integration, and 3D view of resulted models.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![UI](https://img.shields.io/badge/UI-Avalonia%2012-7B61FF)
![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-informational)
![License](https://img.shields.io/badge/license-MPL--2.0%20(libs)%20%2F%20GPL--3.0--or--later%20(app)-blue)
![Therion](https://img.shields.io/badge/Therion-v6.4.0-2E7D32)

> TherionProc is an independent IDE that *understands* the Therion languages and *drives* the Therion toolchain — it does not replace it. The core libraries are a C# implementation of the Therion file **formats**; the actual cave compilation is delegated to your installed `therion` binary. TherionProc is not affiliated with or endorsed by the Therion project.

---

## Highlights

- **Semantic editor** for `.th`, `.th2`, `.thconfig`, and `.xvi` — syntax highlighting, context-aware autocomplete, hover docs, folding, diagnostic squiggles, and an overview ruler, built on AvaloniaEdit.
- **Cross-file logical model** with **go-to-definition**, **find-references**, and **rename** across an entire project.
- **Build & run** the real Therion toolchain with streaming output, cancellation, and clickable `file:line` diagnostics — then open results in **Loch / Aven** or the built-in viewers.
- **Map / scrap editing** of .th2 files is provided by integration of Mapiah.
- **Live 2D centreline preview** and an **embedded 3D model viewer** (CaveView.js) with station-pick → jump-to-source.
- **Survey analytics** — length breakdowns, vertical/horizontal extents, team rollups, fixed points, data-quality, and exploration **leads**.
- **Import / export & GIS** — Survex, Compass, DEM, and GPX import; entrance/fix export to KML / GeoJSON / GPX / CSV.
- **Extensible & scriptable** — a headless CLI, an editor-agnostic **LSP** server, script hooks, and a semantic-rule plugin loader.
- **Cross-platform** (Windows / macOS / Linux), localized in **English and Romanian** (additions are welcome).

A complete feature reference: **[docs/features.md](docs/features.md)**.

---
The application is in an alpha development stage: there are known bugs to be fixed, UI evolves, features are tested to varying degrees, and testing on Linux and macOS was sparse.

---

## Getting started

### Prerequisites
- **.NET 8 SDK** (`8.0.x`).
- A platform web engine for the optional 3D viewer: WebView2 (bundled on Windows 11 / modern Edge), WKWebView (built into macOS), or WebKitGTK on Linux (`apt install libwebkit2gtk-4.1-0`).
- The external **Therion toolchain** for compiling (see [External tools](docs/usage.md#external-tools)).

### Build

```sh
git clone <your-fork-url> TherionProc
cd TherionProc
dotnet restore TherionProc.sln
# -m:1 is required: the full solution OOMs MSBuild under parallel builds.
dotnet build TherionProc.sln -m:1 -c Release
```

### Run the app

```sh
dotnet run --project TherionProc
```

### Test

```sh
dotnet test TherionProc.sln -m:1 -c Release
```

> The CI matrix builds and tests on Windows, Linux, and macOS — see [.github/workflows/ci.yml](.github/workflows/ci.yml).

---

## Documentation

Full documentation hosted in the [`docs/`](docs/README.md) folder:

- **[Features](docs/features.md)** — the complete feature reference.
- **[Architecture](docs/architecture.md)** — layer design, project layout, and reusing the libraries.
- **[Usage](docs/usage.md)** — external tools, the command-line tools, and configuration & data locations.

Topic guides: [3D viewer](docs/3d-viewer.md) · [Diagnostics](docs/diagnostics.md) · [LSP](docs/lsp.md) · [Plugins](docs/plugins.md) · [Release](docs/release.md) — full index in [docs/README.md](docs/README.md).

---

## Contributing

Contributions are welcome — just open a pull request. See [CONTRIBUTING.md](CONTRIBUTING.md).

---

## License

TherionProc is dual-licensed: **Reusable libraries** (`src/**`) — **MPL-2.0**;  The IDE **Application** (`TherionProc/**`) — **GPL-3.0-or-later** . Full texts live in each project's `LICENSE` file (libraries) and the repository-root [LICENSE](LICENSE) (app). Further notes on licensing in [LICENSING.md](LICENSING.md).

---

## Acknowledgements

- The **[Therion](https://therion.speleo.sk/)** project — the cave-surveying system this tool targets.
- The **[Survex](https://github.com/ojwb/survex/)** project, [survex.com](https://survex.com/) .
- **[CaveView.js](https://github.com/aardgoose/CaveView.js)** — the embedded 3D model renderer.
- **[Mapiah](https://github.com/rsevero/mapiah)** project, [flathub](https://flathub.org/en/apps/io.github.rsevero.mapiah) — the .th2 visual map / scrap editor.
- **[Avalonia](https://avaloniaui.net/)**, **[AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)**, **[Dock.Avalonia](https://github.com/wieslawsoltes/Dock)**, **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)**, **[Superpower](https://github.com/datalust/superpower)**, **Svg.Skia**, and **Docnet** — some of the libraries TherionProc is built on.
