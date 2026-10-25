# Arhitectură

> [Index documentație](README.md) · [README-ul proiectului](../../README.md)

Codul este împărțit în straturi **complet decuplate**. Straturile inferioare **nu au dependențe de UI / Avalonia / platformă** și pot fi reutilizate dintr-un CLI, un server LSP, o gazdă web sau din teste. Aplicația desktop depinde doar de stratul de abstracțiuni și conectează implementările concrete în rădăcina sa de compoziție.

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

| Proiect | Tip | Scop |
|---|---|---|
| [src/Therion.Core](../../src/Therion.Core) | bibliotecă | Primitive: `SourceSpan`, `Diagnostic`, unități, identificatori, internere. Zero dependențe. |
| [src/Therion.Syntax](../../src/Therion.Syntax) | bibliotecă | Lexere + parsere [Superpower](https://github.com/datalust/superpower) pentru `.th` / `.th2` / `.thconfig` / `.xvi`; AST-uri; tratarea codificărilor și a sistemelor de coordonate. |
| [src/Therion.Semantics](../../src/Therion.Semantics) | bibliotecă | Rezolvare între fișiere, tabele de simboluri, etape de validare, analize, export GIS. |
| [src/Therion.Workspace](../../src/Therion.Workspace) | bibliotecă | Modelul de proiect/sesiune, monitorizarea fișierelor, cache, importatoare (Survex / Compass / DEM / GPX). |
| [src/Therion.Build](../../src/Therion.Build) | bibliotecă | Descoperirea uneltelor Therion, compilare, analiza ieșirii, lansarea vizualizatoarelor. |
| [src/Therion.Processing.Abstractions](../../src/Therion.Processing.Abstractions) | bibliotecă | Interfețe publice consumate de UI și de alte gazde. |
| [src/Therion.Cli](../../src/Therion.Cli) | unealtă (`therion-cli`) | Validare / lint / formatare / statistici / dependențe / GIS fără interfață grafică. |
| [src/Therion.Lsp](../../src/Therion.Lsp) | unealtă (`therion-lsp`) | Language Server (diagnostice prin stdio). |
| [src/Therion.Structural](../../src/Therion.Structural) | bibliotecă | Geologie structurală: detectarea și ajustarea planelor, direcție/înclinare, stereograme. Vedeți [structural-geology.md](structural-geology.md). |
| [src/Therion.Blender](../../src/Therion.Blender) | bibliotecă | Citește modelele compilate `.lox` / `.3d`, le transformă în rețele de poligoane, măsoară graful liniei centrale și scrie scena și scriptul Python pe care le randează un Blender fără interfață (experimental). |
| [ThIDE](../../ThIDE) | aplicație | Interfața desktop Avalonia 12 (MVVM, CommunityToolkit.Mvvm). `Services/UiAutomation/` conține serverul opțional de automatizare UI. |
| `tests/**` | teste | Suite xUnit, inclusiv un corpus de proiecte Therion reale. |
| [tests/ThIDE.UiTests](../../tests/ThIDE.UiTests), [tests/e2e-linux](../../tests/e2e-linux) | teste | Suite UI end-to-end rulate pe aplicația reală (C# prin serverul de automatizare; pytest prin AT-SPI). Sărite de un simplu `dotnet test`; rulați-le cu `tests/run-ui-tests.sh`. |
| [tools/uidrive](../../tools/uidrive) | unealtă | Driver AT-SPI în Python și CLI pentru aplicația în execuție (Linux). |

**Stivă tehnologică:** .NET 8 (LTS) · Avalonia 12 · Superpower (parsare) · Dock.Avalonia (andocare) · CaveView.js (3D) · Svg.Skia / Docnet (randarea hărților și a PDF-urilor). Therion este fixat la **v6.4.0** în [TherionVersion.json](../../TherionVersion.json).

## Reutilizarea bibliotecilor

Bibliotecile `src/**` sunt concepute pentru a fi reutilizate independent de aplicația desktop — parsați fișiere Therion, construiți un model între fișiere, rulați analize sau exportați GIS din propriul dumneavoastră program .NET. Sunt licențiate sub **MPL-2.0** (copyleft la nivel de fișier), deci pot fi încorporate în unelte cu sursă închisă sau cu altă licență și rămân compatibile cu proiectele GPL-3.0 / AGPL-3.0. `src/Therion.Structural` și `src/Therion.Blender` au propriul fișier `LICENSE` MPL-2.0, la fel ca celelalte. Vedeți [LICENSING.md](../../LICENSING.md).

## Testarea UI

Două căi controlează aplicația în execuție end-to-end — un server de automatizare în proces și un driver AT-SPI extern. Cum diferă, cum se rulează și convențiile pentru selectori și automation id: [dev/ui-testing.md](dev/ui-testing.md).
