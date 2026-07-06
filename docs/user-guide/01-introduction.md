# 1. Introduction — what ThIDE is and isn't

> [← Back to the User Guide home](README.md)

## What ThIDE is

**ThIDE** is a cross-platform desktop **workbench / IDE for Therion cave-survey projects**. Think of
it as the tool you keep open while you turn raw survey data into finished cave maps: it *understands*
the Therion languages and *drives* the Therion toolchain, so you spend less time fighting syntax and
folder paths and more time surveying and drawing.

It gives you, in one window:

- A **smart editor** for the four Therion file types (`.th`, `.th2`, `.thconfig`, `.xvi`) with
  syntax colouring, autocomplete, hover help, folding, and live error-checking.
- A **cross-file understanding** of your whole project — so it can jump to where a station or survey
  is defined, find every reference to it, and rename it everywhere at once.
- One-click **compiling** with the real Therion program, with clickable errors and a list of the
  files it produced.
- Built-in **viewers** — a 2D map viewer, an embedded 3D model viewer, and a quick centreline
  preview — plus **survey analytics** (lengths, depths, teams, data quality) and an
  **exploration-leads** register.
- **Import/export** helpers (Survex, Compass, DEM, GPX, KML/GeoJSON/GPX/CSV) and handy
  **calculators** (units, coordinates, magnetic declination).

## What ThIDE is *not*

ThIDE **does not replace Therion.** It does not compile caves itself — the actual cave computation
(loop closure, map rendering, 3D model generation) is delegated to *your installed `therion`
program*. ThIDE understands the file **formats** and orchestrates the toolchain around them.

This matters in practice:

- You still need Therion installed to **compile** (see [Installation & setup](02-installation-and-setup.md)).
- The lengths, depths and previews ThIDE shows you are **preview-quality**, computed in-app for
  instant feedback. They do *not* include Therion's loop adjustment. **Therion remains the source of
  truth** for final adjusted numbers and maps.
- ThIDE is an independent project and is **not affiliated with or endorsed by** the Therion project.

## Who it's for

- **Cavers and surveyors** who already keep their data in Therion and want a friendlier editing and
  building experience than a bare text editor.
- **Project coordinators** who manage a multi-cave, multi-surveyor project and need an overview:
  what's connected, what's floating, where the leads are, how long the cave is.
- **Power users** who want scripting hooks, a headless command-line tool, or editor integrations —
  see [Extensibility](20-extensibility.md).

## The mental model in one paragraph

You point ThIDE at a **workspace** (a folder of Therion files) and choose an **active `.thconfig`**
(the configuration file that says what to build). ThIDE parses everything into an in-memory
**object graph** — surveys, stations, scraps, maps and how they connect — and keeps it live as you
type. From that graph it drives everything else: navigation, error-checking, analytics, previews,
and the compile. The next page gets you set up; [Core concepts](04-core-concepts.md) unpacks this
vocabulary properly.

## A note on the alpha stage

ThIDE is in **active alpha development**. Expect the occasional rough edge, and expect the UI to keep
evolving. Testing has been heaviest on Windows and lighter on Linux and macOS. Your data is plain
Therion text files the whole time — ThIDE never locks it into a proprietary format — so you can
always fall back to Therion directly.

---

Next: [Installation & setup →](02-installation-and-setup.md)
