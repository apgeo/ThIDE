# ThIDE User Guide

<!-- Version stamp — the ThIDE release this guide's content was last checked against, NOT the date
     the PDF was generated. Bump it whenever you update the guide (see about-this-guide.md), and
     keep the "**For ThIDE <version>**" shape: build/build-user-guide.* parses this line to put the
     version on the PDF/HTML title page. -->
> **For ThIDE 0.3.0-alpha.1** — guide content last updated 2026-07-17.
> Check **Help → About** for the version you are running; if yours is newer, some screens may have
> moved.

Welcome to the ThIDE user guide — a hands-on manual for cavers and surveyors who use
**ThIDE** to write, check, build, and explore [Therion](https://therion.speleo.sk/) cave-survey
projects.

This guide is task-oriented: it explains what each part of the app is *for*, and how you actually
use it in the field-to-map workflow. It does not assume you are a programmer. It does assume you
have some familiarity with Therion itself (the `.th` / `.th2` / `.thconfig` files and how a cave is
described in them). If Therion is new to you, keep the **Therion Book** open alongside this guide
(**Help → Therion Book**, bundled in the app).

> **New here?** Read [Introduction](01-introduction.md) → [Installation & setup](02-installation-and-setup.md)
> → [Getting started](03-getting-started.md) in order. Everything else you can dip into as needed.

---

## Contents

### Get oriented
1. [Introduction — what ThIDE is and isn't](01-introduction.md)
2. [Installation & setup](02-installation-and-setup.md)
3. [Getting started: your first session](03-getting-started.md)
4. [Core concepts & vocabulary](04-core-concepts.md)
5. [A tour of the interface](05-interface-tour.md)

### Work
6. [Typical workflows](06-typical-workflows.md)
7. [The editor](07-editing.md)
8. [Navigation & search](08-navigation-and-search.md)
9. [Diagnostics & validation](09-diagnostics-and-validation.md)

### Build & visualize
10. [Compiling & output](10-compiling-and-output.md)
11. [The viewers: Map, 3D & Mainline preview](11-viewers.md)
- [Blender animation renders](23-blender-animation.md) — *experimental, work in progress* (page 23,
  at the end of this guide)

### Analyze your survey
12. [Object Browser & Outline](12-object-browser-and-outline.md)
13. [Survey Overview & analytics](13-survey-overview-and-analytics.md)
14. [Exploration leads, TODOs & metadata](14-leads-notes-and-metadata.md)
15. [Structural geology](15-structural-geology.md)
16. [The Relational Map](16-relational-map.md)

### Exchange data
17. [Import, export & GIS](17-import-export-and-gis.md)
18. [Calculators & converters](18-calculators.md)

### Configure & extend
19. [Settings & preferences](19-settings-and-preferences.md)
20. [Extensibility (CLI, LSP, AI assistants, plugins, hooks)](20-extensibility.md) — includes the
    *experimental* [Assistant panel & MCP integration](20-extensibility.md#ai-assistants-mcp)

### Reference
21. [Keyboard shortcuts](21-keyboard-shortcuts.md)
22. [Troubleshooting & FAQ](22-troubleshooting-and-faq.md)
- [About this guide (how to edit & extend it)](about-this-guide.md)

---

## How this guide relates to the rest of the docs

The [`docs/`](../README.md) folder also contains **reference** material that goes deeper on a few
topics (the full diagnostic-code catalogue, the language-server protocol, the plugin API, packaging).
Those are linked from the relevant pages here. This user guide is the front door; the reference docs
are the appendix.

- Project overview & quick start: [top-level README](../../README.md)
- Full feature list (one page): [docs/features.md](../features.md)
- Every diagnostic code: [docs/diagnostics.md](../diagnostics.md)

---

*ThIDE is in an alpha development stage. Some screens and shortcuts may have moved since this guide
was written — if something doesn't match, please help us fix it (see
[About this guide](about-this-guide.md)).*
