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
| `TH2_008` | lenient | _(reserved)_ Unknown point/line/area subtype. | `Th2Parser` |
| `TH2_009` | lenient | Unknown `-option` on a point/line/area object. | `Th2Parser` |
| `TH2_010` | lenient | `scrap` block is missing `endscrap`. | `Th2Parser` |

## Workspace (`TH_WS_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_WS_001` | Error   | Path not found: `<path>`.                    | `ProjectEntryPointResolver` |
| `TH_WS_002` | Warning | No Therion configuration file found in `<folder>`. | `ProjectEntryPointResolver` |
