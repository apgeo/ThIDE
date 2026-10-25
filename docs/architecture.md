# Architecture

> [Documentation index](README.md) · [Project README](../README.md)

The codebase is split into **fully decoupled** layers. The lower layers have **no UI / no Avalonia / no platform** dependencies and are reusable from a CLI, an LSP server, a web host, or tests. The desktop app depends only on the abstractions layer and wires concrete implementations at its composition root.

```
┌──────────────────────────────────────────────────────────────┐
│  ThIDE                  Avalonia desktop app (UI)            │
│  Views / ViewModels / DI composition root                    │
└───────────────────────────▲──────────────────────────────────┘
                            │ (interfaces only)
┌──────────────────────────────────────────────────────────────┐
│  Therion.Workspace           in-memory project / session     │
│  Therion.Build               drives the Therion toolchain    │
│  Therion.Semantics           cross-file model + indexes      │
│  Therion.Syntax              per-file lexer/parser + AST     │
│  Therion.Core                primitives (SourceSpan, …)      │
│  Therion.Processing.Abstractions   public interfaces         │
└──────────────────────────────────────────────────────────────┘
```

| Project | Kind | Purpose |
|---|---|---|
| [src/Therion.Core](../src/Therion.Core) | library | Primitives: `SourceSpan`, `Diagnostic`, units, identifiers, interners. Zero deps. |
| [src/Therion.Syntax](../src/Therion.Syntax) | library | Lexers + [Superpower](https://github.com/datalust/superpower) parsers for `.th` / `.th2` / `.thconfig` / `.xvi`; ASTs; encoding & coordinate-system handling. |
| [src/Therion.Semantics](../src/Therion.Semantics) | library | Cross-file resolution, symbol tables, validation passes, analytics, GIS export. |
| [src/Therion.Workspace](../src/Therion.Workspace) | library | Project/session model, file watching, caching, importers (Survex / Compass / DEM / GPX). |
| [src/Therion.Build](../src/Therion.Build) | library | Therion toolchain discovery, compilation, output parsing, viewer launching. |
| [src/Therion.Processing.Abstractions](../src/Therion.Processing.Abstractions) | library | Public interfaces consumed by the UI and other hosts. |
| [src/Therion.Cli](../src/Therion.Cli) | tool (`therion-cli`) | Headless validation / lint / format / stats / deps / GIS. |
| [src/Therion.Lsp](../src/Therion.Lsp) | tool (`therion-lsp`) | Language Server (diagnostics over stdio). |
| [src/Therion.Structural](../src/Therion.Structural) | library | Structural geology: plane detection and fitting, strike/dip, stereonets. See [structural-geology.md](structural-geology.md). |
| [src/Therion.Blender](../src/Therion.Blender) | library | Reads compiled `.lox` / `.3d` models, meshes them, measures the centreline graph, and writes the scene and Python script a headless Blender renders (experimental). |
| [ThIDE](../ThIDE) | app | Avalonia 12 desktop UI (MVVM, CommunityToolkit.Mvvm). `Services/UiAutomation/` holds the opt-in UI automation server. |
| `tests/**` | tests | xUnit suites, including a corpus of real-world Therion projects. |
| [tests/ThIDE.UiTests](../tests/ThIDE.UiTests), [tests/e2e-linux](../tests/e2e-linux) | tests | End-to-end UI suites against the real app (C# over the automation server; pytest over AT-SPI). Skipped by a plain `dotnet test`; run with `tests/run-ui-tests.sh`. |
| [tools/uidrive](../tools/uidrive) | tool | Python AT-SPI driver and CLI for the running app (Linux). |

**Stack:** .NET 8 (LTS) · Avalonia 12 · Superpower (parsing) · Dock.Avalonia (docking) · CaveView.js (3D) · Svg.Skia / Docnet (map & PDF rendering). Therion is pinned to **v6.4.0** in [TherionVersion.json](../TherionVersion.json).

## Reusing the libraries

The `src/**` libraries are designed to be reused independently of the desktop app — parse Therion files, build a cross-file model, run analytics, or export GIS from your own .NET program. They are licensed under **MPL-2.0** (file-level copyleft), so they can be embedded in closed-source or differently-licensed tools, and remain compatible with GPL-3.0 / AGPL-3.0 projects. `src/Therion.Structural` and `src/Therion.Blender` carry their own MPL-2.0 `LICENSE` like the others. See [LICENSING.md](../LICENSING.md).

## UI testing

Two routes drive the running app end to end — an in-process automation server and an external AT-SPI driver. How they differ, how to run them, and the selector and automation-id conventions: [dev/ui-testing.md](dev/ui-testing.md).
