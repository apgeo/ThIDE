# Therion.Workspace — changes for consumers

What a consumer of this library can notice, newest first. Each entry says what changed, what you see,
and what to do. Entries also cover behaviour that reaches you through `Therion.Syntax` and
`Therion.Semantics`, which this library is built on.

## 2026-11 (round 5)

### Behaviour changes
- **`StationRenumber.Plan` no longer refuses a survey that holds a shot or fix written `x@sub`.**
  Such a reference leaves a placeholder station `parent.x@sub` in the occurrence index; the plan
  took it for a station of the parent, and any rule that renamed it gave an `InvalidName` problem
  (`T2@sub`). Placeholders are now left out of `Renames` and `Unchanged`; the reference itself is
  still renamed with the station it names when that station is in scope. Migration: none.
- **`FixAll.Plan` closes every block left open at one place in one run.** A survey whose last child
  is an open centreline is reported on a line that leads back to the centreline, so its terminator
  was skipped as a duplicate and a second run was needed. Both terminators now go into one insertion
  edit, inner block first (`\n  endcentreline\nendsurvey`), and `Fixed` counts both. The same
  diagnostic listed twice is still fixed once. Migration: none; a host that counts edits per
  diagnostic sees one edit for the two fixes.
- **Declaration lookups compare paths the way the model indexes its files**
  (`Therion.Semantics.WorkspaceSemanticModel.FindStationByDeclaration`, `FindSurveyByDeclaration`,
  and the station lookup of a `.th2` sketch): on Linux `Cave.th` and `cave.th` are two files, and a
  survey declared at the same offset in both was found in whichever came first. They now use
  `PerFile.Comparer`. Migration: a model built with the public constructor gets the comparer of the
  `PerFile` dictionary passed in.

## 2026-11 (later)

### Behaviour changes
- **New thconfig diagnostic `TH0059` for `export … -output` paths written with `\`.** Off Windows,
  Therion writes `-output rez\cave.lox` as a file literally named `rez\cave.lox` in the working
  folder. `ThconfigParser` now reports such a relative path (Warning; Info on Windows, where it
  works), quoting the path as written first and giving the `/` spelling in the hint. Rooted paths
  (`C:\…`, `\\server\…`) are not reported. New: `DiagnosticCodes.BackslashOutputPath`,
  `CodeFixes.BackslashOutputPath`.
- **`CodeFixes` rewrites it**: `ForLine` and `ForDiagnostic` offer the `Fix_PathSeparators` fix for
  `TH0059` as for `TH_SEM_016`, and `BatchCodes` / `CanBatchFix` include it, so "fix all" covers it.
  Migration: none; a host that lists diagnostic codes or translates messages by code may add `TH0059`.

## 2026-11 — correctness and safety fixes

### Breaking (source only)
- **`Therion.Semantics.DataQualityReport` gains a trailing positional `TotalLegs`.** Code that
  *constructs* the record must pass it; code that only reads it is unaffected. The leg metrics
  (ZeroLength, MissingLength, MissingCompass, MissingClino, NoBacksight, NoLrud, SteepLegs) now
  leave splays out. Migration: read `TotalLegs`, not `TotalShots`, as the leg count.

### Behaviour changes
- **Undecodable bytes are detected instead of being written back as U+FFFD.** A lossy read of an
  ASCII or legacy code-page file holds U+FFFD where it used to hold `?`, and `Write`/`EncodeForSave`
  throw `UndecodableBytesException` while such characters remain. New: `UndecodableBytesException`,
  `SourceFile.Lossy`, `TextFileFormat.Lossy` (takes part in record equality),
  `EditProblemKind.Undecodable` (appended). Migration: treat `Undecodable` like `Unrepresentable`
  (refuse the edit, tell the user).
- **Saving follows the `encoding` directive the way reading does**: within the first 4096 bytes of
  the file in its own encoding. A file with a long non-ASCII header is no longer rewritten as UTF-8
  while still declaring a code page. `Create` may throw `UnrepresentableCharacterException`.
- **Writes through a symbolic link change the link's target; a read-only file throws
  `UnauthorizedAccessException`** instead of being overwritten. New: `SourceFileIo.LinkTargetOf`.
  Migration: make a file writable first if you relied on overwriting read-only files.
- **`TopodroidSurveyHeader.UnsafeInputs` also lists surface `bitmap` and scrap `-sketch` paths.**
  A safety gate built on it rejects more headers (intended). New init-only lists `Bitmaps`, `Sketches`.
- **`ShotTableReader.Read`**: "a row one value short is a splay" now applies only when no row in the
  table is full length; otherwise the short row is left out and reported as `TooFewValues`. Rows read
  as splays by that rule get `ShotIssueKind.ReadAsSplay` (appended). Migration: mark splays with `-`
  in the `to` column; add a case for `ReadAsSplay` to exhaustive switches. A comma-separated table
  in which a minority of rows has a different cell count is now read as comma-separated (those rows
  are reported) instead of as one whitespace column with every row dropped.
- **`CodeFixes.ForLine` offers `equate` first for a zero-length leg** (TH_SEM_012); comment-out is
  labelled with the station it disconnects, and is not offered alone when it would disconnect one.
  Hosts that localise `TitleKey` need `Fix_Equate` and `Fix_CommentOutDisconnects`. The check also
  runs when no TH_SEM_012 diagnostic is passed in (a line with a 0 reading is parsed and bound).
- **Re-indenter and Format all keep more text as it is**: a stray closer no longer dedents the rest
  of the file; lines continuing a quoted string, NBSP and other Unicode spaces are kept; `Keep` line
  endings leave mixed CRLF/LF files alone.
- **The "Survey trip" template** writes `# team "name"` instead of `team ""`.
- **Workspace path lookups are case-sensitive on Linux** (`PerFile`, include resolution); Windows and
  macOS unchanged.
- Through **Therion.Syntax**: string values carry `"` where the source had `""`; a string token no
  longer spans lines except by backslash continuation; thconfig `RawArguments` hold no continuations;
  new warning TH0003 (unterminated string); `team ""` and empty `explo-team` give TH0065; the file
  node's `Span.End` is the real end of multi-line text.
- Through **Therion.Semantics**: `calibrate` is applied (value = (reading − zero error) × scale,
  before unit conversion; bearings normalised into [0, 360)) — lengths, bearings, inclinations and
  everything built on them change in centrelines that use it; do not apply it yourself as well.
  `x@sub` resolves relative to the survey it is written in, so projects with same-named sub-surveys
  get different equates, connectivity and references (they pointed at the first match). Station
  declaration spans of row- or mark-declared stations are the token span.

### Additive API
`TopodroidProjectScaffold.ExistingTargets`, `SourceFileIo.Read(path, bytes)`, `ShotInsertion.Find`
and `ShotInsertTarget`, `CodeFixes.ShotOutlier`, `ReadingCalibration` and the `ShotSymbol`
calibration properties, `EquateRef.Scope` / `EquateRecord.Scope` / `EquateRecord.ResolvedMembers`,
scope overloads of `ResolveStationSymbol`, `WorkspaceEquates.ResolveMember`,
`SurveyRefDecomposer.Decompose` and `WorkspaceSemanticModel.Build`, `DataQualityChecks.IsLeg`,
`TherionStrings.Unquote`/`SplitArguments`, `TherionTokenizer.IsUnterminatedString`.
`ProjectTemplateEngine.ValueProblems` and `TemplateValueProblem`: the values `Validate` rejects, as
data, for a UI that words the messages in its own language (`Validate` is unchanged).

Previews that show the change: `FormatAll.PlanShowingChanges` and `ProjectNormalize.PlanShowingChanges`
(one row per file on its first changed line, with a `Description` built from `FormatAll.Describe`,
`FormatChange` and `FormatChangeKinds`), `FixAll.PreviewByLine` (one row per changed line with every
edit on it applied), `HitSnippet.Around`, and the init-only `EditPreviewRow.Edits` / `Description`
with `AllEdits`. `FixAll.Keep` keeps every edit of a kept row; rows built as before behave as before.
