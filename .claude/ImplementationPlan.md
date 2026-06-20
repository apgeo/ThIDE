# Therion Interpreter / Parser / Processor — Implementation Plan

> Status: **Draft v0.10** — closes Post-M6 follow-ups C (.NET 10 readiness scaffolding, opt-in multi-targeting) and D (MessagePack disk-cache backend; disk cache default flipped to **disabled**, opt-in via env var).
> Target runtime: **.NET 8 (LTS)** today, with a planned upgrade path to **.NET 10 (LTS)** when GA.
> UI: **Avalonia 12.x** (MVVM, CommunityToolkit.Mvvm), cross-platform (Windows / Linux / macOS).
> Parser: **Superpower**.
> Default parser mode: **lenient** (strict mode opt-in via configuration).
> Default units: **metres** (canonical), original units preserved on the AST.
> Localization: **English + Romanian** (i18n from day one, including caving-domain terms).
> Therion source of truth: <https://github.com/therion/therion> (pinned by git tag, SHA recorded in `TherionVersion.json`, no vendored copy).
> `thbook` source of truth: <https://github.com/therion/therion/tree/master/thbook> (TeX) + released PDF (e.g., `thbook-v6.4.0.pdf`).

---

## 1. High-level architecture

The system is split into **fully decoupled** layers. The lower layers have **no UI / no Avalonia / no platform** dependencies and can be reused from a Minimal API host, CLI, or tests.

```
+--------------------------------------------------------------+
|  TherionProc                 (Avalonia UI — desktop app)     |
|  - Views / ViewModels / DI composition root                  |
+----------------------↑---------------------------------------+
                       | (interfaces only)
+--------------------------------------------------------------+
|  Therion.Workspace            (in-memory project / session)  |
|  - Loaded file set, cross-file resolution, change notif.     |
+----------------------↑---------------------------------------+
                       |
+--------------------------------------------------------------+
|  Therion.Semantics            (cross-file model + indexes)   |
|  - Symbol tables (stations, surveys, scraps, maps, configs)  |
|  - Reference resolution, scoping, validation rules           |
+----------------------↑---------------------------------------+
                       |
+--------------------------------------------------------------+
|  Therion.Syntax               (per-file AST + diagnostics)   |
|  - Lexer + parser (Superpower), file-level model             |
|  - Pluggable command handlers, syntax version registry       |
+----------------------↑---------------------------------------+
                       |
+--------------------------------------------------------------+
|  Therion.Core                 (primitives, shared types)     |
|  - SourceSpan, Diagnostic, units, identifiers, results       |
+--------------------------------------------------------------+
```

### Projects (proposed solution layout)

| Project | TFM | Purpose |
|---|---|---|
| `Therion.Core` | `net8.0` | Primitives: `SourceSpan`, `Diagnostic`, `Severity`, `Identifier`, `Unit`, `Result<T>`, `IFileSource`. Zero deps. |
| `Therion.Syntax` | `net8.0` | Lexer + Superpower parsers for `.th`, `.th2`, `.thconfig`, `.xvi`. AST nodes. Diagnostics. Syntax-version registry. |
| `Therion.Semantics` | `net8.0` | Cross-file resolution, symbol tables, indexes (stations, surveys…), validation passes. |
| `Therion.Workspace` | `net8.0` | Project/session abstraction: file watcher, cache, incremental rebuild orchestration, entry-point discovery & sniffer. |
| `Therion.Build` | `net8.0` | Wraps the installed Therion toolchain: discovery, compilation, output parsing, artifact collection, viewer launching. UI-agnostic. |
| `Therion.Processing.Abstractions` | `net8.0` | Public interfaces (`ITherionParser`, `IWorkspace`, `ISymbolIndex`, `ITherionCompiler`, …) for consumers. |
| `Therion.Cli` *(optional, later)* | `net8.0` | Headless validation / dump tool. Useful for smoke tests and CI. |
| `TherionProc` (existing) | `net8.0` | Avalonia desktop UI. Depends only on `*.Abstractions` + DI registration of concrete impls. |
| `Therion.Syntax.Tests` | `net8.0` | xUnit tests, including corpus tests against public Therion sample repos. |
| `Therion.Semantics.Tests` | `net8.0` | xUnit. |
| `Therion.Workspace.Tests` | `net8.0` | xUnit. |

> **Rule:** `TherionProc` only references `*.Abstractions` projects + the composition root wires concrete implementations. This guarantees decoupling and enables reuse from a future Minimal API.

---

## 2. Therion syntax versioning

Therion's grammar evolves with the `therion` CLI version and the `thbook` documentation. We must:

- Expose a `TherionSyntaxVersion` value (e.g., `6.3.0`) on every produced model and diagnostic.
- Make command/keyword tables **registry-based** (`ICommandRegistry`) keyed by version, so adding a new command does **not** require recompiling the parser core.
- Default version comes from configuration; `.thconfig` may override per workspace.
- Each `CommandDefinition` references its **source of truth** (e.g., `therion/src/thsurvey.cxx@<hash>` or `thbook §X.Y`) via metadata for traceability.

```csharp
public sealed record TherionSyntaxVersion(int Major, int Minor, int Patch, string Source);
// Source = "therion-src@<git-sha>" or "thbook@<version>"
```

---

## 3. File formats — scope & strategy

| Format | Purpose | Notes |
|---|---|---|
| `.th` | Survey data: `survey`, `centreline`, `data`, `station`, `import`, `equate`, `fix`, `team`, `date`, comments… | Most complex. Block-structured. |
| `.th2` | 2D scrap/map drawing: `scrap`, `point`, `line`, `area`, `endscrap`. Coordinate-bearing. | Many enumerated types per shape. References `.xvi` for background images. |
| `.thconfig` | Project configuration: `source`, `layout`, `export`, `select`, … | Driver of cross-file graph (`source` directives). |
| `.xvi` | **Geo-referenced raster image metadata**: maps a background survey scan (`.jpg`/`.png`/`.bmp`) to Therion survey coordinates via a 2D affine transform. Written by `xtherion` / Therion when exporting a sketch. Read when loading a `.th2` scrap for tracing. | Fixed-structure key-value text format (see §3.1). Both input and output artifact. |

**Common low-level lexical rules** (shared lexer):
- Line comments: `#` to EOL (unless inside a string).
- Multi-line comments: `[comment ... endcomment]`-style blocks where applicable (verify per Therion source).
- Line continuation: backslash at EOL.
- Encoding directive: `encoding <name>` must be respected before further parsing.
- Identifiers, numbers (with units), quoted strings, keywords.

Each format gets its **own parser module** but shares the lexer and primitives.

### 3.1 XVI format structure

`.xvi` is a **fixed-structure plain-text** georeferencing descriptor written by `xtherion` (Therion's Tcl/Tk editor). Key structural facts (source of truth: `xtherion` source + `thbook`):

```
# XTherion export file
XVI 1
SCALE <px-per-metre>
TRANSFORM <a> <b> <c> <d> <tx> <ty>   # 2D affine transform (survey ← pixel)
IMAGE <relative-path-to-image>         # .jpg / .png / .bmp alongside the .xvi
CALIBRATION <x1> <y1> <px1> <py1> ...  # optional ground-control points (≥ 2)
```

- All coordinates are in **Therion survey coordinate space** (same CRS as the referencing `.th2` scrap).
- The image path is **relative** to the `.xvi` file; absolute paths allowed but unusual.
- A `.th2` scrap can reference one or more `.xvi` files via the scrap `sketch <xvi-path> <transform-options>` option (exact syntax: Therion source `thsketch.cxx`).
- XVI files are **output artifacts** when Therion exports a sketch (§9bis.3) and also **input** when `.th2` files are processed for tracing.

---

## 4. Parsing layer (`Therion.Syntax`)

### 4.1 Lexer

- Implemented with **Superpower** `Tokenizer<TherionToken>`.
- Produces a flat stream of `Token<TherionTokenKind>` with absolute `SourceSpan` for **excellent diagnostics**.
- Handles encoding (`encoding xxx`) before tokenizing the rest — likely a two-phase read (prelude scan, then re-decode bytes with the declared encoding).
- **XVI exception**: `.xvi` files use a simpler non-Therion lexical structure (fixed-keyword lines, no block nesting). They are tokenized with a **dedicated lightweight tokenizer** (`XviTokenizer`) that shares `SourceSpan` and `Diagnostic` primitives from `Therion.Core` but does not reuse the main Therion tokenizer.

### 4.2 Parser

- Superpower `TokenListParser<TKind, T>` per command/construct.
- **Error recovery:** synchronize on **end-of-line** and **block terminators** (`endsurvey`, `endcentreline`, `endscrap`, `endcomment`). This keeps error reports localized and lets one bad command not poison the whole file.
- **Diagnostics-first design:** every parse failure produces a structured `Diagnostic` with:
  - `Severity` (`Error`, `Warning`, `Info`, `Hint`),
  - `Code` (e.g., `TH0007`),
  - `Message`, `ExpectedTokens`, `SourceSpan`, `RelatedSpans`, optional `HelpUri`.
- A `ParseResult { AstFile File; ImmutableArray<Diagnostic> Diagnostics; }` is returned **even on errors** (partial AST).

#### Parser modes — **lenient** (default) vs **strict**

Per requirement, the default mode is **lenient** to maximize the amount of model produced from imperfect input. Both modes are configurable per workspace / per parse call via `ParserOptions.Mode`:

| Behavior | Lenient (default) | Strict |
|---|---|---|
| Unknown command | `Warning` + skip to next line, keep parsing | `Error`, no model node emitted |
| Unknown option/flag | `Warning`, keep value as raw trivia | `Error` |
| Missing block terminator | `Warning`, auto-close at EOF/next top-level | `Error`, no recovery for the block |
| Unknown enum value (e.g., point type) | `Warning`, model stores raw string | `Error` |
| Deprecated syntax | `Info` / `Hint` | `Warning` (or `Error` if dialect says so) |

`ParserOptions` is plumbed through DI and exposed in the UI as a toggle in workspace settings.

### 4.3 AST shape (granular, immutable)

Top-level abstractions:

```csharp
public abstract record TherionNode(SourceSpan Span);
public sealed record TrivialComment(SourceSpan Span, string Text) : TherionNode(Span);

public abstract record TherionCommand(SourceSpan Span, string Keyword) : TherionNode(Span);
public sealed record SurveyCommand(...) : TherionCommand(...);
public sealed record CentrelineCommand(...) : TherionCommand(...);
public sealed record DataRow(...) : TherionNode(...);          // a single shot
public sealed record StationFix(...) : TherionCommand(...);
public sealed record ImportCommand(...) : TherionCommand(...); // .thconfig source/input
public sealed record ScrapBlock(...) : TherionCommand(...);    // .th2
public sealed record SketchReference(...) : TherionNode(...);  // .th2 scrap sketch <xvi-path> ...
public sealed record PointObject(...) : TherionNode(...);      // .th2 point
public sealed record LineObject(...) : TherionNode(...);       // .th2 line + segments
public sealed record AreaObject(...) : TherionNode(...);       // .th2 area
// ...etc

// XVI AST — separate hierarchy, shares TherionNode base
public sealed record XviFile(
    SourceSpan Span,
    int Version,                          // e.g. 1
    double Scale,                         // px per metre
    AffineTransform2D Transform,          // survey-from-pixel
    string ImageRelativePath,
    ImmutableArray<CalibrationPoint> CalibrationPoints,
    ImmutableArray<TrivialComment> LeadingComments
) : TherionNode(Span);

public sealed record AffineTransform2D(double A, double B, double C, double D, double Tx, double Ty);
public sealed record CalibrationPoint(double SurveyX, double SurveyY, double PixelX, double PixelY);
```

Design choices:
- **`record` types** for value-based equality (useful for caching/diffing).
- **Granular**: every option/flag/parameter is its own typed property (no string blobs).
- Comments and whitespace **preserved** as `Trivia` attached to nodes — needed for the future **emitter / writer** (round-trip).
- Every node carries `SourceSpan` for UI navigation and diagnostics.

### 4.4 Extensibility

- `ICommandHandler` plugin interface:
  ```csharp
  public interface ICommandHandler {
      string Keyword { get; }
      TherionSyntaxVersion MinVersion { get; }
      ParseResult<TherionCommand> Parse(ParseContext ctx);
  }
  ```
- Handlers are registered via DI (`AddTherionCommand<TSurveyHandler>()`) so new commands or experimental dialects can be added without editing core.
- A `IDialect` abstraction allows future variants (e.g., a strict "Therion 7" dialect, or a vendor-specific extension).

### 4.5 Caching

Whole-file reparse is the unit of work (confirmed). The point of the cache is to **avoid re-parsing the many other untouched files** in a large project.

Two-level cache:

1. **In-memory** keyed by `(absolutePath, length, lastWriteUtc, contentHashOptional)` → `ParseResult`.
2. **On-disk** under `%LocalAppData%/TherionProc/cache/` (Windows) / `~/.cache/therionproc/` (Linux) / `~/Library/Caches/TherionProc/` (macOS), using a compact binary format (**MessagePack**).
   - **Production / normal debugging:** disk cache **on** by default.
   - **Deep parser/semantic debugging:** disk cache can be **skipped** via `--no-cache` CLI flag, `TherionWorkspaceOptions.DisableDiskCache = true`, or env var `THERIONPROC_NO_CACHE=1`. In-memory cache can be flushed via `IWorkspace.InvalidateAll()`.
   - Each cache entry stores the `TherionSyntaxVersion` it was produced with → mismatching versions invalidate automatically.

Invalidation:
- File system watcher in `Therion.Workspace`.
- `thconfig` change → invalidates dependent set.
- Parser/semantic version bump → cache schema version bumped → old entries ignored.

---

## 5. Semantic layer (`Therion.Semantics`)

Cross-file model built on top of `ParseResult` for each file.

### 5.1 Symbol tables / indexes

- `StationIndex`: `Dictionary<QualifiedStationName, StationSymbol>` with **hash lookup O(1)**.
- `SurveyIndex`: tree of nested surveys (Therion surveys are hierarchical → `parent.child.station` naming).
- `ScrapIndex`, `MapIndex`, `EquateGraph`.
- `XviIndex`: `Dictionary<string, XviSymbol>` keyed by **resolved absolute path** of the `.xvi` file. Each entry holds the parsed `XviFile` AST, the resolved absolute image path, and the list of `ScrapBlock` nodes that reference it. Enables:
  - Detecting missing image files (image path unresolvable → `Warning TH_XVI_001`).
  - Finding all scraps that use a given background scan.
  - Cross-navigating: select an `.xvi` in the workspace tree → see all referencing scraps.
- `FileGraph`: directed graph of file → file references (via `.thconfig source`, `.th input`/`load`, and **`.th2` scrap → `.xvi` → image** edges).

Indexes are **immutable snapshots** rebuilt incrementally on file changes (Roslyn-like compilation model).

### 5.2 Resolution passes

Pipeline (each pass produces diagnostics):

1. **Bind** — attach symbols to declarations.
2. **Resolve references** — every station/survey/map reference must resolve; unresolved → diagnostic with the closest match (Levenshtein) as a hint. **XVI image paths are resolved here**: relative paths are resolved against the `.xvi` file's directory; missing images produce `Warning TH_XVI_001`; missing `.xvi` files referenced from `.th2` scraps produce `Error TH_XVI_002`.
3. **Validate** — semantic checks (e.g., duplicate stations, conflicting `fix`, `equate` cycles). For XVI: validate the affine matrix is invertible (non-degenerate transform → `Warning TH_XVI_003`), calibration point count ≥ 2 when present, scale > 0.
4. **Topology** *(future)* — connectivity, loop closures (out of initial scope; design hooks now).

### 5.3 Validation rules as plugins

`ISemanticRule { string Id; Diagnostic[] Run(SemanticContext ctx); }`
Registered via DI → easy to add/disable rules, write user-defined ones.

---

## 6. Workspace layer (`Therion.Workspace`)

- `ITherionWorkspace` holds the **current set of opened files**, the parser cache, the semantic snapshot, and raises `WorkspaceChanged` events.
- Loading a `.thconfig` automatically discovers and parses referenced files (BFS over `source` directives).
- Thread-safety: parsing is CPU-bound and parallelizable per-file. Semantic build is single-writer with copy-on-write snapshots.
- All public API is **async** (`ValueTask<…>`).

### 6.1 Project entry-point discovery

A Therion configuration file is **not necessarily named `*.thconfig`** — historically many caver workflows use no extension at all (just `thconfig`) or a custom name. The workspace must handle this flexibly.

`IProjectEntryPointResolver` (in `Therion.Workspace`) supports three modes, all configurable per `WorkspaceOptions.EntryPointDiscovery`:

1. **Open file**: user picks any file directly. If it parses as a configuration (`source` / `layout` / `export` / `select` at top level), it is accepted regardless of extension.
2. **Open folder**: scans the folder (**non-recursive by default**, configurable max depth, default `3` when recursion is enabled) with the following ordered strategy:
   1. Files **with no extension** are inspected first (Therion convention `thconfig`).
   2. Files with `.thconfig` or `.thc` extension.
   3. **Syntax-based autodetect** on remaining files: a lightweight "is-this-a-thconfig?" probe runs the lexer on the first N tokens and checks for top-level config commands (`source`, `layout`, `export`, `select`, `cs`). The probe is implemented as `IThconfigSniffer` and is itself extensible.
3. **Explicit**: user provides a path via CLI (`therion-cli --config <path>`) or sidecar setting.

**Sniffer guardrails** (code constants, easily tweakable):
- `SnifferMaxFileSizeBytes = 64 * 1024` — files larger than 64 KB are skipped.
- `SnifferBinaryProbeBytes = 4 * 1024` — first 4 KB inspected; if it looks binary (NUL bytes / high ratio of control chars), file is skipped.
- `SnifferMaxTokens = 256` — only the first N tokens are examined.

Resolution outcomes:
- **0 candidates** → diagnostic + offer "Open file…" fallback.
- **1 candidate** → loaded automatically.
- **>1 candidates** (any mix of extensioned / no-extension / sniffed) → UI prompts the user to choose; CLI fails with a list and asks for `--config`. Choices are remembered per folder in user settings.

The sniffer is conservative (returns `Likely | Unlikely | Unknown`) and never throws — files that fail to lex are skipped silently in autodetect mode.

### 6.2 Settings storage

Per-workspace settings (parser mode, canonical units, cache toggle, UI language, Therion executable paths, layout, …) live in **both** locations, with sidecar overriding profile:

1. **Sidecar** `.thp.json` next to the entry-point file — **default write target**, version-controlled with the project.
2. **User profile** (`%AppData%/TherionProc/settings.json` etc., per OS) — global defaults.

Merge order at load: defaults → user profile → sidecar → CLI flags / env vars (highest).

---

## 7. UI layer (`TherionProc`, Avalonia 11)

### 7.1 Composition

- **MVVM** via CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`).
- **DI** via `Microsoft.Extensions.DependencyInjection` — composition root in `App.axaml.cs`.
- ViewModels depend only on `Therion.Processing.Abstractions`.

### 7.2 Main shell — Dock.Avalonia (confirmed)

Per requirement, cross-platform third-party widgets are acceptable when they beat stock. We adopt **Dock.Avalonia** for the shell: full VS-style **tabbed + dockable + floating** layout, works on Windows / Linux / macOS, actively maintained.

- Layout state is persisted per workspace (`%AppData%/TherionProc/layout.json`).
- Each "document" implements `IDocumentViewModel` (host-agnostic) so we can swap docking implementations later without rewriting VMs.
- Tool windows (Workspace Explorer, Diagnostics, Object Browser, Properties) are `IToolViewModel`.

### 7.3 Key views

1. **Workspace Explorer** (tool, left): tree of files from the active `.thconfig`, with diagnostics badges.
2. **File Editor** (document, tab per file): **AvaloniaEdit** with:
   - Syntax highlighting for `.th` / `.th2` / `.thconfig` (custom highlight definition driven by the lexer's token kinds — single source of truth).
   - Squiggles from diagnostics.
   - **Click actions on terms**: Ctrl+Click / F12 → **Go to Definition** (station, survey, scrap, map, file source). Right-click → **Find All References**, **Peek**, **Open Containing File**.
   - These actions are implemented as `ISymbolNavigationService` in the abstractions layer (UI-agnostic), backed by the semantic indexes from §5.
   - **Outline** panel showing the AST as a tree.
3. **Object Browser** (tool): filterable/sortable/groupable lists for each entity type:
- **Stations**, **Surveys**, **Shots (legs)**, **Fixes**, **Equates**, **Scraps**, **Points**, **Lines**, **Areas**, **XVI References**.
- The **XVI References** view shows every `.xvi` in the project as a row: path (relative to project), resolved image file, transform summary (scale + shift), calibration point count, referencing scrap(s). Clicking a row navigates to the `SketchReference` node in the `.th2` editor; double-clicking opens the `.xvi` source.
- **Inline edit of data-object rows in the list** (per requirement). Edits go through a `IModelEditService` that:
     - validates the new value against the same parser/semantic rules,
     - produces either a textual edit to the underlying file (round-trip via the emitter), or
     - stages the change in a pending-edit buffer if the emitter isn't ready for that node yet (M6+).
4. **Diagnostics panel** (tool, bottom): all diagnostics with click-to-navigate, filter by severity/file/code.
5. **Properties panel** (tool, right): detail editor for the selected object (form-style, same `IModelEditService`).

### 7.4 Large data lists (20k+ legs)

Use **`DataGrid`** from `Avalonia.Controls.DataGrid` with **UI virtualization** (default).
- Backing store: `ICollectionView`-like wrapper around an `ImmutableArray<ShotRow>`.
- Provide a **filter/sort/group** model in the VM (UI-agnostic).
- For grouping at 20k rows: prefer a **flat virtualized list with group headers** over nested expanders (perf).
- Inline editing reuses `DataGrid` cell editors bound through `IModelEditService`.
- Fallback: `ListBox` + `VirtualizingStackPanel` + custom item template if `DataGrid` perf is insufficient.

### 7.5 Navigation

Selecting an object → activates the file tab → scrolls editor to `SourceSpan`. Bidirectional: clicking in the editor highlights the object in the browser. Go-to-definition / find-references go through `ISymbolNavigationService`.

### 7.6 Localization (i18n) — English + Romanian

- All user-facing strings (UI labels, menu items, diagnostic **messages**) go through `IStringLocalizer` (`Microsoft.Extensions.Localization`) backed by `.resx` files: `Strings.resx` (en, default) + `Strings.ro.resx`.
- **Diagnostic codes** (`TH0007`) stay culture-invariant; only the message text is localized. Tests assert against codes, not messages.
- Two resource scopes:
  - `Therion.Core.Resources` — diagnostic messages (reused by CLI and any future API).
  - `TherionProc.Resources` — UI labels.
- Runtime language switch via a `LanguageService` that sets `CultureInfo.CurrentUICulture` and raises a change event; bindings update through MVVM.
- Number / date formatting follows the UI culture; **coordinate / measurement display** always uses invariant numeric formatting (decimal point) regardless of UI culture, to match Therion files.

---

## 8. Performance & caching summary

- **Parsing**: parallel per file; result cached by `(path, length, lastWriteUtc)`.
- **Indexes**: hash-based (`Dictionary`, `FrozenDictionary` on .NET 8). Stations keyed by canonical qualified name.
- **Snapshots**: immutable; UI binds to the latest snapshot; updates are atomic swaps → no UI tearing.
- **Memory**: AST uses `record` + interned strings (`StringInterner` for keywords and identifiers).
- **Future**: when moving to .NET 10, evaluate `System.Threading.Channels` pipelines and source-generated parsers.

### 8.1 Units (canonicalize to metres, preserve original)

- AST holds the **original literal + unit** as parsed (e.g., `Length(15.2, Unit.Feet)`).
- A `IUnitConverter` (in `Therion.Core`) provides canonical SI values; semantics layer adds a derived `CanonicalMetres` property to numeric AST nodes.
- Default canonical unit: **metres** for lengths, **degrees** for angles (configurable via `WorkspaceOptions.CanonicalUnits`).
- This dual representation preserves round-trip fidelity (emit original) while making computation easy (use canonical).

### 8.2 Logging — standard .NET ecosystem

- `Microsoft.Extensions.Logging` is the abstraction used everywhere in the libraries (no Serilog dependency in `Therion.*`).
- The host (UI / CLI / future API) wires standard sinks:
  - **Console** (`AddSimpleConsole` / `AddJsonConsole`),
  - **Debug** (`AddDebug`),
  - **EventSource / EventLog** where applicable,
  - **File** via `Microsoft.Extensions.Logging` + a stock rolling-file sink (e.g., `Microsoft.Extensions.Logging.AzureAppServices` style or the BCL `TextWriter` provider; if a third-party file provider is needed we stay within the standard ILogger ecosystem).
- Log categories follow project namespaces (`Therion.Syntax.Lexer`, `Therion.Semantics.Binder`, …).

---

## 9. Round-trip & code generation (future-ready)

To enable **emitting Therion files from a model** later:
- Preserve **trivia** (comments, whitespace, line endings, BOM, encoding) on AST nodes.
- Add an `ITherionWriter` interface from day one (stub now), implemented as a visitor over the AST.
- Keep AST nodes immutable but provide `with`-friendly records to compose changes.

---

## 9bis. Therion compilation & external viewers (`Therion.Build`)

Separate library `Therion.Build` (UI-agnostic, no Avalonia) wraps the **installed Therion distribution** and related viewers. Exposed via `ITherionCompiler`, consumed by both UI and CLI.

### 9bis.1 Toolchain discovery

`IExternalToolLocator` finds executables in this order:

1. **User-specified path** in settings (`ExternalTools.TherionPath`, `ExternalTools.LochPath`, `ExternalTools.AvenPath`).
2. **Environment variables** (`THERION_HOME`, `PATH`).
3. **Well-known install locations** per OS:
   - Windows: `%ProgramFiles%\Therion\`, registry key `HKLM\SOFTWARE\Therion`.
   - Linux: `/usr/bin/therion`, `/usr/local/bin/therion` (apt / brew).
   - macOS: `/Applications/Therion.app/Contents/MacOS/`, Homebrew prefixes.
4. **Fallback**: prompt the user / return `ToolNotFound` diagnostic.

Detected tools are exposed as `ToolInfo(Path, Version, Source)` and reported in the UI's *Settings → External Tools* page. Version is sniffed via `therion --version` / `loch --version` / `aven --version`.

### 9bis.2 Compilation pipeline

```
ITherionCompiler.CompileAsync(projectEntryPoint, options, cancellationToken)
  -> CompileResult {
       ExitCode,
       Stdout / Stderr (streamed via IProgress<CompilerOutputLine>),
       Diagnostics      // structured, see below
       OutputArtifacts  // discovered generated files
     }
```

Mechanics:
- Spawns `therion.exe` with the entry-point and any flags (`-l <logfile>`, `--print-xtherion-bg`, etc.).
- Working directory = directory of the entry-point.
- **Streamed output**: stdout/stderr lines are surfaced live to the UI's *Compiler Output* tool window. Lines are tagged with severity (heuristic: `error:` / `warning:` prefixes + parsed line refs).
- **Diagnostic mapping**: each Therion output line that contains `file:line` (e.g., `cave.th:42:`) is converted into a structured `Diagnostic` linked to the same `SourceSpan` infrastructure used by our parser, so the user can click → jump to the offending line in the AvaloniaEdit editor. A pluggable `ITherionOutputParser` does this; it is **versioned** because Therion's output format changes between releases.
- **Cancellation**: kills the process tree on `CancellationToken`.
- **Caching**: compile results are not cached (Therion's own outputs are filesystem-persistent), but the **output artifact list** is cached per `(entryPointHash, therionVersion)` so the UI can show last-known outputs immediately on reopen.

### 9bis.3 Output artifact discovery

After (and during) a successful compile, `IOutputArtifactCollector` scans:

- The working directory and any path mentioned in `export` commands of the active `.thconfig`,
- Common Therion output extensions: `.lox` (Loch 3D model), `.3d` (Survex / Aven, **typically emitted by Therion** via `export model -fmt survex`), `.pdf` (maps), `.svg`, `.xvi`, `.shp`, `.kml`, `.dxf`, `.html`, `.png`, `.tlx`, `.dbf`.

Each artifact is exposed as `OutputArtifact(Path, Kind, SizeBytes, LastWriteUtc, SourceExportCommand?)` and shown in a **Generated Files** tool window with:
- Grouping by `Kind`,
- Open with default shell handler (works for `.lox` and `.3d` on Windows as you noted),
- Open containing folder,
- Quick actions for known kinds (see below).

**Live watch**: a `FileSystemWatcher` is attached to each export directory while a compile is running and for a short grace period afterwards, so newly generated files appear in the list as they are written. Watchers are debounced (250 ms) and disposed when no longer needed.

**Compile concurrency**: only **one compile at a time per workspace**. Additional Build requests while a compile is in progress are rejected with a clear message (Cancel + retry). The Build button is disabled while a compile is running.

### 9bis.4 External viewers — Loch & Aven

Because the typical pipeline has Therion **emit both `.lox` and `.3d`**, both *Open in Loch* and *Open in Aven* are surfaced as **top-level toolbar buttons** after a successful compile (enabled only when the respective artifact exists), in addition to per-artifact quick actions:

| Artifact kind | Default action | Quick action |
|---|---|---|
| `.lox` | Shell open (Windows default = Loch) | **"Open in Loch"** → launches `ExternalTools.LochPath` if set, else shell-open. |
| `.3d` | Shell open (Windows default = Aven if Survex installed) | **"Open in Aven"** → launches `ExternalTools.AvenPath` if set, else shell-open. |
| `.pdf`, `.svg`, `.png`, … | Shell open | — |
| Any | Reveal in file manager | — |

Loch / Aven launches are **fire-and-forget** (`Process.Start` with `UseShellExecute = true` for shell-open fallback). Stdout is **not** captured for viewers.

If no `.lox` / `.3d` was generated by the last compile, the respective toolbar button / quick action is disabled with a tooltip explaining why (e.g., "no `export model -fmt lox` directive in `.thconfig`").

### 9bis.5 UI integration

- **Build menu**: *Build*, *Rebuild*, *Cancel*, *Open Last Output Folder*.
- **Compiler Output** tool window: live streaming, severity colors, click-to-navigate on `file:line` matches.
- **Generated Files** tool window: list/grid grouped by kind, with action buttons; updated live via output-directory watcher.
- **Settings → External Tools**: detected paths + override fields + "Test" buttons.
- All strings localized (en / ro).

### 9bis.5a Keyboard shortcuts (configurable)

All commands have **configurable shortcuts** via *Settings → Keyboard*. Defaults:

| Command | Default shortcut |
|---|---|
| Build | `F5` |
| Rebuild | `Ctrl+F5` |
| Cancel build | `Ctrl+Break` (also `Shift+F5` as alt) |
| Open in Loch | `F9` |
| Open in Aven | `F10` |
| Go to Definition | `F12` |
| Find All References | `Shift+F12` |
| Toggle Workspace Explorer | `Ctrl+Alt+L` |
| Toggle Diagnostics | `Ctrl+\, E` |

Implementation: `IKeyboardShortcutService` holds a `Dictionary<CommandId, KeyGesture>` loaded from settings (sidecar overrides profile, per §6.2). Avalonia `KeyBinding`s on the main window are rebuilt whenever the map changes. Conflicts are detected at edit time in the settings UI.

### 9bis.6 Cross-platform notes

- Process spawning uses `System.Diagnostics.Process` with `UseShellExecute = false` for `therion` (to capture output) and `true` for viewer launches when a specific path is **not** set.
- On Linux/macOS, shell-open is delegated to `xdg-open` / `open` via a small `IShellOpener` abstraction.
- All path comparisons use the OS-correct comparer.

### 9bis.7 Roadmap placement

- **M5b** (new sub-milestone, can run in parallel with M5): toolchain discovery, `ITherionCompiler` with streaming output, output artifact collector, **Compiler Output** + **Generated Files** tool windows, viewer quick actions, settings page.
- Compilation does **not** depend on our parser being feature-complete — it can ship as soon as M1's shell is in place. Diagnostic-to-span mapping improves as our parser matures.

---

## 10. Diagnostics — UX priorities

Per your priority on **excellent syntax error reports**:

- Every diagnostic has: `Code`, `Severity`, `Message`, `SourceSpan`, optional `ExpectedTokens`, `RelatedSpans`, `Hint`, `HelpUri` pointing to `thbook` section or our docs.
- Renderer in CLI prints **rustc-style** carets:
  ```
  error TH0021: expected 'endsurvey', found 'survey'
    --> cave.th:42:1
     |
  42 | survey upper
     | ^^^^^^ unterminated 'survey' starts here (line 17)
  ```
- UI shows the same info in tooltips/diagnostics panel with click-to-navigate to `RelatedSpans`.
---
## 11. Testing strategy

- **Unit tests** per parser rule (golden inputs + expected AST snapshots, e.g., via Verify.Xunit).
- **Corpus tests** using public Therion sample repos:
  - `therion-mirror` examples,
  - `loch` samples (where compatible),
  - any `.th/.th2/.thconfig` files in `therion/samples` upstream.
  Tests assert **zero unexpected errors** and snapshot AST counts.
- **Round-trip tests** (once writer exists): parse → emit → parse → assert AST equality (modulo non-significant trivia). **Includes XVI**: `XviFile` round-trips must reproduce the affine transform numerically to double precision.
- **Performance tests**: load a synthetic 20k-leg survey under a budget (e.g., < 1.5 s parse, < 200 ms incremental).

---

## 12. Tooling & dependencies

| Concern | Choice | Notes |
|---|---|---|
| Parser | **Superpower** | per requirement |
| MVVM | **CommunityToolkit.Mvvm** | source-generated, low ceremony |
| DI | `Microsoft.Extensions.DependencyInjection` | stock |
| Logging | `Microsoft.Extensions.Logging` (Console / Debug / File providers from the .NET ecosystem) | standard sinks |
| Localization | `Microsoft.Extensions.Localization` + `.resx` (en, ro) | i18n |
| Docking shell | **Dock.Avalonia** | VS-style layout, cross-platform |
| Editor control | **AvaloniaEdit** | syntax highlighting + squiggles + click handlers |
| Tests | **xUnit + FluentAssertions + Verify** | snapshots |
| Cache serialization | **MessagePack** | compact binary |
| File watching | `System.IO.FileSystemWatcher` | with debounce |
| External process | `System.Diagnostics.Process` | for Therion / Loch / Aven launching |

---

## 13. Roadmap / milestones

### M1 — Foundations (1–2 weeks)
- Create solution layout (projects above).
- `Therion.Core` primitives (`SourceSpan`, `Diagnostic`, `Unit`, `IUnitConverter`, `Result<T>`).
- Lexer + minimal `.thconfig` parser.
- `ParserOptions` (lenient/strict) plumbed.
- DI wiring in `TherionProc`; **Dock.Avalonia** shell with Workspace Explorer + empty document host.
- i18n scaffolding: `Strings.resx` (en) + `Strings.ro.resx`, language switch in menu.
- `ILogger` wired with Console + Debug + File providers.

### M2 — `.th` parser core
- Survey / centreline / data / station / fix / equate / import.
- Diagnostics framework + rustc-style CLI formatter (CLI scaffolded here).
- Lenient and strict modes pass the same corpus tests (different expected diagnostics).
- AvaloniaEdit document editor with syntax highlighting driven by lexer.
- 200+ unit tests on representative snippets.

> **M2 status (snapshot):** ✅ **complete.** Parser core, rustc-style formatter, CLI (`validate`/`dump-ast`/`list-stations`), encoding directive (`encoding <name>` two-phase read), host-agnostic `TokenClassifier`, bundled synthetic corpus + corpus runner, perf smoke (5k legs / <1 s), and **AvaloniaEdit editor with live syntax highlighting** wired into the main window. **229 tests passing.**
> AvaloniaEdit 11.1.0 works against Avalonia 12.0.3 in practice (built and runs). The `TherionColorizer` consumes the same `TokenClassifier` output, keeping the lexer as the single source of truth. Diagnostic squiggles and click-to-navigate land with M3 alongside the semantic indexes.

### M3 — Semantic indexes + symbol navigation
- Station/survey indexes, reference resolution, equate graph.
- `ISymbolNavigationService` (Go to Definition / Find References).
- UI: Object Browser with stations + shots, virtualized DataGrid (target 20k rows), in-place edit (read-only first, then editable via M6 writer).

> **M3 status (snapshot):** ✅ **complete.** `Therion.Semantics` ships `QualifiedName`, `StationSymbol` / `SurveySymbol` / `ShotSymbol`, `EquateGraph` (union-find), `SemanticBinder` (walks `TherionFile` → `BlockCommand.Children`, qualifies stations with survey path, resolves equate refs via local-scope-then-ancestor lookup with Levenshtein hint), `SemanticModel` (`FrozenDictionary` indexes implementing `ISymbolIndex`), and `SymbolNavigationService` implementing the new `ISymbolNavigationService` contract. New diagnostic codes `TH_SEM_001/002/003`. Parser extended to recognize centreline body rows as `DataRow` nodes. UI: `ObjectBrowserViewModel` exposes virtualized `Stations` + `Shots` lists; `MainWindow` hosts a virtualized `Avalonia.Controls.DataGrid` (Fluent theme) with Stations / Shots tabs. **F12 + Ctrl+Click in `TherionTextEditor` invoke `ISymbolNavigationService.GoToDefinition`** to jump to the station/survey declaration. 20 000-leg parse + bind perf spike passes under budget. **238 tests passing** (229 syntax + 8 semantics incl. perf + 1 workspace). Full editing + workspace-driven document load deferred to M5 (caching/watcher) and M6 (emitter), per Decision #13.

### M4 — `.th2` + `.xvi` parsers
- Scrap/point/line/area + their many subtypes (`.th2`).
- **XVI parser** (`XviTokenizer` + `XviParser`): `XviFile` AST, `AffineTransform2D`, `CalibrationPoint`.
- `XviIndex` built in semantics; image-path resolution; `TH_XVI_001` / `TH_XVI_002` / `TH_XVI_003` diagnostics.
- Workspace Explorer: `.xvi` nodes as children of the referencing `.th2` file; image file as a leaf (⚠ badge if unresolved).
- **XVI References** view in Object Browser (read-only at this stage).
- `SketchReference` navigation: click `.xvi` row → jump to the `sketch` keyword in the `.th2` editor.
- XVI references added as edges in `FileGraph`.
- Corpus tests: assert XVI files in bundled sample repos parse cleanly.
- UI: scrap/object list; (no 2D rendering yet).

> **M4 status (snapshot):** ✅ **parsers + semantic index + workspace integration complete.** `Therion.Syntax` ships `XviTokenizer` + `XviParser` producing `XviFile` (`AffineTransform2D`, `CalibrationPoint`), `Th2Parser` with `ScrapBlock` / `PointObject` / `LineObject` / `AreaObject` / `SketchReference`, lenient/strict diagnostics (`TH_XVI_010-014`, `TH2_001/002/003/010`). `Therion.Semantics` ships `XviIndex` (`XviSymbol`, `FileGraphEdges`, back-refs from `.xvi` → referencing scraps) with image-path resolution, missing-file and degenerate-transform checks (`TH_XVI_001/002/003`). XVI integration: `SemanticModel.Xvi` init-only property, new `WorkspaceSemanticModel` aggregator (per-file binders + workspace-wide `XviIndex` + merged FileGraph edges — `source`/`input`/`load` + scrap→xvi), and `TherionWorkspace.BuildSemanticModel()` auto-discovers `.xvi` siblings of loaded `.th2` files. Workspace Explorer UI and **XVI References** Object Browser tab remain pending and will land alongside Dock.Avalonia shell work.

### M5 — Caching & file watcher
- In-memory + disk cache (MessagePack), default on.
- `--no-cache` / env var / option to skip for debugging.
- Incremental rebuild on file change (per-file reparse; untouched files served from cache).

> **M5 status (snapshot):** ✅ **in-memory + JSON disk tier + watcher + workspace loader + workspace semantic model complete.** `Therion.Workspace` ships `IParseCache` + `InMemoryParseCache`, **`IDiskParseCache` + `JsonDiskParseCache`** (XDG/LocalAppData/Caches-aware, schema-versioned, source-text fingerprint, best-effort writes), **`TieredParseCache`** (L1 in-memory + L2 disk with auto-promotion), `DebouncedFileWatcher` (250 ms, matches Decision #24), `TherionWorkspace : IWorkspace` (BFS-walks `source`/`input`/`load`, watches dirs, atomic re-parse on change, `WorkspaceChanged` event). New `TherionWorkspace.BuildSemanticModel()` instincproduces a `WorkspaceSemanticModel` (per-file `SemanticModel` + workspace-wide `XviIndex` + merged FileGraph edges). `WorkspaceOptions.FromEnvironment()` honors `THERIONPROC_NO_CACHE`; disabling drops the L2 tier only. MessagePack on-disk format remains a follow-up swap behind `IDiskParseCache` (no new package dependency).

### M5b — Therion compilation & viewers (parallel; see §9bis)
- `Therion.Build` library: `IExternalToolLocator`, `ITherionCompiler`, `IOutputArtifactCollector`.
- UI: *Build* menu, *Compiler Output* and *Generated Files* tool windows (with live output-dir watcher).
- Top-level toolbar buttons + per-artifact quick actions for Loch (`.lox`) and Aven (`.3d`); shell-open fallback.
- Settings → External Tools page with autodetect + override.
- Settings → Keyboard page; `IKeyboardShortcutService` with defaults (`F5` Build, `Ctrl+Break` Cancel).

> **M5b status (snapshot):** ✅ **toolchain + compiler + artifact collector + shell-open complete (UI deferred).** `Therion.Build` ships `ExternalToolLocator` (well-known paths + PATH lookup, per §9bis.1), `HeuristicTherionOutputParser` (`file:line:` + `error:` / `warning:` / `hint:` recognition, per Decision #23), `OutputArtifactCollector` (recognizes `.lox`/`.3d`/`.pdf`/`.svg`/`.xvi`/`.shp`/`.kml`/`.dxf`/`.html`/`.png`/`.tlx`/`.dbf`, per §9bis.3), `TherionCompiler : ITherionCompiler` (process spawn, stdout/stderr streaming via `IProgress<CompilerOutputLine>`, kill-on-cancel via `Process.Kill(entireProcessTree:true)`, new diagnostic codes `TH_BUILD_001/002/003/OUT`), and `ShellOpener` (Windows / `xdg-open` / `open`). **5 build tests passing**. UI integration (Build menu, Compiler Output / Generated Files tool windows, Loch/Aven toolbar buttons, Settings pages, `IKeyboardShortcutService`) lands with Dock.Avalonia work and is tracked alongside M6.

### M6 — Polish, edit & extensibility hooks
- Plugin loading for command handlers and semantic rules.
- `ITherionWriter` emitter v1 → enables Object Browser inline edits to persist to files (kept disabled in UI until then, per decision).
- Go-to-definition / find-references expanded to all symbol kinds.
- Layout persistence for Dock.Avalonia.

> **M6 status (snapshot, v0.6):** ⏳ **emitter + extensibility + edit + compile gate + semantic rule runner + artifact cache + UI open-file pipeline + Build/Diagnostics/Workspace UI surfaces landed; only the Dock.Avalonia migration and diagnostic squiggles remain.** Cumulative deliverables:
>
> - **Syntax/Semantics emitter & plugin contracts:** `ITherionWriter` + `TherionWriter` round-tripping `.th` (survey / centreline / data / row / fix / equate / input / team / date) and `.th2` (scrap / point / line+vertices / area), `UnknownCommand` re-emit. Plugin contracts: `ICommandHandler`, `IDialect`, `ICommandRegistry` + thread-safe `CommandRegistry` (§4.4), `ISemanticRule` + `SemanticContext` + `ISemanticRuleRunner` + `SemanticRuleRunner` (§5.3 — invokes plugins over `WorkspaceSemanticModel.PerFile`, isolates plugin exceptions as `TH_SEM_RULE` diagnostics), `IModelEditService` + `ModelEditService` (codes `TH_EDIT_001/002`).
> - **DI extension methods (resolves D3/D4):** `AddTherionCommands()` (`Therion.Syntax`) discovers all registered `ICommandHandler`s and builds an `ICommandRegistry`; `AddTherionSemantics()` (`Therion.Semantics`) registers `ISemanticRuleRunner` over all registered `ISemanticRule`s plus `IModelEditService`. Both projects now depend on `Microsoft.Extensions.DependencyInjection.Abstractions`.
> - **Build pipeline:** `ICompileGate` + lock-free `CompileGate` (Decision #27); `IOutputArtifactCache` + `JsonOutputArtifactCache` (§9bis.2 — last-known artifact list keyed by SHA-256(entryPoint + therionVersion), schema-versioned, OS-correct cache root).
> - **UI surfaces (this iteration):**
>   - `MainWindow` now hosts **Build menu** (Build / Rebuild / Cancel / Open in Loch / Open in Aven / Open last output folder), a top **toolbar** with quick-action buttons (Loch/Aven enabled state driven by `Build.HasLoxArtifact` / `HasAvenArtifact`), and **`Window.KeyBindings`** for `F5` / `Ctrl+F5` / `Shift+F5` / `F9` / `F10` (Decision #29 defaults; `IKeyboardShortcutService` settings page deferred).
>   - **Tabs added** to the bottom tool pane: **XVI References**, **Diagnostics**, **Compiler Output**, **Generated Files**. `XviReferencesViewModel` projects `WorkspaceSemanticModel.Xvi.ByPath` into rows (path + image + existence + scale + CP count + scrap refs). `DiagnosticsViewModel` aggregates parser + semantic + compile diagnostics with click-to-navigate hook. `BuildViewModel` streams compiler output via `IProgress<CompilerOutputLine>`, manages a single in-flight build through `CompileGate`, writes/reads the artifact cache around `DocumentChanged`, and dispatches Loch/Aven launches through `IExternalToolLocator` + `IShellOpener` fallback.
>   - **Workspace Explorer** replaces the M1 placeholder: `WorkspaceExplorerViewModel` projects `WorkspaceSemanticModel.PerFile` + `Xvi.ByPath` into a flat node list; entry-point first, then loaded files, then `.xvi` siblings with image-existence flags.
>   - **Strict parser-mode toggle** (D15) in *View* menu: backed by new `ParserOptionsHost.Current` ambient (read by parsers that don't take `ParserOptions` explicitly).
> - **DI composition root:** `AppServices` now wires the full M5/M5b/M6 graph (`IParseCache → TieredParseCache` honoring `WorkspaceOptions.DisableDiskCache`, `IDiskParseCache → JsonDiskParseCache`, `ITherionCompiler`, `IOutputArtifactCollector`, `IOutputArtifactCache`, `ICompileGate`, `IShellOpener`, `ITherionOutputParser`, `ISemanticRuleRunner`, `IModelEditService`, `IDocumentService`, command/rule plugin registries) plus all new ViewModels.
>
> **Cumulative: 301 tests passing** (244 syntax + 27 semantics + 20 workspace + 10 build).
>
> **What remains for M6:**
> 1. ~~**Dock.Avalonia shell** (D13)~~ — **Partially resolved.** Interim dockable shell shipped: `MainWindow` now uses `GridSplitter`-based resizable left/bottom panes; new `ILayoutService` + `JsonLayoutService` persists pane sizes, pane visibility, and window bounds to `%AppData%/TherionProc/layout.json` (XDG fallback); new `MainWindowViewModel.ToggleWorkspaceExplorerCommand` / `ToggleDiagnosticsCommand` plug into `ShellCommandIds.ToggleWorkspaceExplorer` / `ToggleDiagnostics` shortcuts so the *Settings → Keyboard* page can bind them. The actual **Dock.Avalonia package swap** is now a post-M6 follow-up (item B'); the `ILayoutService` abstraction was designed so an `IFactory.LoadLayout` / `SaveLayout` adapter can replace `JsonLayoutService` without touching consumers.
> 2. ~~**Diagnostic squiggles in the editor** (D5/D17)~~ — **Resolved.** `DiagnosticSquiggleRenderer : IBackgroundRenderer` paints severity-colored wavy underlines over the `KnownLayer.Selection` layer; bound via new `Diagnostics` + `CurrentFilePath` styled properties on `TherionTextEditor`; `MainWindowViewModel.CurrentDiagnostics` pushes the merged parser + semantic + compile set.
> 3. ~~**`IKeyboardShortcutService` + Settings → Keyboard page** (Decision #29)~~ — **Resolved.** New `IKeyboardShortcutService` contract + `ShellCommandIds` catalog in `Therion.Processing.Abstractions`; `JsonKeyboardShortcutService` persists a delta map to `%AppData%/TherionProc/keyboard.json`; *Settings → Keyboard* sub-tab exposes one inline-editable row per command with per-row Reset + Reset-all. `MainWindow` rebuilds `KeyBindings` from the service on load and on every `GesturesChanged`.
> 4. ~~**Settings → External Tools page** (§9bis.5)~~ — **Resolved.** New `IExternalToolPathOverrides` contract + `JsonExternalToolPathOverrides` (persists to `%AppData%/TherionProc/external-tools.json`). `ExternalToolLocator` gained an overrides-aware ctor (override → well-known → PATH) and now sniffs version via `<tool> --version` with a 5 s timeout. *Settings → External Tools* sub-tab exposes editable `Override` paths + per-row **Test** button + live `Result` column.
> 5. ~~**Click-to-navigate** from Diagnostics and Compiler Output rows back into the editor~~ — **Resolved.** New `TherionTextEditor.ScrollTo(SourceSpan)`; `MainWindow` wires `DiagnosticsGrid.DoubleTapped` → `Diagnostics.NavigateCommand` → `MainWindowViewModel.NavigateToSpanRequested` → `editor.ScrollTo`. **Compiler Output rows now propagate `CompilerOutputLine.Span` through `CompilerOutputRow` and `Build.NavigateRequested`** so double-click jumps to the offending line via the same path.
> 6. **Built-in `ICommandHandler` / `ISemanticRule` registrations** — DI extensions exist but ship with empty registries.
> 7. ~~**Expanded go-to-def** beyond stations + surveys~~ — **Resolved.** `SemanticModel` now carries a `Scraps` index (populated by `SemanticBinder` from every `ScrapBlock`); `SemanticModel.TryResolve` falls back to scrap-id lookup. `WorkspaceSymbolNavigationService.GoToDefinition` additionally matches file paths (full path or basename, case-insensitive) against every loaded `PerFile` entry so `input foo.th` / `source foo.thconfig` references resolve to the file's top.
> 8. ~~**Editing through `IModelEditService`**~~ — **Resolved.** `ShotSymbol` now carries `SourceRow` + `FieldDefinition` references (populated by the binder); `ShotRow` is an `ObservableObject` with TwoWay `Length` / `Compass` / `Clino` bindings; cell edits raise `ObjectBrowserViewModel.ShotEditRequested`; `MainWindowViewModel.ApplyShotEditAsync` reconstructs a new `DataRow`, calls `IModelEditService.ReplaceNode`, and writes the result through `DocumentService.WriteCurrentTextAsync` which persists + re-parses. *Shots* grid in `MainWindow.axaml` now editable (From/To/Line stay read-only).

### M7 — .NET 10 readiness
- Add `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` to libs.
- Evaluate new APIs (e.g., better `SearchValues`, JSON improvements).

---

## 14. Decisions log

### 14.1 Decisions log (rounds 1–3, locked in v0.4)

| # | Topic | Decision |
|---|---|---|
| 1 | Editing | **Full editor**: AvaloniaEdit with syntax highlighting + click-on-term actions + in-list editing. |
| 2 | Parser default mode | **Lenient** by default; **strict** opt-in via `ParserOptions.Mode`. |
| 3 | Reparse granularity | **Whole-file reparse**; cache untouched files. |
| 4 | Therion source of truth | <https://github.com/therion/therion> pinned by tag; SHA recorded in `TherionVersion.json`. **No local vendored copy.** |
| 5 | `thbook` source | TeX in `therion/thbook/` + released PDF (e.g. `thbook-v6.4.0.pdf`). |
| 6 | Sample corpora bundled | Therion `samples/` + `jarvist/migovecsurveydata` + `tr1813/migresurvey` + `iccaving/migovec-survey-data` + `apgeo/grind`. Each under `tests/Corpus/<source>/` with `LICENSE.txt`. |
| 7 | Shell | **Dock.Avalonia** (cross-platform). Third-party widgets allowed. |
| 8 | Disk cache | **On by default**; skip via `--no-cache` / env var / option. |
| 9 | Units | Canonical **metres**; original preserved. |
| 10 | i18n | **English + Romanian**, **including caving-domain terms** (both domain + UI localized). |
| 11 | Logging | Standard .NET sinks via `Microsoft.Extensions.Logging`. |
| 12 | Entry-point discovery | **Open file or open folder**, configurable. Files with **no extension first**, then `.thconfig`, then **syntax autodetect** for any file. Multiple candidates → user prompt. See §6.1. |
| 13 | Editing/go-to-def staging | **Whichever is cheapest** — final functionality only needs to work at the final stage. Likely: M3 ships read-only Object Browser + stations/file-refs go-to-def; full editing and full symbol-kind navigation land with the emitter at M6. May be **disabled in UI** in between. |
| 14 | Settings storage | **(c)** Both — sidecar `.thp.json` overrides user profile. **Default write target = sidecar.** |
| 15 | CLI | **In M2** (`Therion.Cli`): `validate`, `dump-ast`, `list-stations`. |
| 16 | Diagnostic catalog | `docs/diagnostics.md`, versioned, linked from `HelpUri`. |
| 17 | Public API stability | **Not stable** yet; no semver / analyzer baseline. |
| 18 | UI threading | Parsing & semantics on background threads; **atomic snapshot swaps** posted to Avalonia dispatcher. No partial states. |
| 19 | Compilation & viewers | New `Therion.Build` lib + UI: autodetect `therion`, stream output, list outputs, launch **Loch** for `.lox` and **Aven** for `.3d` (paths configurable, shell-open fallback). See §9bis. |
| 20 | Folder-scan recursion | **Non-recursive by default**; configurable max depth (default `3` when enabled). |
| 21 | Sniffer guardrails | Skip files **> 64 KB** (`SnifferMaxFileSizeBytes` constant, configurable in code), skip binary-looking files (first 4 KB probe), first 256 tokens only. |
| 22 | CI / Therion install | **Assume / require Therion installed locally** for compile-related tests. CI runs parser/semantic tests only; compilation tests are opt-in/local. |
| 23 | Compiler output parser | **Best-effort heuristic** per version; unmatched lines preserved verbatim. No round-trip guarantee. |
| 24 | Output artifact watch | **Live watch** of export directories during compile + grace period, debounced 250 ms. |
| 25 | Loch/Aven detection (non-Win) | user-set path → `loch`/`aven` on `PATH` → `xdg-open`/`open` fallback. |
| 26 | `.3d` origin | Therion **emits** `.3d` (typical pipeline) → *Open in Aven* gets a **top-level toolbar button**. |
| 27 | Compile concurrency | **One compile at a time per workspace.** Additional Build requests rejected with prompt. |
| 28 | Romanian translation | **Seed `Strings.ro.resx` with a draft translation** (to be reviewed). |
| 29 | Shortcuts | **Configurable** via Settings → Keyboard. Defaults: `F5` Build, `Ctrl+F5` Rebuild, `Ctrl+Break` / `Shift+F5` Cancel, `F12` Go to Def, `Shift+F12` Find Refs. |
| 30 | XVI format | `.xvi` (geo-referenced sketch metadata) added throughout the chain: dedicated `XviTokenizer` / `XviParser`, `XviFile` AST + `SketchReference` on scraps, `XviIndex` in semantics, image-path resolution diagnostics (`TH_XVI_001/002/003`), **XVI References** view in Object Browser, Workspace Explorer integration, `FileGraph` edges. Covered in §3.1, §4.1, §4.3, §5.1, §5.2, §7.3, §11. Implemented in **M4**. |

---

## 15. Risks

- **Grammar drift**: Therion's grammar isn't a single formal spec → mitigation: version registry + extensive corpus tests + traceability metadata on every command definition.
- **Encoding handling**: legacy files with non-UTF8 → two-phase read with `encoding` directive.
- **Performance on huge `.th2`**: scraps with thousands of vertices → keep AST allocations low (arrays of structs where it matters).
- **Avalonia DataGrid grouping perf**: validate early with a 20k synthetic dataset (M3 spike).
- **Therion compiler output format drift** (§9bis): stderr/stdout layout changes between releases → mitigation: versioned `ITherionOutputParser`, best-effort fallback that preserves unmatched lines verbatim.
- **Entry-point misdetection** (§6.1): a non-config file syntactically resembling config could be wrongly picked → mitigation: conservative sniffer + always prompt on multiple candidates + remember user choice per folder.

---

## 16. Known doc-vs-impl discrepancies (audit, v0.5)

Recorded during the post-M6 audit so the plan stays honest. None of these block UI work; they will be reconciled either by patching the doc or by completing the implementation, as noted.

| # | §  | Discrepancy | Resolution path |
|---|----|---|---|
| D1 | §4.1, §4.2, §12 | Plan specifies **Superpower** for both tokenizer and parser. Implementation uses a **hand-rolled `TherionTokenizer` + recursive-descent parsers** (still Superpower-compatible at the token-shape level via `TokenClassifier`). | **Update doc** in v0.6 — record "hand-rolled lexer/parser, Superpower kept as a possible future swap." The hand-rolled stack already passes 241 syntax tests including a 5 k-leg perf smoke and is simpler to maintain. |
| D2 | §4.5, §12 | Disk cache format specified as **MessagePack**. Implementation uses **JSON (`JsonDiskParseCache`)** behind `IDiskParseCache`. | Intentional — the abstraction was added so MessagePack can slot in later without API churn. Move to MessagePack when (a) cache size matters in practice and (b) a maintained MessagePack package is selected. |
| D3 | §5.3 | "Rules registered via DI." | `ISemanticRuleRunner` now exists and runs rules over the workspace model, but **no DI extensions register built-in rules yet**. Will be added with `AddTherionSemantics()` alongside `AddTherionCommands()` in the composition root work. |
| D4 | §4.4 | "Handlers registered via DI (`AddTherionCommand<TSurveyHandler>()`)." | `ICommandRegistry` + `CommandRegistry` exist; **DI extension methods and built-in handler registrations are still pending**. Lands with composition-root work for the Dock.Avalonia shell. |
| D5 | §7.3 | Editor must show **diagnostic squiggles** and a **diagnostics tool window**. | Editor + colorizer ship; squiggle adornments and the diagnostics panel are queued with Dock.Avalonia (M6 UI tail). |
| D6 | §7.3 / §5.1 | `FileGraph` described as a directed graph. Implementation stores it as a flat `ImmutableArray<(From,To)>`. | Acceptable until UI needs reverse-edge lookups; promote to an adjacency-list structure when the Workspace Explorer consumes it. |
| D7 | §1 | `IFileSource` is a Core primitive. | Defined but unused — `Therion.Workspace` legitimately owns IO. Either route reads through it (clean) or drop it from the plan in v0.6. |
| D8 | §4.5 | Key includes optional `contentHashOptional`. | Implementation uses `(path, length, lastWriteUtc, syntaxVersion)`. Sufficient in practice; the hash slot stays optional in the contract for future use. |
| D9 | §7.3 | `IModelEditService` should "validate the new value against the same parser/semantic rules." | Current impl re-emits and re-parses (syntactic round-trip). Full semantic re-check happens at the next workspace rebuild, not inline. Tight inline validation can be added when the edit grid actually lights up. |
| D10 | §7.3 | UI had no way to open files — File menu only contained *Exit*; `TherionWorkspace` / `IProjectEntryPointResolver` were never reached from any VM. | **Resolved.** New `IStoragePicker` + `IDocumentService` host the open-file/open-folder pipeline; `MainWindowViewModel` exposes `OpenFile` / `OpenFolder` / `Exit` commands; `MainWindow` attaches an Avalonia `IStorageProvider`-backed picker on `Opened`. |
| D11 | §7.1 | `AppServices` only registered a handful of services — `TherionWorkspace`, parse caches, compiler, edit service, rule runner, artifact cache, shell opener were never wired. | **Resolved.** Composition root now registers `IDocumentService`, `IParseCache` (tiered, honoring `WorkspaceOptions.DisableDiskCache`), `IDiskParseCache`, `IModelEditService`, `ISemanticRuleRunner` (empty rule set by default), `ITherionCompiler`, `IOutputArtifactCollector`, `ITherionOutputParser`, `IOutputArtifactCache`, `ICompileGate`, `IShellOpener`. |
| D12 | §3 header | Plan header previously read *"UI: **Avalonia 11.x**"*; codebase runs on Avalonia 12.0.3. | **Resolved.** Header now says Avalonia 12.x. |
| D13 | §13 M1 | Plan calls for Dock.Avalonia shell at M1; current `MainWindow` still uses a `Grid` placeholder marked `<!-- TODO M2b -->`. | Deferred — tracked with the Dock.Avalonia integration work. Open-file UX is now usable on the placeholder shell. |
| D14 | §7.6 | `Strings.resx` had `Romana` (no diacritic) for the Romanian menu label. | **Resolved.** Now `Română`. |
| D15 | §4.2 | `ParserOptions.Mode` (lenient/strict) is plumbed in code but not surfaced as a UI toggle. | **Resolved** via *View → Strict parser mode* checkbox backed by new `ParserOptionsHost.Current` ambient (parsers without an explicit `ParserOptions` argument read from it). |
| D16 | §9bis.4 | *Open in Loch* / *Open in Aven* toolbar buttons described but not yet on the main window. | **Resolved.** Top-of-window toolbar exposes both quick-action buttons; enabled state is driven by `BuildViewModel.HasLoxArtifact` / `HasAvenArtifact`, and the Build menu mirrors the same commands with `F9` / `F10` key bindings. |
| D17 | §7.3 | Diagnostic squiggles + diagnostics panel still unimplemented (already noted as D5). | **Resolved.** Diagnostics panel ships (DataGrid tab driven by `DiagnosticsViewModel`) **and** squiggle adornments via `DiagnosticSquiggleRenderer : IBackgroundRenderer` over the editor; `MainWindowViewModel.CurrentDiagnostics` is the merged source. |
| D18 | §4.4 / §5.3 | DI extension methods `AddTherionCommands()` / `AddTherionSemantics()` previously missing (paired backlog of D3 + D4). | **Resolved.** Both extension methods now ship in their respective libraries; `Therion.Syntax` and `Therion.Semantics` depend on `Microsoft.Extensions.DependencyInjection.Abstractions`. Built-in handler / rule registrations are tracked separately as M6 follow-up #6. |
| D19 | §9bis.5a | `IKeyboardShortcutService` described as a configurable map; current shell wires hard-coded `KeyBinding`s. | **Resolved.** `IKeyboardShortcutService` ships in `Therion.Processing.Abstractions` with `ShellCommandIds`; `JsonKeyboardShortcutService` persists a delta map to `%AppData%/TherionProc/keyboard.json`. `MainWindow.RebuildKeyBindings()` rebuilds `Window.KeyBindings` from the service on load and on `GesturesChanged`, and *Settings → Keyboard* edits gestures inline. |
| D20 | §7.3 | Diagnostics / Compiler Output rows should be click-to-navigate. | **Resolved.** Both grids now double-click-navigate: `DiagnosticsViewModel.NavigateRequested` and `BuildViewModel.NavigateRequested` (driven by `CompilerOutputLine.Span` extracted by `HeuristicTherionOutputParser`) are forwarded through `MainWindowViewModel.NavigateToSpanRequested` to `TherionTextEditor.ScrollTo(SourceSpan)`. |
| D21 | §5.3 / M6 #6 | DI extensions for semantic rules ship empty. | **Partially resolved.** New `AddTherionBuiltinSemanticRules()` extension registers the first stock rule (`OrphanFixedStationRule`, `TH_SEM_004`). Handler-side built-ins remain blocked by D1. |
| D22 | §4.4 / Post-M6 A | `ICommandHandler` plugins had no parser-side dispatch path. | **Resolved.** `ThParser` now takes an optional `ICommandRegistry`; any keyword not in the built-in switch goes through the registry before the `UnknownCommand` fallback. Handler exceptions surface as `TH0012 PluginHandlerFailed` diagnostics so a misbehaving plugin can't crash a parse. `DocumentService` consumes the registry from DI. Three new tests in `CommandRegistryDispatchTests` cover the dispatch + exception + no-registry paths. Block-level handler dispatch (cursor + block terminator integration) remains a future refinement. |
| D23 | §13 / Post-M6 B' | Dock.Avalonia migration risk. | **Phase 1 resolved.** `Dock.Avalonia 12.0.0.2` + `Dock.Model.Mvvm 12.0.0.2` added to `TherionProc.csproj` — compatibility with Avalonia 12.0.3 proven; 295 tests still pass. Active shell remains the interim `GridSplitter` + `ILayoutService`; the `DockFactory` adapter that replaces `JsonLayoutService` is the remaining phase-2 work. |
| D24 | §13 M7 / Post-M6 C | .NET 10 readiness. | **Resolved (scaffolding).** New `TherionLibraryTargetFrameworks` MSBuild property in `Directory.Build.props` — defaults to `net8.0`; pass `-p:TherionMultiTargetNet10=true` (or set the same-named env var) to multi-target `net8.0;net10.0`. All seven libraries (`Therion.Core` / `Processing.Abstractions` / `Syntax` / `Semantics` / `Workspace` / `Build`) now consume the shared property; apps + tests stay net8.0-only. Smoke-built `Therion.Core` produces both `net8.0` and `net10.0` assemblies with zero warnings. New-API audit deferred until net10.0 is the day-to-day target. |
| D25 | §4.5 / D2 / Post-M6 D | Disk parse cache was JSON-only and on-by-default. | **Resolved.** New `MessagePackDiskParseCache` (LZ4-compressed) implements `IDiskParseCache` with the same envelope shape as `JsonDiskParseCache`; selection happens at the composition root via new `WorkspaceOptions.DiskCacheFormat` (`Json` │ `MessagePack`, default `MessagePack`). **Disk cache now defaults to disabled** (`WorkspaceOptions.DisableDiskCache = true`) to keep first-run startup deterministic; opt-in via env var `THERIONPROC_DISK_CACHE=1` (or by constructing `WorkspaceOptions` with `DisableDiskCache = false`). Disable wins over enable when both are set. `THERIONPROC_DISK_CACHE_FORMAT=json|msgpack` selects the backend. `AppServices` returns `NullDiskParseCache.Instance` when disabled so the L1-only path is allocation-free. Five new tests (4 MessagePack + updated env tests) cover all paths. |

*End of plan.*
89