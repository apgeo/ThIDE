# Station names, the `.` character, and qualified-name parsing

**Status:** implemented · **Date:** 2026-07-01 · **Fix commit:** `2913fd2` ("fix(semantics): allow '.' in station names (e.g. N32.11)")

This note records why the fix exists and the invariants future changes must preserve, so the bug
is not reintroduced when the naming/resolution code is touched again.

## The problem

Therion **station names may contain `.`** — e.g. `N32.11`, `N32.23`, and even multi-dot names like
`N32.11.i`. Real example (the file that surfaced this):
`tests/Corpus/sample_projects/Therion_202502x/lox/Cerna_lox.th`

```
# declaration (sub-file PS-6nord/.../20181103_ps6b_lox.th, inside `survey SV-ps6b`)
N32.10 N32.11 2.87 225.9 -27.7      # from = N32.10, to = N32.11  (dots are literal)

# cross-survey link (Cerna_lox.th)
equate N32.11@SV-ps6b N32.11@SV-ps6f_labirint_Raul_batran
```

The binder mis-read the dotted from/to `N32.11` as **survey `N32` + station `11`**, indexed the
station under the wrong key, and then the `@` equate (which correctly keeps `N32.11` whole) could
not find it — producing spurious `TH_SEM_001` "unresolved station reference" warnings.

Root cause was a single overloaded assumption: **`.` was always treated as a survey-hierarchy
separator**, when inside a station token it is a literal character.

## The Therion naming rule (what we now follow)

- A **station name is an opaque token**. Dots inside it are literal.
- **Survey hierarchy** is expressed only by:
  1. **survey nesting** (the enclosing `survey … endsurvey` scope), and
  2. the **`@` notation** — `station@inner.outer` (bottom-up). The part **left of `@` is the
     station name** (may contain dots); the part **right of `@` is the survey path** (dot-separated,
     hierarchy). `StationRef` parses this and reverses the survey path to top-down order.
- Therion does **not** use a top-down dotted station reference (`survey.survey.station`) in source
  syntax; cross-survey references use `@`. (We keep a fallback for it anyway — see below.)

## Internal representation & invariants

`QualifiedName` holds `ImmutableArray<string> Parts` — **already-separated** components, the **last
of which is the whole station name** (which may contain dots). The parts array is unambiguous; the
danger is only in *reconstructing* it from a string by splitting on `.`.

**Invariants — do not break these:**

1. **Never split a raw station token on `.`.** To qualify a station token against a survey scope,
   use `QualifiedName.OfStation(scope, name)` (appends the whole token). Do **not** use
   `QualifiedName.Parse` for that.
2. `QualifiedName.Parse(dotted)` **does** split on every `.`. It is only for survey paths and for
   known dotted strings without dotted station components (internal round-trips, tests). Its XML doc
   carries this warning.
3. The lexer already keeps dotted tokens whole — `.` is **not** a tokenizer separator. Keep it that way.
4. Cross-survey resolution goes through `StationRef` (the `@` parser), which keeps the point whole.
   The primary, **unambiguous** index is `StationsBySurveyAndPoint`, keyed on
   `(immediate-survey-last, whole-point)`. `StationsByQn` (full dotted key) is a fallback and is
   theoretically ambiguous for dotted station names — never rely on it as the sole resolver.

## What the fix changed

All in `src/Therion.Semantics/`:

- **`SemanticBinder.QualifyLocal`** — keeps the station token whole via the new
  `QualifiedName.OfStation(scope, name)` factory instead of `if (token.Contains('.')) Parse(token)`.
  This is the essential correctness fix (the declaration side).
- **`SemanticBinder.TryResolveRef`** — resolves the **whole token first**, and only falls back to the
  legacy dotted-path split (`raw.Split('.')`) if the whole token resolves nowhere. So same-file
  dotted equates resolve, and any hypothetical top-down dotted reference still works (no regression).
- **`QualifiedName`** — new `OfStation` factory (documents the rule) and a warning doc on `Parse`.

Not touched: the tokenizer, the AST, `StationRef` (its `@` handling was already correct), and the
`QualifiedName` representation.

## Risks / things to watch

- A **top-down dotted, non-`@`** reference (`equate cave.upper.1 …`) is now read as a literal name
  first. Evidence (corpus + thbook `@`-notation) says this form isn't used in Therion source, and
  `TryResolveRef`'s fallback preserves it if it ever appears.
- If a future feature adds a genuinely hierarchical dotted reference syntax, resolve it explicitly at
  that site — do **not** reintroduce blanket `.`-splitting in `QualifyLocal` or `QualifiedName`.

## Tests

`tests/Therion.Semantics.Tests/DottedStationNameTests.cs`:
- `Station_name_with_dot_is_kept_whole` — declaration keyed `[survey, "N32.11"]`, not `["N32","11"]`.
- `Same_file_equate_of_dotted_station_resolves` — covers `TryResolveRef` whole-token-first.
- `Cross_survey_at_equate_of_dotted_station_resolves` — the reported `@`-equate case.
- `Plain_station_names_are_unaffected` — non-dotted `survey.station` behaviour unchanged.

Verified on the real corpus (`thconfig_Cerna_lox.thc`): the `N32.11` / `N32.23` unresolved-station
warnings disappear and multi-dot names (`N32.11.i`) resolve. The remaining `TH_SEM_012` there is a
genuine zero-length leg in the data, unrelated to this fix.
