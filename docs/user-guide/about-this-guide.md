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

### Suggested PDF build

Generate the shipping PDF from these Markdown files with a converter that preserves internal links
and a bookmark outline, e.g. [Pandoc](https://pandoc.org/):

```sh
pandoc docs/user-guide/README.md docs/user-guide/0*.md docs/user-guide/1*.md \
       docs/user-guide/2*.md docs/user-guide/about-this-guide.md \
       --toc --toc-depth=2 -V documentclass=report \
       -o ThIDE-User-Guide.pdf
```

(Order the inputs by filename so the numbered pages come out in reading order.) A CI step can drop
the resulting PDF into the app's bundled assets so **Help → User Guide** always ships the current
manual. This build is intentionally *not* wired up yet — it is a recommendation for the first
release that adds an in-app help entry.

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
