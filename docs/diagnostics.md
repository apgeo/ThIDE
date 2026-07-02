# Diagnostic catalog

> Localized messages live in `Therion.Core.Resources` (`Strings.resx` / `Strings.ro.resx`).
> Severities below are the lenient-mode defaults; in `ParserMode.Strict` the "lenient" rows
> are promoted to Error.

## Parser core (`TH00xx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0002` | Warning | Unexpected token `<text>`.     | `ThconfigParser` |
| `TH0010` | lenient | Unknown command `<keyword>`. | `ThconfigParser`, `ThParser`, `Th2Parser` |
| `TH0020` | lenient | Block `<x>` is missing its terminator (e.g. `endsurvey`). | `ThParser` |
| `TH0021` | Error   | Mismatched block terminator (found a different `end…`). | `ThParser` |
| `TH0030` | lenient | Malformed `fix` (missing or non-numeric `x`/`y`/`z`). | `ThParser` |
| `TH0031` | Warning | Malformed `equate` (needs at least two stations). | `ThParser` |
| `TH0032` | Warning | Malformed `data` (needs at least a style name). | `ThParser` |
| `TH0033` | lenient | Unknown data style `<style>`. | `ThParser` |
| `TH0034` | lenient | Unknown data reading `<keyword>`. | `ThParser` |
| `TH0037` | lenient | Single-column line in a centreline (`<tok>`): not a valid command or survey shot. | `ThParser` |
| `TH0038` | lenient | Malformed `sd` (needs `<quantity…> <value> <unit>`). | `ThParser` |
| `TH0039` | lenient | Malformed `grid-angle` / `vthreshold` (needs a numeric value). | `ThParser` |
| `TH0040` | lenient | Malformed `units` (unknown quantity / missing unit). | `ThParser` |
| `TH0041` | lenient | Malformed `calibrate` (missing zero-error). | `ThParser` |
| `TH0042` | lenient | Malformed `declination` (needs a numeric value / dated list / `-`). | `ThParser` |
| `TH0043` | lenient | Unknown coordinate system `<cs>`. | `ThParser`, `ThconfigParser` |
| `TH0056` | lenient | Invalid `infer` spec (expects `<plumbs\|equates> <on\|off>`). | `ThParser` |

### Identifiers & block matching

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0050` | lenient | Identifier contains a character outside the keyword/ext-keyword set. | `ThParser` (survey/map), `Th2Parser` (scrap) |
| `TH0051` | lenient | `endsurvey`/`endscrap` id does not match the opener. | `ThParser`, `Th2Parser` |

### Centreline argument enums

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0052` | lenient | Unknown shot flag (`flags`). | `ThParser` |
| `TH0053` | lenient | Unknown mark type (`mark`). | `ThParser` |
| `TH0054` | lenient | Unknown extend spec (`extend`). | `ThParser` |
| `TH0055` | lenient | Unknown station flag (`station`). | `ThParser` |

### .thconfig command arguments

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0060` | lenient | Unknown export type (`export <type>`). | `ThconfigParser` |
| `TH0061` | lenient | `-fmt` value invalid for the export type. | `ThconfigParser` |
| `TH0062` | lenient | Unknown layout option key. | `LayoutBodyParser` (`.th` + `.thconfig`) |

### Schema-driven validation (`SchemaValidator`, syntax-coverage effort)

Emitted by the declarative schema pass (`Therion.Syntax/Schema/`); populated incrementally —
see `.claude/therion-syntax/PLAN.md`. Toggleable per category/section via
`ParserOptions.Validation` (`SchemaValidationOptions`); perf notes in `.claude/therion-syntax/PERF.md`.

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0063` | Error | Command has fewer arguments than required. | `SchemaValidator` |
| `TH0064` | Warning | Command has more arguments than allowed. | `SchemaValidator` |
| `TH0065` | lenient | Argument/option value fails its declared type or enum. | `SchemaValidator` |
| `TH0066` | lenient | Option not valid for this object type (e.g. `-text` on a non-label point; per-type matrix from thpoint.cxx). | `Th2PointRules` |
| `TH0067` | Info | Right keyword, wrong case (Therion's `thmatch_token` is case-sensitive). Fires for every schema enum table — the stored spelling is recovered even through our case-insensitive lookups; tables Therion matches with `thcasematch_token` opt out via `ValueSpec.CaseSensitive = false`. | `SchemaValidator` |
| `TH0068` | Warning | Not a number nor a special value (`-` `.` NaN Inf up down). | `SchemaValidator` |
| `TH0069` | Warning | Numeric value outside the schema's range (vthreshold 0–90, fix sd > 0, …). | `SchemaValidator`, `ThCentrelineRules` |

`ThCentrelineRules` (typed centreline nodes; section `centreline`) also emits: `TH0064` (fix > 7
args, extend > 3 stations, > 21 readings — `THDATA_MAX_ITEMS` is 22 but counts the style token),
`TH0065` (unknown team role / instrument quantity, sd/units length↔angle class mix), `TH0042`
(numeric declination without units), `TH0055` (direct `fixed` flag; `explored` without
`continuation`).

### `data <style> <readings>` order validation (`ThCentrelineRules`, lenient)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0070` | lenient | Reading not valid for the data style (e.g. `gradient` outside `normal`). | `DataStyles.ValidateOrder` |
| `TH0071` | lenient | Reading listed twice (aliases count: `tape` ≡ `length`). | `DataStyles.ValidateOrder` |
| `TH0072` | lenient | "Not all data for given style" — required readings absent. | `DataStyles.ValidateOrder` |
| `TH0073` | lenient | `newline` cannot be the first or last reading. | `DataStyles.ValidateOrder` |
| `TH0074` | lenient | `station` mixed with `from`/`to`; interleaved reading after `newline`; or a non-interleaved (shot) reading before `newline` in interleaved data. | `DataStyles.ValidateOrder` |

## Semantics (`TH_SEM_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_SEM_001` | Warning | Unresolved station reference `<name>` (with optional "did you mean" hint). Equate references are validated workspace-wide, so cross-file / `@`-qualified targets resolve across the project and this only fires when a reference resolves nowhere. | `SemanticBinder` (standalone) / `WorkspaceSemanticModel.ValidateEquateReferences` (project) |
| `TH_SEM_002` | Warning | Station `<name>` is fixed more than once. | `SemanticBinder` |
| `TH_SEM_003` | Warning | Malformed station reference `<name>`. | `SemanticBinder` |
| `TH_SEM_005` | Warning | Data row column count doesn't match its reading order. | `SemanticBinder` |
| `TH_SEM_006` | Error   | Data-row value isn't valid for its reading (non-numeric length/compass/clino). | `SemanticBinder` |
| `TH_SEM_007` | Warning | Data-row value out of range for its reading (compass > 360°, clino > 180°). | `SemanticBinder` |
| `TH_SEM_010` | Info    | Naming collision across files (DIAG-05). | `ProjectDiagnostics` |
| `TH_SEM_011` | Warning | Loop misclosure beyond tolerance (DIAG-02). | `ProjectDiagnostics` |
| `TH_SEM_012` | Warning/Info | Blunder/outlier shot (DIAG-03). | `ProjectDiagnostics` |
| `TH_SEM_013` | Warning | Foresight/backsight disagreement (DIAG-04). | `ProjectDiagnostics` |
| `TH_SEM_014` | Warning | Dangling include: an `input`/`source` target not found (DIAG-06). | `ProjectDiagnostics` |
| `TH_SEM_NAMING` | configurable | User naming-convention lint violated (LANG-13). | `NamingConventionRule` |

## XVI format (`set XVI*` Tcl export)

Syntax layer (`Therion.Syntax`):

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_XVI_001` | lenient | Unknown `set XVI…` variable. | `XviParser` |
| `TH_XVI_002` | lenient | Unexpected non-`set` statement. | `XviParser` |
| `TH_XVI_003` | Error   | `{` block missing its closing `}`. | `XviParser` |
| `TH_XVI_004` | lenient | `XVIgrid` not 8 numeric values. | `XviParser` |

Semantic layer (`Therion.Semantics`):

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_XVI_050` | Error   | A `-sketch` target referenced from a `.th2` scrap was not found on disk. | `XviIndex` |

## .th2 drawing format (`TH2_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH2_001` | lenient | Malformed `point` (needs x, y, type; coordinates must be numeric). | `Th2Parser` |
| `TH2_002` | lenient | `line` block is missing `endline`. | `Th2Parser` |
| `TH2_003` | lenient | `area` block is missing `endarea`. | `Th2Parser` |
| `TH2_004` | lenient | Unknown point type `<type>`. | `Th2Parser` |
| `TH2_005` | lenient | Unknown line type `<type>`. | `Th2Parser` |
| `TH2_006` | lenient | Unknown area type `<type>`. | `Th2Parser` |
| `TH2_008` | lenient | Invalid subtype for the point type (only station/air-draught/water-flow/u: take subtypes; per-type value matrix from thpoint.cxx). | `Th2PointRules` |
| `TH2_009` | lenient | Unknown `-option` on a point/line/area object. | `Th2Parser` |
| `TH2_010` | lenient | `scrap` block is missing `endscrap`. | `Th2Parser` |

## Workspace (`TH_WS_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_WS_001` | Error   | Path not found: `<path>`.                    | `ProjectEntryPointResolver` |
| `TH_WS_002` | Warning | No Therion configuration file found in `<folder>`. | `ProjectEntryPointResolver` |
