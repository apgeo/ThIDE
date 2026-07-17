# About this guide

> [← Back to the User Guide home](README.md)

This page explains **how the user guide is structured**, so that both humans and AI assistants can
keep it accurate and extend it as ThIDE grows. If you only want to *use* ThIDE, you can skip this
page.

## What this guide is

- **Audience:** end users and power users (cavers, surveyors) — not developers.
- **Scope:** what the app does and how to use it. Implementation/architecture lives elsewhere
  (see [docs/architecture.md](../architecture.md)); this guide only mentions internals when they
  affect what you should *do*.
- **Source format:** plain **Markdown**, one file per topic, under
  [`docs/user-guide/`](.). This is deliberately editable by hand and by tooling.

## Format & how it ships

The guide is authored in Markdown and can be **shipped as a single navigable PDF** inside ThIDE
(with a bookmark outline and clickable internal links). ThIDE already renders PDFs in its
[Map Viewer](11-viewers.md#map-viewer), so a bundled `ThIDE-User-Guide.pdf` opens naturally in-app,
and the same Markdown can also be served in a future in-app Help panel.

**Why Markdown as the source (not authoring directly in PDF/Word):**

| Need | How Markdown satisfies it |
|---|---|
| Human-editable | Anyone can edit a `.md` file in any text editor — including ThIDE itself. |
| Agent-editable & extensible | AI assistants can add/rewrite pages reliably from plain text. |
| Version-controlled | Diffs are readable; changes are reviewable in Git. |
| Multi-output | One source → PDF (for shipping), HTML (for a website), or in-app Markdown. |
| Consistent with the repo | The rest of `docs/` is already Markdown. |

### Building the PDF

A build script collates these pages (in filename order) and runs [Pandoc](https://pandoc.org/) into a
single PDF with a bookmark outline and a table of contents:

```sh
# Windows
pwsh build/build-user-guide.ps1
# Linux / macOS
build/build-user-guide.sh
# No PDF engine installed? Produce a self-contained HTML instead:
pwsh build/build-user-guide.ps1 -Format html      #  (or:  FORMAT=html build/build-user-guide.sh)
```

The default output is `ThIDE/Assets/ThIDE-User-Guide.pdf`, which the app bundles automatically via its
globbed `<AvaloniaResource Include="Assets\**" />`. **Help → User Guide** then opens it **in the app's
own PDF viewer** (the [Map Viewer](11-viewers.md#map-viewer)) when that viewer is enabled, else in the
OS default viewer — see [`UserGuideService`](../../ThIDE/Services/UserGuideService.cs) and the
`OpenUserGuide` command in `MainWindowViewModel`.

**Requirements:** `pandoc` always, plus **`xelatex`** for the PDF format — a TeX distribution
(MiKTeX / TeX Live, with `texlive-xetex`). xelatex is needed because the guide uses Unicode glyphs
(arrows, box symbols) that `pdflatex` cannot typeset. No TeX? Use `-Format html`.

The generated PDF is a **build artifact** — it is `.gitignore`d, not committed. The release workflow
([.github/workflows/release.yml](../../.github/workflows/release.yml)) builds it once in a `guide`
job, **attaches it to the GitHub Release** as a standalone download, and hands it to the per-OS
`publish` jobs which drop it into `ThIDE/Assets` before `dotnet publish` so it ships inside every
package. When the PDF is absent (e.g. a plain local dev build), **Help → User Guide** falls back to
opening this Markdown source on disk, then the online docs, so the menu item always works.

**Cross-page links** are fixed up when the pages are merged, by a Pandoc Lua filter
([`build/user-guide-links.lua`](../../build/user-guide-links.lua)): a link to another guide page
jumps within the document, and a link to any other repo file (a reference doc, a source file) opens
its copy on GitHub in a browser. One known limitation: a link to a *specific sub-section* of another
guide page currently jumps to that page's top — mapping every sub-anchor is a possible follow-up.

## The version stamp (update it whenever you touch the guide)

The first page ([`README.md`](README.md)) carries a stamp naming **the ThIDE version this guide's
content was last checked against**:

```markdown
> **For ThIDE 0.3.0-alpha.1** — guide content last updated 2026-07-17.
```

**This is a content fact, not a build fact.** It is deliberately *not* the version that happened to
be in `build/version.props` when someone regenerated the PDF — a rebuild with no content change must
not move it. It answers the reader's actual question: *"is this guide describing the app I'm
running?"*

Rules:

- **Bump it when you update the guide's content** to match a newer app version — and only then. Use
  the app's SemVer as **Help → About** reports it (`build/version.props`:
  `TherionVersionMajor.Minor.Patch` + `TherionPrerelease`).
- **Leave it alone** for edits that don't change what the guide describes (typos, wording, link
  fixes) — the stamp says which app version the *content* tracks, not when the file was touched.
- **Keep the `**For ThIDE <version>**` shape.** The build scripts parse that line and pass it to
  Pandoc as the document `subtitle`, so the version also prints on the **PDF title page** (page 1)
  and in the HTML build. Reword the rest of the line freely; keep that prefix. If the stamp goes
  missing the build still succeeds — it warns and the title page just omits the version.

## Page conventions

Keep new pages consistent with the existing ones:

- **File naming:** `NN-topic-name.md`, two-digit prefix for reading order. Non-numbered meta pages
  (like this one) have descriptive names.
- **Top of every page:** an `H1` title, then a back-link line: `> [← Back to the User Guide home](README.md)`.
- **Cross-links:** link generously between pages. Use relative links (`07-editing.md`), and link to
  reference docs one level up (`../diagnostics.md`).
- **Menus & controls:** name them exactly as they appear in the app, e.g. **View → Map Viewer**,
  the **Diagnostics** panel, the **Compile** menu. Bold the literal label.
- **Shortcuts:** write keys as `Ctrl+Shift+P`. The canonical list is
  [Keyboard shortcuts](21-keyboard-shortcuts.md) — link there rather than duplicating.
- **Tone:** short, scannable, task-first. Prefer tables and bullet lists over long paragraphs.
- **Scope discipline:** one topic per page; if a page grows past a couple of screens, split it and
  add both halves to the home [Contents](README.md#contents).

## Adding a new page

1. Create `docs/user-guide/NN-your-topic.md` following the conventions above.
2. Add a line for it under the right section of [`README.md`](README.md) Contents.
3. Link to it from any related pages.
4. If a PDF build exists, the new file is picked up automatically by the filename-ordered glob.

## Known gaps / good first edits

This is a **first iteration**. Screenshots would help a lot and are not included yet — a natural
next contribution is to add a `images/` folder and embed annotated captures on the interface and
viewer pages. Other TODOs are called out inline with a `<!-- TODO -->` comment where relevant.
