# Synthetic Therion sample corpus

> Implementation Plan §11 (Testing) + Decision #6 (Sample corpora bundled).
> Stand-in for the external repos (`therion samples/`, `jarvist/migovecsurveydata`,
> `tr1813/migresurvey`, `iccaving/migovec-survey-data`, `apgeo/alpha`) until
> those are vendored under their own LICENSE.txt.

Each `.th` / `.th2` / `.thconfig` file here is parsed by the corpus tests and is
asserted to produce **zero error-severity diagnostics** in lenient mode.

Files here are deliberately small but exercise:
- comments, blank lines, line continuations
- nested `survey` / `centreline` blocks
- `data normal` shot rows
- `fix` and `equate`
- `input` cross-file references
- `.thconfig` with `source` / `layout` / `export`
