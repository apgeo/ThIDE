# Layout blocks & embedded code (MetaPost / TeX)

How the app parses and highlights a Therion `layout … endlayout` block and the foreign-language
code embedded inside it.

## What a layout body contains

A `layout` body is a mix of three things:

1. **Option lines** — `key [value…]` (e.g. `scale 1 500`, `legend on`, `symbol-hide point cave-station`).
2. **Embedded MetaPost** — `code metapost … endcode`. MetaPost macros that customise drawing.
3. **Embedded TeX** — `code tex-map … endcode` and `code tex-atlas … endcode`. TeX/LaTeX for the
   legend/map/atlas text.

`code metapost` and `code tex-*` are **different languages** — MetaPost is a Metafont-derived
drawing language; `tex-map`/`tex-atlas` are TeX. They are highlighted by separate lexers.

## Parsing

Layout blocks are parsed into a typed `LayoutCommand`
([`src/Therion.Syntax/ThconfigAst.cs`](../src/Therion.Syntax/ThconfigAst.cs)) by
[`LayoutBodyParser`](../src/Therion.Syntax/LayoutBodyParser.cs), reused by both the `.thconfig`
parser ([`ThconfigParser`](../src/Therion.Syntax/ThconfigParser.cs)) and the `.th` parser
([`ThParser`](../src/Therion.Syntax/ThParser.cs)) — some projects keep layouts in `input`-ed `.th`
files. The model captures the options, the `copy`/`cs`/`symbol-set`/`symbol-*` directives, and the
embedded `code … endcode` blocks (recorded with their language tag, body kept opaque).

Unknown option keys raise `UnknownLayoutOption` (warning in lenient mode, error in strict). The
known keys live in [`LayoutKeywords`](../src/Therion.Syntax/LayoutKeywords.cs).

### The greedy `code` → `endcode` rule

Therion reads a `code` block greedily up to the **next** `endcode`. A `code metapost` with no
`endcode` therefore swallows the following option lines until the next `endcode`. This is real:
see [`tests/Corpus/Synthetic/project/Vladusca.thconfig`](../tests/Corpus/Synthetic/project/Vladusca.thconfig),
where the first `code metapost` has no `endcode` and pulls a later `scale 1 100` into the metapost
block. Both the parser (`LayoutBodyParser`) and the editor region scanner honour this rule, so the
two never disagree about where a code block ends.

## Highlighting

The editor blanked the whole layout body in the past. Now each line is classified into an
`EmbeddedRegion` by [`LayoutRegionScanner`](../src/Therion.Syntax/LayoutRegionScanner.cs) — the
single source of truth for "which language is this line?":

| Region | Lines | Highlighter |
|---|---|---|
| `LayoutOption` | option lines + the `code …`/`endcode` fences | `TokenClassifier.ClassifyLayoutLine` (option key → keyword) |
| `MetaPost` | body of a `code metapost` block | [`MetaPostLexer`](../src/Therion.Syntax/MetaPostLexer.cs) |
| `Tex` | body of a `code tex-map`/`tex-atlas` block | [`TexLexer`](../src/Therion.Syntax/TexLexer.cs) |
| `None` | opaque bodies (e.g. `lookup`) | not highlighted |
| _absent_ | ordinary Therion lines (incl. `layout`/`endlayout`) | `TokenClassifier.Classify` |

[`TherionColorizer`](../TherionProc/Editor/TherionColorizer.cs) receives the map via
`SetLineRegions` and dispatches per line; all lexers emit the shared `ClassifiedSpan` /
`TokenClassification` stream, so they render through the same palette (and the same HTML-report /
CLI paths) with no new colours.

### Embedded lexers — scope

`MetaPostLexer` and `TexLexer` are **highlighting-grade** tokenizers, not full parsers — there is no
mature reusable .NET parser for either language (both reference implementations are C/web2c) and we
only need lexical colouring. They recognise comments, strings, numbers, operators/grouping and a
curated keyword/primitive set (plus Therion-specific MetaPost macros such as `thdraw`/`thfill`).
They are line-local, matching the Therion colorizer; a string spanning multiple `code` lines (very
rare) would mis-highlight until a future incremental highlighter promotes it.

## Future work (deferred, intentionally out of scope)

- **Deep per-option value validation.** Today only the option *key* is validated; argument shapes
  are not. A future pass could validate values (e.g. `scale` → positive numbers, `legend` → on/off,
  `color-model` → cmyk/rgb/grey, `symbol-set` → a known standard, `north` → true/grid, `units` →
  unit + factor). See the `FUTURE` block in
  [`LayoutKeywords.cs`](../src/Therion.Syntax/LayoutKeywords.cs) for the full deferred list. This
  was deferred to keep the surface small and avoid brittleness across Therion versions.
- **Multi-line embedded constructs** (strings spanning `code` lines) via an incremental highlighter.
- **Context-aware completion** inside a layout body (offer `LayoutKeywords.Keys` +
  `SymbolSets.StandardNames`).
