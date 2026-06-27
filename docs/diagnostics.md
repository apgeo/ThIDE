# Diagnostic catalog

> Codes never change once shipped.
> Localized messages live in `Therion.Core.Resources` (`Strings.resx` / `Strings.ro.resx`).

## Parser core (`TH00xx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH0001` | Error   | Lexer failed at this position. | `TherionTokenizer` |
| `TH0002` | Warning | Unexpected token `<text>`.     | `TherionTokenizer`, parsers |
| `TH0003` | Error   | Unexpected end of file.        | parsers |
| `TH0010` | Warning (lenient) / Error (strict) | Unknown top-level command `<keyword>`. | `ThconfigParser`, `ThParser`, `Th2Parser` |
| `TH0011` | Warning (lenient) / Error (strict) | Missing block terminator (e.g., `endsurvey`). | `ThParser`, `Th2Parser` |
| `TH0033` | Warning (lenient) / Error (strict) | Unknown data style `<style>` (LANG-05). | `ThParser` |
| `TH0034` | Warning (lenient) / Error (strict) | Unknown data reading `<keyword>` (LANG-05). | `ThParser` |
| `TH0040` | Warning (lenient) / Error (strict) | Malformed `units` (unknown quantity / missing unit). | `ThParser` |
| `TH0041` | Warning (lenient) / Error (strict) | Malformed `calibrate` (missing zero-error). | `ThParser` |
| `TH0043` | Warning (lenient) / Error (strict) | Unknown coordinate system `<cs>` (LANG-03). | `ThParser` |

## Semantics (`TH_SEM_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_SEM_001` | Warning | Unresolved station reference `<name>` (with optional "did you mean" hint). | `SemanticBinder` |
| `TH_SEM_002` | Warning | Station `<name>` is fixed more than once. | `SemanticBinder` |
| `TH_SEM_003` | Warning | Malformed station reference `<name>`. | `SemanticBinder` |
| `TH_SEM_005` | Warning | Data row column count doesn't match its reading order (LANG-05). | `SemanticBinder` |
| `TH_SEM_006` | Error   | Data-row value isn't valid for its reading (e.g. a non-numeric length/compass/clino). | `SemanticBinder` |
| `TH_SEM_007` | Warning | Data-row value is out of range for its reading (e.g. compass > 360°, clino > 180°). | `SemanticBinder` |
| `TH_SEM_010` | Info    | Naming collision: the same survey/map name is declared in more than one file (DIAG-05). | `ProjectDiagnostics` |
| `TH_SEM_011` | Warning | Loop misclosure beyond tolerance — a closed centreline loop doesn't close (DIAG-02). | `ProjectDiagnostics` |
| `TH_SEM_012` | Warning/Info | Blunder/outlier shot: zero-length leg, self-loop, or an implausibly long leg (DIAG-03). | `ProjectDiagnostics` |
| `TH_SEM_013` | Warning | Foresight and backsight disagree beyond tolerance (compass ±180°, clino negated) (DIAG-04). | `ProjectDiagnostics` |
| `TH_SEM_014` | Warning | Dangling include: an `input`/`source` target was not found on disk (DIAG-06). | `ProjectDiagnostics` |
| `TH_SEM_NAMING` | configurable | User naming-convention lint violated (LANG-13). | `NamingConventionRule` |

## XVI format (`TH_XVI_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_XVI_001` | Warning | Referenced image file `<path>` not found.    | `XviIndex` |
| `TH_XVI_002` | Error   | Referenced `.xvi` file `<path>` not found.   | `XviIndex` |
| `TH_XVI_003` | Warning | Affine transform is degenerate (non-invertible). | `XviIndex` |
| `TH_XVI_010` | Warning | Missing XVI `<version>` header line.         | `XviParser` |
| `TH_XVI_011` | Warning | Malformed `SCALE` � expected a single numeric value. | `XviParser` |
| `TH_XVI_012` | Warning | Malformed `TRANSFORM` � expected 6 numeric values. | `XviParser` |
| `TH_XVI_013` | Warning | Missing or malformed `IMAGE` directive.      | `XviParser` |
| `TH_XVI_014` | Warning (lenient) / Error (strict) | Unknown XVI keyword `<keyword>`. | `XviParser` |

## .th2 drawing format (`TH2_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH2_001` | Warning | Malformed `point` � requires x, y and type. | `Th2Parser` |
| `TH2_002` | Warning (lenient) / Error (strict) | `line` block is missing `endline`. | `Th2Parser` |
| `TH2_003` | Warning (lenient) / Error (strict) | `area` block is missing `endarea`. | `Th2Parser` |
| `TH2_004` | Warning (lenient) / Error (strict) | Unknown point type `<type>` (LANG-07). | `Th2Parser` |
| `TH2_005` | Warning (lenient) / Error (strict) | Unknown line type `<type>` (LANG-07). | `Th2Parser` |
| `TH2_006` | Warning (lenient) / Error (strict) | Unknown area type `<type>` (LANG-07). | `Th2Parser` |
| `TH2_010` | Warning (lenient) / Error (strict) | `scrap` block is missing `endscrap`. | `Th2Parser` |

## Workspace (`TH_WS_xxx`)

| Code | Severity | Message | Source |
|---|---|---|---|
| `TH_WS_001` | Error   | Path not found: `<path>`.                    | `ProjectEntryPointResolver` |
| `TH_WS_002` | Warning | No Therion configuration file found in `<folder>`. | `ProjectEntryPointResolver` |
