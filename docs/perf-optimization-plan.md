# Performance optimization plan

Targets: the parsers, syntax checker, workspace analysis, heavy lookups, and object-graph
processing (search/connectivity). This document holds the grouped optimization plan, the Phase 0
measurement baselines, and how to reproduce them.

Two distinct performance stories:

- **Throughput** (CLI, initial load, batch): dominated by per-token / per-line / per-value
  allocations and `QualifiedName` hashing.
- **Interactive latency** (felt while editing): dominated by the workspace re-binding the *whole*
  project on every change (`TherionWorkspace.OnFileChanged` → `WorkspaceSemanticModel.Build`).

## Phase 0 — measurement baselines (recorded 2026-07-01)

Captured on the synthetic single-survey centreline (`s0→s1→…`), Release, net8.0. The
BenchmarkDotNet `Allocated` column and the test-side `GC.GetAllocatedBytesForCurrentThread`
deltas agree to within rounding.

| Scenario | Time | Allocated |
|---|---|---|
| Tokenize 5 000 legs | 3.5 ms | **10.04 MB** |
| Tokenize 20 000 legs | 21.2 ms | **40.32 MB** |
| Parse 5 000 legs | — | **18.47 MB** |
| Bind 20 000 legs | — | **14.84 MB** |
| Parse + Bind 20 000 legs | — | **89.00 MB** (parse ~74 MB dominates, bind ~15 MB) |

Key takeaway: **lexing + parsing is ~80% of end-to-end allocation**; the tokenizer alone is ~half
of parse. Group A is therefore the highest-leverage throughput change.

These baselines are encoded as ratcheting ceilings in:

- `tests/Therion.Syntax.Tests/AllocationGuardTests.cs`
- `tests/Therion.Semantics.Tests/AllocationGuardTests.cs`

Lower the budgets as each group lands so wins are locked in and regressions fail CI.

## Optimization groups (priority order)

| Group | Change | Win | Code cost | Risk | Priority |
|---|---|---|---|---|---|
| D | `QualifiedName`: cache hash + `ToString`; cheaper construction | High | Low | Low | **P0** |
| A1 | Tokenizer: don't store text for whitespace/newline/continuation | High | Trivial | Low | **P0** |
| G1 | Cache per-file `SemanticModel`; only re-bind changed files | High (latency) | High | Med | **P0** |
| A2 | Tokenizer: lazy token text (offset+length over source) | High | Moderate | Med | P0/P1 |
| C | Keyword dispatch via `FrozenDictionary`, no `ToLowerInvariant` | Med | Low | Low | P1 |
| E | Classify each `data` column once; reuse per row | Med | Low | Low | P1 |
| I | `ConnectivityGraph`: O(1) `AreConnected` via component ids; sort without `ToString` | Med | Low | Low | P1 |
| B | Logical-line slices instead of per-line arrays | Med | Moderate | Med | P1 |
| H | Reference index keys without `ToString`; rebuild only on symbol change | Med | Low | Low | P2 |
| J | Navigation: resolve via frozen index, not per-file walk; parse name once | Med | Low–Mod | Low | P2 |
| F | `CoalesceValues` fast path (skip StringBuilder when nothing is adjacent) | Low–Med | Low | Low | P2 |
| K | GC/JIT flags (TieredPGO, ServerGC for CLI only) — measure first | Low–Med | Trivial | Low | P2 |
| L | MessagePack disk cache as default; cache bind results | Low–Med | Low | Low | P3 |

Anchor references for each group live in the code:
`TherionTokenizer.cs`, `LogicalLine.cs`, `ThParser.cs`, `SemanticBinder.cs`, `QualifiedName.cs`,
`ConnectivityGraph.cs`, `ReferenceIndexBuilder.cs`, `WorkspaceSemanticModel.cs`,
`TherionWorkspace.cs`, `WorkspaceSymbolNavigationService.cs`, `DataReadingValidation.cs`.

## Phases

- **Phase 0 — measurement (this doc + harness).** Done.
- **Phase 1 — safe throughput:** D1/D2, A1, C, E, I.
- **Phase 2 — interactive latency:** G1, G3, then J, H.
- **Phase 3 — structural:** A2, B, G2, L, K.

## How to run

### Benchmarks (time + allocation + GC), no VS required

BenchmarkDotNet **must** run in Release.

```
dotnet run -c Release --project benchmarks/Therion.Benchmarks                       # interactive menu
dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --list flat        # list benchmarks
dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter *Bind*
dotnet run -c Release --project benchmarks/Therion.Benchmarks -- --filter * --job short   # quick pass
```

Record the `Mean` and `Allocated` deltas per group. Use `--job short` while iterating; a default
run for the numbers you publish.

### Allocation-guard tests (fast, CI-friendly)

```
dotnet test tests/Therion.Syntax.Tests   -m:1 -c Release --filter FullyQualifiedName~AllocationGuardTests
dotnet test tests/Therion.Semantics.Tests -m:1 -c Release --filter FullyQualifiedName~AllocationGuardTests
```

(The existing `PerformanceSmokeTests` / `SemanticPerfTests` keep the wall-clock budgets.)

### Finding the next hotspot (free, cross-platform)

- `dotnet-trace collect -- <cmd>` → open in **speedscope** or **PerfView** (CPU sampling).
- `dotnet-counters monitor` → live alloc rate / Gen0-1-2 / time-in-GC.
- `dotnet-gcdump collect` → heap snapshot (retained `QualifiedName` / token / string analysis).
- PerfView "GC Heap Alloc" (Windows/ETW) → exact allocation call sites.

**VS2026 is not required.** Its Performance Profiler (CPU Usage + .NET Object Allocation Tracking)
is convenient and in every edition, but the CLI tools above are better for tracking per-change
deltas, which is the point of this harness.
