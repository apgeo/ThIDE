# Application directives (`#@`)

> [Documentation index](README.md) · [Project README](../README.md)

ThIDE adds an editor/UX layer that lives **inside Therion comments**, so it is fully
forward/backward compatible with Therion syntax — Therion sees only a comment and ignores it.
These *application directives* are the rough equivalent of preprocessor / pragma lines. The first
(and currently only) consumer is the collapsible **`#@region … #@endregion`** block; a future batch
adds `#@if/#@elif/#@else/#@endif` on the same model.

The engine is a pure library — [`src/Therion.Syntax/Directives/`](../src/Therion.Syntax/Directives) —
so it is reusable and unit-tested independently of the app.

## Syntax

```
#@<type> <arg1> <arg2> … <argN>
```

- The `#@` opens a directive; the **type** follows immediately and is **case-insensitive**
  (`#@Region` ≡ `#@region`).
- A `#` inside a Therion double-quoted string is not a comment, so it does not start a directive
  (mirrors the tokenizer).
- **Arguments** are separated by runs of blanks and/or a single comma (with optional surrounding
  blanks). A comma may delimit an *empty* slot → that argument is **undefined**; a run of blanks
  never produces an empty argument, and a trailing comma adds none.
- An argument written as `_` or `undefined` (case-insensitive) is **undefined**.
- An argument wrapped in `'…'` or `"…"` is a single argument equal to the quoted text, verbatim
  (a quoted `undefined` is a defined value).

## Regions (`#@region` / `#@endregion`)

Wrap any span of a `.th` / `.th2` / `.thconfig` file in a foldable, named region:

```therion
#@region "Entrance series"
  centreline
    ...
  endcentreline
#@endregion
```

- The optional first argument is the region **title**, shown on the collapsed fold header (a bare
  `#@region` folds as `#@region`).
- Regions **nest** like C# `#region`: the innermost `#@endregion` closes the innermost open
  `#@region`. Only *closed* regions are foldable.
- Folding is computed by [`TherionFoldingStrategy`](../ThIDE/Editor/TherionFoldingStrategy.cs) from
  the same [`DirectiveScanner`](../src/Therion.Syntax/Directives/DirectiveScanner.cs) the diagnostics
  use, so the fold and the warnings never disagree about where a region ends.

### Enclose in Region

Select some lines and run **Edit → Enclose in Region** (`Ctrl+Shift+R`, also in the command palette).
You're prompted for a title; the selection is wrapped in `#@region "<title>"` / `#@endregion`. Title
quoting is handled by [`RegionDirective`](../src/Therion.Syntax/Directives/RegionDirective.cs)
(prefers single quotes, falls back to double quotes when the title contains one).

## Diagnostics

Unbalanced regions are flagged in the Diagnostics panel — see
[diagnostics.md](diagnostics.md#application-directives-thide_dir_xxx):

| Code | Meaning |
|---|---|
| `THIDE_DIR_001` | `#@region` is missing its `#@endregion`. |
| `THIDE_DIR_002` | `#@endregion` has no matching `#@region`. |
