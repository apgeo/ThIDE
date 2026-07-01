// Implementation Plan �9bis.5 � Build menu + Compiler Output + Generated Files.
// One VM hosts the build pipeline (Build / Rebuild / Cancel), streams compiler
// output, and exposes the artifact list so the Loch / Aven quick actions can
// light up when matching outputs exist.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public enum OutputRowKind { Command, Normal, Summary }

/// <summary>Coarse Therion build stage parsed from its output (BUILD-05). Monotonic: only advances.</summary>
public enum BuildPhase { Idle, Starting, Reading, Processing, Exporting }

/// <summary>How the leading time column is rendered for every output row (#4).</summary>
public enum TimeColumnMode
{
    /// <summary>Elapsed since the previous line (e.g. "+5 s").</summary>
    SinceLast,
    /// <summary>Full wall-clock timestamp (e.g. "14:03:21").</summary>
    Timestamp,
    /// <summary>Elapsed since the build started (e.g. "+12.3 s").</summary>
    SinceStart,
}

/// <summary>One compiler-output row: raw text + classification + timing for the time column.</summary>
public sealed class CompilerOutputRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private static readonly IBrush ErrorBrush   = new ImmutableSolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly IBrush WarnBrush     = new ImmutableSolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x00));
    private static readonly IBrush CommandBrush  = new ImmutableSolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly IBrush SuccessBrush  = new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly IBrush NormalBrush   = new ImmutableSolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    private static readonly IBrush InfoBrush     = new ImmutableSolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75));
    private static readonly IBrush TimeBrush     = new ImmutableSolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));

    public string Text { get; }
    public string Severity { get; }
    public SourceSpan? Span { get; }
    public OutputRowKind Kind { get; }

    // Raw timing captured at creation; the time column display is recomputed from these
    // whenever the user flips the time-column mode (#4).
    public DateTimeOffset Timestamp { get; }
    public TimeSpan Delta { get; }
    public TimeSpan SinceStart { get; }

    private string _timeDisplay = string.Empty;
    public string TimeDisplay { get => _timeDisplay; private set => SetProperty(ref _timeDisplay, value); }

    public bool IsError { get; }
    public bool IsWarning { get; }
    public bool IsInfo { get; }
    public bool IsSuccess { get; }
    public IBrush TimeBrushValue => TimeBrush;

    // ---- severity icon (#4): info=gray, warning=amber, error=red ----
    // Per-glyph visibility flags so the cell can show the right colored icon without a
    // string-keyed resource lookup in XAML.
    public bool ShowSuccessIcon => Kind == OutputRowKind.Summary && IsSuccess;
    public bool ShowErrorIcon   => (Kind == OutputRowKind.Summary && !IsSuccess)
                                   || (Kind == OutputRowKind.Normal && IsError);
    public bool ShowWarnIcon     => Kind == OutputRowKind.Normal && IsWarning;
    public bool ShowInfoIcon     => Kind == OutputRowKind.Normal && IsInfo;

    /// <summary>Foreground for the text column: red for errors, amber for warnings, etc.</summary>
    public IBrush TextBrush { get; }
    public Avalonia.Media.FontWeight Weight =>
        Kind is OutputRowKind.Command or OutputRowKind.Summary ? FontWeight.SemiBold : FontWeight.Normal;

    // ---- clickable file-path link (#1) --------------------------------------
    /// <summary>The offending identifier from a Therion error (e.g. "E65a"); kept for future use (#1).</summary>
    public string? Symbol { get; }
    /// <summary>The line text before the file path (rendered normally).</summary>
    public string MessagePrefix { get; } = string.Empty;
    /// <summary>The file path inside the line (rendered as a blue, clickable hyperlink).</summary>
    public string MessagePath { get; } = string.Empty;
    /// <summary>The line text after the file path (rendered normally).</summary>
    public string MessageSuffix { get; } = string.Empty;
    /// <summary>True when a navigable file path was detected in this line.</summary>
    public bool HasLink => MessagePath.Length > 0 && Span is { } s && !string.IsNullOrEmpty(s.FilePath);

    public CompilerOutputRow(string text, string severity, SourceSpan? span,
        OutputRowKind kind, DateTimeOffset timestamp, TimeSpan delta, TimeSpan sinceStart,
        TimeColumnMode mode, bool success = false, string? linkText = null, string? symbol = null)
    {
        Text = text;
        Severity = severity;
        Span = span;
        Kind = kind;
        Symbol = symbol;

        // Split the line around the detected path so the path can be drawn/clicked as a link.
        if (!string.IsNullOrEmpty(linkText))
        {
            int idx = text.IndexOf(linkText, StringComparison.Ordinal);
            if (idx >= 0)
            {
                MessagePrefix = text[..idx];
                MessagePath = linkText;
                MessageSuffix = text[(idx + linkText.Length)..];
            }
            else { MessagePrefix = text; }
        }
        else { MessagePrefix = text; }
        Timestamp = timestamp;
        Delta = delta;
        SinceStart = sinceStart;

        bool err = string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase);
        bool warn = string.Equals(severity, "Warning", StringComparison.OrdinalIgnoreCase);
        // "average loop error" is a loop-closure statistic, not a failure — never color it
        // as an error/warning in either output view (#4).
        if ((err || warn) && text.Contains("average loop error", StringComparison.OrdinalIgnoreCase))
        { err = false; warn = false; }

        IsError = err;
        IsWarning = warn;
        IsSuccess = success;
        IsInfo = kind == OutputRowKind.Normal && !err && !warn;
        TextBrush = kind switch
        {
            OutputRowKind.Command => CommandBrush,
            OutputRowKind.Summary => success ? SuccessBrush : ErrorBrush,
            _ => IsError ? ErrorBrush : IsWarning ? WarnBrush : NormalBrush,
        };
        ApplyTimeMode(mode);
    }

    /// <summary>Recomputes <see cref="TimeDisplay"/> for the given column mode (#4/#12).</summary>
    public void ApplyTimeMode(TimeColumnMode mode)
    {
        TimeDisplay = mode switch
        {
            // Timestamp: full wall-clock with a 2-decimal millisecond part (#12).
            TimeColumnMode.Timestamp => Timestamp.ToString("HH:mm:ss.ff"),
            // Since-build-start: elapsed for every row, but the first (command) line shows the
            // full timestamp so the absolute start time is visible (#12).
            TimeColumnMode.SinceStart => Kind == OutputRowKind.Command
                ? Timestamp.ToString("HH:mm:ss")
                : FormatElapsed(SinceStart),
            // Since-last-line: delta for intermediate rows; the first/last (command/summary)
            // lines show the full wall-clock timestamp (#12).
            _ => Kind == OutputRowKind.Normal ? FormatElapsed(Delta) : Timestamp.ToString("HH:mm:ss"),
        };
    }

    /// <summary>Relative-time formatter: a space sits between the value and its unit (#4).</summary>
    internal static string FormatElapsed(TimeSpan d)
    {
        var ms = d.TotalMilliseconds;
        if (ms < 0) ms = 0;
        return ms < 1000 ? $"+{(int)ms} ms" : $"+{d.TotalSeconds:0.#} s";
    }
}
public sealed record ArtifactRow(string Path, string Kind, long SizeBytes, DateTimeOffset LastWriteUtc)
{
    /// <summary>True when a project input is newer than this output (BUILD-03).</summary>
    public bool IsStale { get; init; }
    /// <summary>True for .lox/.3d outputs that can be shown in a 3D viewer (drives the row's 3D buttons).</summary>
    public bool CanView3D =>
        System.IO.Path.GetExtension(Path).ToLowerInvariant() is ".lox" or ".3d";
    /// <summary>
    /// Per-file auto-open-after-build override (#7): null = use the general per-type setting,
    /// true = always open this file, false = never. Bound two-way to the grid's 3-state checkbox;
    /// persisted in <c>AppSettings.AutoOpenOverrides</c>.
    /// </summary>
    public bool? AutoOpen { get; set; }
    /// <summary>Short status text for the Generated Files grid.</summary>
    public string StateText => IsStale ? "⚠ stale" : "current";
    public string SizeDisplay => SizeBytes < 1024 ? $"{SizeBytes} B" : $"{SizeBytes / 1024} KB";
    /// <summary>File name only, for the dedicated File Name column (#4).</summary>
    public string FileName => System.IO.Path.GetFileName(Path);
    /// <summary>Local timestamp without the timezone offset, default format (#4).</summary>
    public string ModifiedDisplay => LastWriteUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    /// <summary>Icon resource key for the file type, shown in front of Kind (#4).</summary>
    public string IconKey => System.IO.Path.GetExtension(Path).ToLowerInvariant() switch
    {
        ".lox" or ".3d"           => "Icon.Cube",
        ".pdf" or ".svg" or ".dxf" => "Icon.Map",
        ".xvi"                    => "Icon.Xvi",
        ".png" or ".jpg"          => "Icon.File",
        ".log" or ".tlx"          => "Icon.Info",
        _                          => "Icon.File",
    };
}

/// <summary>A runnable export target parsed from the active thconfig (BUILD-01).</summary>
public sealed class BuildTarget
{
    public string Title { get; }
    public System.Windows.Input.ICommand BuildCommand { get; }
    public BuildTarget(string title, System.Windows.Input.ICommand buildCommand)
    {
        Title = title;
        BuildCommand = buildCommand;
    }
}

public partial class BuildViewModel : ViewModelBase
{
    private readonly ITherionCompiler _compiler;
    private readonly ICompileGate _gate;
    private readonly IShellOpener _shell;
    private readonly IExternalToolLocator _locator;
    private readonly IDocumentService _documents;
    private readonly IOutputArtifactCache _artifactCache;
    private readonly IWorkspaceSession? _session;
    private readonly IAppSettingsService? _settings;

    private CancellationTokenSource? _cts;

    /// <summary>Full raw stdout/stderr text, exactly as it came from the compiler (#raw panel).</summary>
    [ObservableProperty] private string _rawOutput = string.Empty;
    private readonly StringBuilder _rawBuffer = new();

    /// <summary>Raised for each output row as it is appended (drives the raw colored view).</summary>
    public event EventHandler<CompilerOutputRow>? OutputRowAdded;
    /// <summary>Raised when the output is cleared (new build / Clear command).</summary>
    public event EventHandler? OutputCleared;

    // Timestamp bookkeeping: the first/command row shows the wall-clock time; each subsequent
    // row shows the delta since the previous one; the final summary row shows wall-clock again.
    private DateTimeOffset _prevRowTime;
    private DateTimeOffset _buildStart;
    /// <summary>Working directory of the running build, used to resolve relative output paths (#1).</summary>
    private string? _buildWorkDir;

    /// <summary>Selected rendering for the time column (since-last / timestamp / since-start, #4).</summary>
    [ObservableProperty] private TimeColumnMode _timeColumnMode = TimeColumnMode.SinceLast;
    partial void OnTimeColumnModeChanged(TimeColumnMode value)
    {
        foreach (var row in _outputBuffer) row.ApplyTimeMode(value); // re-render existing rows
        OnPropertyChanged(nameof(TimeColumnModeIndex));
    }

    /// <summary>ComboBox-friendly view of <see cref="TimeColumnMode"/> (#4).</summary>
    public int TimeColumnModeIndex
    {
        get => (int)TimeColumnMode;
        set => TimeColumnMode = (TimeColumnMode)value;
    }

    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private string _status = string.Empty;
    /// <summary>#3: the "N artifact(s)" portion of a successful build's status, shown as a clickable link.</summary>
    [ObservableProperty] private string _statusArtifactCount = string.Empty;
    /// <summary>True when there's an artifact-count link to show next to the status.</summary>
    public bool HasArtifactLink => _statusArtifactCount.Length > 0;
    partial void OnStatusArtifactCountChanged(string value) => OnPropertyChanged(nameof(HasArtifactLink));
    // Any status change drops a stale artifact link; the success path re-sets it immediately after.
    partial void OnStatusChanged(string value) => StatusArtifactCount = string.Empty;

    // ---- build phases (BUILD-05): a stage label + stepped progress, alongside the spinner ----
    [ObservableProperty] private BuildPhase _phase;
    [ObservableProperty] private string _currentPhase = string.Empty;
    [ObservableProperty] private double _phaseProgress;

    private void SetPhase(BuildPhase p)
    {
        Phase = p;
        CurrentPhase = p switch
        {
            BuildPhase.Starting   => "Starting…",
            BuildPhase.Reading     => "Reading data…",
            BuildPhase.Processing  => "Processing…",
            BuildPhase.Exporting   => "Exporting…",
            _ => string.Empty,
        };
        PhaseProgress = p switch
        {
            BuildPhase.Starting => 0.08, BuildPhase.Reading => 0.3,
            BuildPhase.Processing => 0.6, BuildPhase.Exporting => 0.9, _ => 0,
        };
    }

    /// <summary>Advances the phase indicator from a Therion output line (monotonic).</summary>
    private void UpdatePhase(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var s = text.ToLowerInvariant();
        if (Phase < BuildPhase.Reading && s.Contains("reading")) SetPhase(BuildPhase.Reading);
        else if (Phase < BuildPhase.Processing &&
                 (s.Contains("processing") || s.Contains("compiling") || s.Contains("computing") || s.Contains("loops")))
            SetPhase(BuildPhase.Processing);
        else if (Phase < BuildPhase.Exporting &&
                 (s.Contains("writing") || s.Contains("exporting") || s.Contains("creating") || s.Contains(".pdf") || s.Contains(".lox")))
            SetPhase(BuildPhase.Exporting);
    }
    [ObservableProperty] private IReadOnlyList<CompilerOutputRow> _output = Array.Empty<CompilerOutputRow>();
    [ObservableProperty] private IReadOnlyList<ArtifactRow> _artifacts = Array.Empty<ArtifactRow>();
    [ObservableProperty] private bool _hasLoxArtifact;
    [ObservableProperty] private bool _hasAvenArtifact;
    /// <summary>True when at least one output is older than a project input (BUILD-03).</summary>
    [ObservableProperty] private bool _hasStaleArtifacts;
    [ObservableProperty] private CompilerOutputRow? _selectedOutput;
    [ObservableProperty] private ArtifactRow? _selectedArtifact;

    // ---- log file (#3) -------------------------------------------------------
    /// <summary>Path of the Therion log file detected after the build, if any.</summary>
    [ObservableProperty] private string? _logFilePath;
    /// <summary>True when a log file was produced and can be opened in the editor.</summary>
    [ObservableProperty] private bool _hasLog;

    // ---- build result indicator (#7) ----------------------------------------
    /// <summary>True once any build has completed in this session.</summary>
    [ObservableProperty] private bool _hasBuildResult;
    /// <summary>True when the last completed build had exit code 0.</summary>
    [ObservableProperty] private bool _lastBuildSucceeded;
    /// <summary>True when the last build produced diagnostics with Warning severity.</summary>
    [ObservableProperty] private bool _lastBuildHasWarnings;
    /// <summary>Number of warnings from the last build.</summary>
    [ObservableProperty] private int _lastBuildWarningCount;

    /// <summary>Raised when a compiler-output row with a span is activated.</summary>
    public event System.EventHandler<Therion.Core.SourceSpan>? NavigateRequested;

    [RelayCommand]
    private void NavigateOutput(CompilerOutputRow? row)
    {
        // Output-link spans carry a file + line but a zero length (so IsEmpty is true); navigate
        // whenever there's a file path rather than gating on IsEmpty, else the links never fire (#3).
        if (row?.Span is { } span && !string.IsNullOrEmpty(span.FilePath))
        {
            // NavigateToSpanAsync ignores IsEmpty (zero-length) spans, so a click on a compiler-
            // output link was silently dropped. Give the span a minimal extent — exactly as the
            // back/forward history does for its zero-length caret stops — so the jump happens (#4).
            if (span.IsEmpty) span = span with { Length = 1 };
            NavigateRequested?.Invoke(this, span);
        }
    }

    private readonly List<CompilerOutputRow> _outputBuffer = new();

    public BuildViewModel(
        ITherionCompiler compiler,
        ICompileGate gate,
        IShellOpener shell,
        IExternalToolLocator locator,
        IDocumentService documents,
        IOutputArtifactCache artifactCache,
        IWorkspaceSession? session = null,
        IAppSettingsService? settings = null,
        ILogService? log = null,
        INotificationService? notifications = null)
    {
        _compiler = compiler;
        _gate = gate;
        _shell = shell;
        _locator = locator;
        _documents = documents;
        _artifactCache = artifactCache;
        _session = session;
        _settings = settings;
        _log = log;
        _notifications = notifications;
        _documents.DocumentChanged += (_, _) => { RestoreLastArtifacts(); RefreshExportTargets(); };
        if (_session is not null) _session.Changed += (_, _) => RefreshExportTargets();
        RefreshExportTargets();
    }

    private readonly ILogService? _log;
    private readonly INotificationService? _notifications;   // UX-07 (tool-not-found toast)

    public BuildViewModel() : this(
        new NullCompiler(), new CompileGate(), new ShellOpener(),
        new NullLocator(), new NullDocumentService(), new NullArtifactCache())
    { } // Designer-only.

    public event EventHandler<ImmutableArray<Diagnostic>>? CompileCompleted;

    /// <summary>Raised when a build begins so the shell can surface the Compiler Output panel (#2).</summary>
    public event EventHandler? BuildStarted;

    public bool CanBuild => !IsBuilding && ResolveBuildEntry() is not null;

    /// <summary>
    /// The build target: the active editor file when it is itself a thconfig, otherwise the
    /// workspace's active thconfig; falls back to the active file when no thconfig is known.
    /// </summary>
    private string? ResolveBuildEntry()
    {
        var active = _documents.CurrentPath;
        if (active is not null && IsThconfig(active)) return active;
        return _session?.ActiveThconfig?.FullPath ?? active;
    }

    private static bool IsThconfig(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length == 0
            ? string.Equals(Path.GetFileName(path), "thconfig", StringComparison.OrdinalIgnoreCase)
            : ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        var entry = ResolveBuildEntry();
        if (entry is null) { Status = "No project loaded."; return; }
        await RunBuildAsync(entry).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs Therion on <paramref name="entry"/> (which may be a generated temporary thconfig for
    /// BUILD-01/02). <paramref name="tempThconfig"/>, when set, is deleted after the build.
    /// </summary>
    public async Task RunBuildAsync(string entry, string? tempThconfig = null, string? label = null)
    {
        using var lease = _gate.TryAcquire();
        if (lease is null) { Status = "A compilation is already in progress."; return; }

        IsBuilding = true;
        HasBuildResult = false;          // clear the previous success/error status-bar icon
        Status = "Compiling…";           // clear the previous "succeeded/failed" label (#3)
        StatusArtifactCount = string.Empty;
        HasLog = false; LogFilePath = null;           // clear the previous build's log (#3)
        SetPhase(BuildPhase.Starting);                // BUILD-05
        _log?.Info($"Build started: {label ?? entry}");
        BuildStarted?.Invoke(this, EventArgs.Empty);  // surface the Compiler Output panel (#2)
        ClearOutputState();
        _cts = new CancellationTokenSource();

        // First row: the full command with its arguments.
        var tool = await _locator.FindAsync(ExternalToolLocator.Therion, _cts.Token).ConfigureAwait(true);
        var command = tool is null ? $"therion \"{entry}\"" : $"\"{tool.Path}\" \"{entry}\"";
        _buildStart = DateTimeOffset.Now;
        _prevRowTime = _buildStart;
        _buildWorkDir = Path.GetDirectoryName(Path.GetFullPath(entry)); // resolve output paths (#1)
        AddRow(new CompilerOutputRow(command, "Command", null, OutputRowKind.Command,
            _buildStart, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode));

        EnsureExportOutputDirectories(entry);   // create any missing output folders (opt-out in Preferences)

        var sw = Stopwatch.StartNew();
        var progress = new Progress<CompilerOutputLine>(line => { AddRow(MakeRow(line)); UpdatePhase(line.Text); });

        try
        {
            var result = await _compiler.CompileAsync(entry, progress, _cts.Token).ConfigureAwait(true);
            sw.Stop();
            UpdateArtifacts(result.Artifacts);
            DetectLog(entry, _buildStart);
            _artifactCache.Save(entry, "unknown", result.Artifacts);
            var warnCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            bool ok = result.ExitCode == 0;
            HasBuildResult = true;
            LastBuildSucceeded = ok;
            LastBuildHasWarnings = warnCount > 0;
            LastBuildWarningCount = warnCount;
            AddRow(SummaryRow(ok, warnCount, sw.Elapsed, result.Artifacts.Length, _buildStart));
            // #3: on success the "N artifact(s)" count is shown as a separate clickable link.
            if (ok)
            {
                Status = result.Artifacts.Length > 0 ? "Compilation succeeded" : "Compilation succeeded (no outputs).";
                StatusArtifactCount = result.Artifacts.Length > 0 ? $"{result.Artifacts.Length} artifact(s)" : string.Empty;
            }
            else
            {
                Status = $"Compilation failed (exit {result.ExitCode}).";
                StatusArtifactCount = string.Empty;
            }
            _log?.Log(
                ok ? (warnCount > 0 ? LogVerbosity.Warning : LogVerbosity.Info) : LogVerbosity.Error,
                ok
                    ? $"Build succeeded: {result.Artifacts.Length} artifact(s)" + (warnCount > 0 ? $", {warnCount} warning(s)" : "")
                    : $"Build failed (exit {result.ExitCode}, {warnCount} warning(s)).");
            CompileCompleted?.Invoke(this, result.Diagnostics);
            if (ok) AutoOpenOutputs(result.Artifacts);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Status = "Compilation cancelled.";
            _log?.Warning("Build cancelled.");
            AddRow(new CompilerOutputRow("Compilation cancelled.", "Error", null, OutputRowKind.Summary,
                DateTimeOffset.Now, TimeSpan.Zero, sw.Elapsed, TimeColumnMode));
        }
        catch (Exception ex)
        {
            Status = "Compilation error: " + ex.Message;
            _log?.Error("Build error: " + ex.Message);
        }
        finally
        {
            IsBuilding = false;
            _cts?.Dispose();
            _cts = null;
            SetPhase(BuildPhase.Idle); // BUILD-05: clear the phase indicator
            if (!string.IsNullOrEmpty(tempThconfig))
                try { File.Delete(tempThconfig); } catch { /* best-effort temp cleanup */ }
        }
    }

    private CompilerOutputRow MakeRow(CompilerOutputLine line)
    {
        var now = DateTimeOffset.Now;
        var delta = now - _prevRowTime;
        _prevRowTime = now;

        // The path as Therion printed it (used to highlight it in the line); resolve it against
        // the build's working directory so the link opens the right file even when relative (#1).
        string? displayPath = line.Span?.FilePath;
        var navSpan = ResolveOutputSpan(line.Span);

        return new CompilerOutputRow(line.Text, line.Severity.ToString(), navSpan,
            OutputRowKind.Normal, now, delta, now - _buildStart, TimeColumnMode,
            linkText: displayPath, symbol: line.Symbol);
    }

    /// <summary>Resolves a relative output-path span to an absolute one against the build dir (#1).</summary>
    private SourceSpan? ResolveOutputSpan(SourceSpan? span)
    {
        if (span is not { } s || string.IsNullOrEmpty(s.FilePath)) return span;
        try
        {
            if (Path.IsPathRooted(s.FilePath) || _buildWorkDir is null) return span;
            var rel = s.FilePath.Replace('/', Path.DirectorySeparatorChar);
            var abs = Path.GetFullPath(Path.Combine(_buildWorkDir, rel));
            return s with { FilePath = abs };
        }
        catch { return span; }
    }

    /// <summary>
    /// Before compiling, ensures each export command's output directory exists, creating it
    /// recursively when missing and logging an info row for each one created. Gated by the
    /// <c>EnsureOutputDirectories</c> setting (on by default); best-effort — a failure here never
    /// aborts the build, so Therion still runs and reports the real error.
    /// </summary>
    private void EnsureExportOutputDirectories(string entry)
    {
        if (_settings is null || !_settings.Current.EnsureOutputDirectories) return;
        if (_buildWorkDir is null) return;
        try
        {
            if (!File.Exists(entry)) return;
            foreach (var dir in ResolveExportOutputDirectories(File.ReadAllText(entry), _buildWorkDir))
            {
                if (Directory.Exists(dir)) continue;
                try
                {
                    Directory.CreateDirectory(dir);
                    var now = DateTimeOffset.Now;
                    AddRow(new CompilerOutputRow($"Created missing output directory: {dir}", "Info", null,
                        OutputRowKind.Normal, now, now - _prevRowTime, now - _buildStart, TimeColumnMode));
                    _prevRowTime = now;
                    _log?.Info($"Created missing output directory: {dir}");
                }
                catch (Exception ex)
                {
                    _log?.Warning($"Could not create output directory '{dir}': {ex.Message}");
                }
            }
        }
        catch { /* best-effort pre-check: never block the build on it */ }
    }

    /// <summary>
    /// Distinct absolute output directories declared by the <c>export … -o &lt;path&gt;</c> commands in
    /// <paramref name="thconfigText"/>, resolved against <paramref name="workDir"/>. Outputs with no
    /// folder part (a bare filename) resolve to the work dir itself, which always exists.
    /// </summary>
    internal static IReadOnlyList<string> ResolveExportOutputDirectories(string thconfigText, string workDir)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in ThconfigExportEditor.ParseExports(thconfigText))
        {
            if (string.IsNullOrWhiteSpace(info.Output)) continue;
            string? dir;
            try
            {
                var rel = info.Output.Trim().Replace('/', Path.DirectorySeparatorChar);
                var full = Path.IsPathRooted(rel) ? rel : Path.Combine(workDir, rel);
                dir = Path.GetDirectoryName(Path.GetFullPath(full));
            }
            catch { continue; }
            if (!string.IsNullOrEmpty(dir) && seen.Add(dir)) dirs.Add(dir);
        }
        return dirs;
    }

    private CompilerOutputRow SummaryRow(bool ok, int warnCount, TimeSpan elapsed, int artifactCount,
        DateTimeOffset buildStart)
    {
        var sb = new StringBuilder();
        sb.Append(ok ? "Compilation finished successfully" : "Compilation finished with errors");
        if (warnCount > 0) sb.Append(ok ? $" (with {warnCount} warning(s))" : $" and {warnCount} warning(s)");
        sb.Append($" in {elapsed.TotalSeconds:0.00} seconds");
        if (ok && artifactCount > 0) sb.Append($" and generated {artifactCount} output file(s)");
        sb.Append('.');
        var now = DateTimeOffset.Now;
        return new CompilerOutputRow(sb.ToString(), ok ? "Info" : "Error", null, OutputRowKind.Summary,
            now, TimeSpan.Zero, now - buildStart, TimeColumnMode, success: ok);
    }

    private void AddRow(CompilerOutputRow row)
    {
        _outputBuffer.Add(row);
        Output = _outputBuffer.ToArray();   // immutable swap for binding
        _rawBuffer.AppendLine(row.Text);
        RawOutput = _rawBuffer.ToString();
        OutputRowAdded?.Invoke(this, row);
    }

    private void ClearOutputState()
    {
        _outputBuffer.Clear();
        Output = Array.Empty<CompilerOutputRow>();
        _rawBuffer.Clear();
        RawOutput = string.Empty;
        _prevRowTime = DateTimeOffset.Now;
        OutputCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens outputs after a successful build (#7). A per-file override (Auto-open checkbox) wins:
    /// <c>true</c> always opens, <c>false</c> never; otherwise the general per-type setting applies,
    /// subject to the open-all-vs-first rule. Explicit "always" files always open.
    /// </summary>
    private void AutoOpenOutputs(ImmutableArray<OutputArtifact> artifacts)
    {
        if (_settings is null) return;
        var s = _settings.Current;
        foreach (var path in ResolveAutoOpenPaths(
            artifacts.Select(a => a.Path).ToList(),
            s.OpenLoxAfterBuild, s.Open3dAfterBuild, s.OpenPdfAfterBuild, s.OpenAllOutputsAfterBuild,
            s.AutoOpenOverrides))
            _shell.Open(path);
    }

    /// <summary>
    /// Pure auto-open decision (#7): explicit "always" overrides open first and unconditionally;
    /// "never" overrides are skipped; the rest follow the general per-type flags, subject to the
    /// open-all-vs-first rule. Order: explicit opens, then defaults; de-duplicated.
    /// </summary>
    internal static IReadOnlyList<string> ResolveAutoOpenPaths(
        IReadOnlyList<string> artifactPaths, bool openLox, bool open3d, bool openPdf, bool openAll,
        IReadOnlyDictionary<string, bool> overrides)
    {
        var defaultExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (openLox) defaultExts.Add(".lox");
        if (open3d) defaultExts.Add(".3d");
        if (openPdf) defaultExts.Add(".pdf");

        var explicitOpen = new List<string>();
        var byDefault = new List<string>();
        foreach (var path in artifactPaths)
        {
            switch (LookupAutoOpen(overrides, path))
            {
                case true: explicitOpen.Add(path); break;
                case false: break;   // explicitly suppressed
                default:
                    if (defaultExts.Contains(Path.GetExtension(path))) byDefault.Add(path);
                    break;
            }
        }

        var toOpen = new List<string>(explicitOpen);
        toOpen.AddRange(openAll ? byDefault : byDefault.Take(1));
        return toOpen.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Looks up a persisted auto-open override for <paramref name="path"/> (case-insensitive, normalized).</summary>
    private static bool? LookupAutoOpen(IReadOnlyDictionary<string, bool>? overrides, string path)
    {
        if (overrides is null || overrides.Count == 0) return null;
        var key = NormPath(path);
        foreach (var kv in overrides)
            if (string.Equals(NormPath(kv.Key), key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    private static string NormPath(string path)
    {
        try { return Path.GetFullPath(path); } catch { return path; }
    }

    /// <summary>#7: persists (or clears) the per-file auto-open override for the grid's 3-state checkbox.</summary>
    public void SetAutoOpenOverride(string path, bool? state)
    {
        if (_settings is null || string.IsNullOrEmpty(path)) return;
        var key = NormPath(path);
        var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _settings.Current.AutoOpenOverrides)
            if (!string.Equals(NormPath(kv.Key), key, StringComparison.OrdinalIgnoreCase)) dict[kv.Key] = kv.Value;
        if (state is bool b) dict[key] = b;
        _settings.Save(_settings.Current with { AutoOpenOverrides = dict });
    }

    /// <summary>#1: open a specific .lox in Loch / .3d in Aven (falls back to the OS default app).</summary>
    [RelayCommand]
    private async Task OpenInExternalViewer(ArtifactRow? row)
    {
        if (row is null) return;
        WarnIfStale(row);
        string toolId = HasExt(row.Path, ".lox") ? ExternalToolLocator.Loch
            : HasExt(row.Path, ".3d") ? ExternalToolLocator.Aven : string.Empty;
        if (toolId.Length > 0 && await _locator.FindAsync(toolId).ConfigureAwait(true) is { } tool)
        {
            try
            {
                Process.Start(new ProcessStartInfo(tool.Path, $"\"{row.Path}\"") { UseShellExecute = false });
                return;
            }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(row.Path);
    }

    /// <summary>Finds a Therion log (.log/.tlx) written during this build, for the open-log button (#3).</summary>
    private void DetectLog(string entry, DateTimeOffset buildStart)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(entry));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            var floor = buildStart.AddSeconds(-2); // small tolerance for clock granularity
            var log = new DirectoryInfo(dir)
                .EnumerateFiles("*.*")
                .Where(f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                         || f.Extension.Equals(".tlx", StringComparison.OrdinalIgnoreCase))
                .Where(f => (DateTimeOffset)f.LastWriteTimeUtc >= floor)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (log is not null) { LogFilePath = log.FullName; HasLog = true; }
        }
        catch { /* best-effort log detection */ }
    }

    /// <summary>Opens the detected build log in the editor as a plain text file (#3).</summary>
    [RelayCommand]
    private async Task OpenLog()
    {
        if (HasLog && LogFilePath is { } path && File.Exists(path))
            await _documents.OpenFileAsync(path).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RebuildAsync() => BuildAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearOutput() => ClearOutputState();

    /// <summary>Returns all output rows as plain text for clipboard copy.</summary>
    public string OutputAsText() =>
        string.Join(Environment.NewLine, Output.Select(r => $"{r.Severity,-8} {r.Text}"));

    [RelayCommand]
    private async Task OpenInLochAsync()
    {
        var lox = Artifacts.FirstOrDefault(a => HasExt(a.Path, ".lox"));
        if (lox is null) return;
        WarnIfStale(lox);
        var tool = await _locator.FindAsync(ExternalToolLocator.Loch).ConfigureAwait(true);
        if (tool is not null)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool.Path, $"\"{lox.Path}\"") { UseShellExecute = false }); return; }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(lox.Path);
    }

    [RelayCommand]
    private async Task OpenInAvenAsync()
    {
        var d3 = Artifacts.FirstOrDefault(a => HasExt(a.Path, ".3d"));
        if (d3 is null) return;
        WarnIfStale(d3);
        var tool = await _locator.FindAsync(ExternalToolLocator.Aven).ConfigureAwait(true);
        if (tool is not null)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool.Path, $"\"{d3.Path}\"") { UseShellExecute = false }); return; }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(d3.Path);
    }

    [RelayCommand]
    private void OpenLastOutputFolder()
    {
        var first = Artifacts.FirstOrDefault();
        if (first is null) return;
        var dir = Path.GetDirectoryName(first.Path);
        if (!string.IsNullOrEmpty(dir)) _shell.Open(dir);
    }

    // ---- BUILD-01: export targets parsed from the active thconfig ----

    /// <summary>The active thconfig's export commands, each runnable on its own (BUILD-01).</summary>
    [ObservableProperty] private IReadOnlyList<BuildTarget> _exportTargets = Array.Empty<BuildTarget>();
    /// <summary>True when the active thconfig declares at least one export target.</summary>
    [ObservableProperty] private bool _hasExportTargets;

    private void RefreshExportTargets()
    {
        var cfg = _session?.ActiveThconfig?.FullPath;
        if (cfg is null || !File.Exists(cfg)) { ExportTargets = Array.Empty<BuildTarget>(); HasExportTargets = false; return; }
        try
        {
            var infos = ThconfigExportEditor.ParseExports(File.ReadAllText(cfg));
            ExportTargets = infos
                .Select(info => new BuildTarget(info.Title,
                    new RelayCommand(() => _ = BuildSingleExportAsync(cfg, info))))
                .ToList();
            HasExportTargets = ExportTargets.Count > 0;
        }
        catch { ExportTargets = Array.Empty<BuildTarget>(); HasExportTargets = false; }
    }

    /// <summary>BUILD-01: builds a derived thconfig that keeps only <paramref name="info"/>'s export.</summary>
    private async Task BuildSingleExportAsync(string cfg, ExportTargetInfo info)
    {
        try
        {
            var modified = ThconfigExportEditor.IsolateExport(File.ReadAllText(cfg), info);
            var temp = Path.Combine(Path.GetDirectoryName(cfg)!, ".therionproc-target.thconfig");
            File.WriteAllText(temp, modified);
            await RunBuildAsync(temp, temp, $"{Path.GetFileName(cfg)} :: {info.Title}").ConfigureAwait(true);
        }
        catch (Exception ex) { Status = ex.Message; _log?.Error("Target build error: " + ex.Message); }
    }

    /// <summary>BUILD-02: runs a composed quick-export (writes a temp thconfig, builds, opens the result).</summary>
    public async Task RunQuickExportAsync(string exportBlock, string outputFileName)
    {
        var cfg = _session?.ActiveThconfig?.FullPath ?? ResolveBuildEntry();
        if (cfg is null || !File.Exists(cfg)) { Status = "No project to export."; return; }
        try
        {
            var dir = Path.GetDirectoryName(cfg)!;
            var modified = ThconfigExportEditor.ComposeExport(File.ReadAllText(cfg), exportBlock);
            var temp = Path.Combine(dir, ".therionproc-export.thconfig");
            File.WriteAllText(temp, modified);
            await RunBuildAsync(temp, temp, $"Quick export → {outputFileName}").ConfigureAwait(true);
            var outPath = Path.Combine(dir, outputFileName);
            if (LastBuildSucceeded && File.Exists(outPath)) _shell.Open(outPath);
        }
        catch (Exception ex) { Status = ex.Message; _log?.Error("Quick export error: " + ex.Message); }
    }

    /// <summary>Raised when the user asks for the Quick Export dialog (BUILD-02); the shell shows it.</summary>
    public event EventHandler? QuickExportRequested;
    [RelayCommand] private void ShowQuickExport() => QuickExportRequested?.Invoke(this, EventArgs.Empty);

    // ---- BUILD-06: external round-trips (survex tools on .3d + therion --print-*) ----

    /// <summary>survex <c>dump3d</c> on the emitted .3d; output streams into the Compiler Output panel.</summary>
    [RelayCommand]
    private async Task Dump3d()
    {
        var d3 = Artifacts.FirstOrDefault(a => HasExt(a.Path, ".3d"));
        if (d3 is null) return;
        await RunExternalAsync(ExternalToolLocator.Dump3d, $"\"{d3.Path}\"", "dump3d", Path.GetDirectoryName(d3.Path)).ConfigureAwait(true);
    }

    /// <summary>survex <c>extend</c>: build an extended-elevation .3d next to the model and open it.</summary>
    [RelayCommand]
    private async Task Extend()
    {
        var d3 = Artifacts.FirstOrDefault(a => HasExt(a.Path, ".3d"));
        if (d3 is null) return;
        var outPath = Path.Combine(Path.GetDirectoryName(d3.Path)!,
            Path.GetFileNameWithoutExtension(d3.Path) + "_extend.3d");
        var ok = await RunExternalAsync(ExternalToolLocator.Extend, $"\"{d3.Path}\" \"{outPath}\"", "extend", Path.GetDirectoryName(d3.Path)).ConfigureAwait(true);
        if (ok && File.Exists(outPath)) _shell.Open(outPath);
    }

    [RelayCommand]
    private Task PrintTherionVersion() => RunExternalAsync(ExternalToolLocator.Therion, "--version", "therion --version", _buildWorkDir);

    [RelayCommand]
    private Task PrintTherionEnvironment() => RunExternalAsync(ExternalToolLocator.Therion, "--print-environment", "therion --print-environment", _buildWorkDir);

    /// <summary>
    /// Runs an external tool with raw args and streams its stdout/stderr into the Compiler Output
    /// panel. Returns true on exit code 0. Used for the BUILD-06 round-trips (not the main compile).
    /// </summary>
    private async Task<bool> RunExternalAsync(string toolId, string args, string label, string? workDir)
    {
        if (IsBuilding) { Status = "A compilation is already in progress."; return false; }
        var tool = await _locator.FindAsync(toolId).ConfigureAwait(true);
        if (tool is null)
        {
            Status = $"{toolId} not found.";
            _log?.Warning($"{toolId} not found on PATH or in External Tools settings.");
            _notifications?.Warning(TherionProc.Resources.Tr.Get("Notif_ToolNotFoundTitle"),
                string.Format(TherionProc.Resources.Tr.Get("Notif_ToolNotFoundMsg"), toolId));   // UX-07
            return false;
        }

        BuildStarted?.Invoke(this, EventArgs.Empty); // surface the output panel (#2)
        ClearOutputState();
        _buildStart = DateTimeOffset.Now;
        _prevRowTime = _buildStart;
        AddRow(new CompilerOutputRow($"\"{tool.Path}\" {args}", "Command", null, OutputRowKind.Command,
            _buildStart, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode));
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool.Path,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
            using var proc = new Process { StartInfo = psi };
            proc.Start();
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(true)) is not null)
                AddRow(PlainRow(line, "Info"));
            var err = await proc.StandardError.ReadToEndAsync().ConfigureAwait(true);
            foreach (var el in err.Split('\n'))
                if (el.Trim().Length > 0) AddRow(PlainRow(el.TrimEnd('\r'), "Warning"));
            await proc.WaitForExitAsync().ConfigureAwait(true);
            AddRow(SummaryRow(proc.ExitCode == 0, 0, DateTimeOffset.Now - _buildStart, 0, _buildStart));
            _log?.Info($"{label} finished (exit {proc.ExitCode}).");
            Status = $"{label} finished (exit {proc.ExitCode}).";
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Status = $"{label} error: {ex.Message}";
            _log?.Error($"{label} error: {ex.Message}");
            return false;
        }
    }

    private CompilerOutputRow PlainRow(string text, string severity)
    {
        var now = DateTimeOffset.Now;
        var delta = now - _prevRowTime;
        _prevRowTime = now;
        return new CompilerOutputRow(text, severity, null, OutputRowKind.Normal, now, delta, now - _buildStart, TimeColumnMode);
    }

    /// <summary>Raised to view an artifact in the in-app map viewer (VIS-05) instead of externally.</summary>
    public event EventHandler<string>? ViewMapRequested;

    [RelayCommand]
    private void OpenArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        // Open a clicked PDF in the in-app map viewer when the option is on (default) and the viewer is
        // enabled; everything else — and PDFs when the option is off — opens in the external default app.
        if (HasExt(row.Path, ".pdf")
            && _settings?.Current is { OpenPdfInInternalViewer: true, EnableInAppViewer: true })
        {
            ViewMapRequested?.Invoke(this, row.Path);
            return;
        }
        _shell.Open(row.Path);
    }

    [RelayCommand]
    private void RevealArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        _shell.RevealInFileManager(row.Path);
    }

    /// <summary>Raised when the user asks to view an artifact in the embedded 3D viewer (VIS-01).</summary>
    public event EventHandler<string>? View3DRequested;

    /// <summary>Generated Files → "View in internal 3D viewer": only .lox/.3d are viewable.</summary>
    [RelayCommand]
    private void ViewIn3D(ArtifactRow? row)
    {
        if (row is null) return;
        if (!HasExt(row.Path, ".lox") && !HasExt(row.Path, ".3d"))
        {
            Status = $"{Path.GetFileName(row.Path)} isn’t a 3D model (.lox / .3d).";
            return;
        }
        View3DRequested?.Invoke(this, row.Path);
    }

    private void UpdateArtifacts(ImmutableArray<OutputArtifact> artifacts)
    {
        // BUILD-03: an output is "stale" if any project input is newer than it.
        var newest = NewestInputUtc();
        var overrides = _settings?.Current.AutoOpenOverrides;
        var rows = artifacts
            .Select(a => new ArtifactRow(a.Path, a.Kind, a.SizeBytes, a.LastWriteUtc)
            {
                IsStale = newest is { } n && a.LastWriteUtc < n,
                AutoOpen = LookupAutoOpen(overrides, a.Path),   // #7: restore the persisted override
            })
            .OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();
        Artifacts = rows;
        HasLoxArtifact  = rows.Any(a => HasExt(a.Path, ".lox"));
        HasAvenArtifact = rows.Any(a => HasExt(a.Path, ".3d"));
        HasStaleArtifacts = rows.Any(a => a.IsStale);
    }

    /// <summary>Newest last-write time across the project's source files (BUILD-03 stale check).</summary>
    private DateTimeOffset? NewestInputUtc()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_session?.Model is { } model)
        {
            foreach (var k in model.PerFile.Keys) files.Add(k);
            foreach (var (from, to) in model.FileGraphEdges) { files.Add(from); files.Add(to); }
        }
        if (_session?.ActiveThconfig?.FullPath is { } cfg) files.Add(cfg);

        DateTimeOffset? newest = null;
        foreach (var f in files)
        {
            try
            {
                if (!File.Exists(f)) continue;
                var t = (DateTimeOffset)File.GetLastWriteTimeUtc(f);
                if (newest is null || t > newest) newest = t;
            }
            catch { /* skip unreadable */ }
        }
        return newest;
    }

    /// <summary>True when <paramref name="path"/> ends with the given (dotted) extension, case-insensitively.</summary>
    private static bool HasExt(string path, string ext) =>
        string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);

    /// <summary>BUILD-03: surface a warning before opening an out-of-date output.</summary>
    private void WarnIfStale(ArtifactRow row)
    {
        if (!row.IsStale) return;
        Status = $"Opening a stale output ({Path.GetFileName(row.Path)}) — rebuild to refresh.";
        _log?.Warning($"Opening a stale output '{Path.GetFileName(row.Path)}': a project input changed since the last build. Rebuild to refresh.");
    }

    private void RestoreLastArtifacts()
    {
        var entry = _documents.CurrentPath;
        if (entry is null) return;
        var cached = _artifactCache.Load(entry, "unknown");
        // Only repopulate when the newly-active file actually has cached build outputs.
        // A bare document change (e.g. returning focus after opening an artifact externally)
        // must not wipe the freshly-built artifact list to an empty grid (#4).
        if (!cached.IsDefaultOrEmpty) UpdateArtifacts(cached);
    }

    /// <summary>
    /// Navigates to the place in the active thconfig where this output file is defined (#7):
    /// finds the artifact's file name (then its stem) in the thconfig text and scrolls there.
    /// </summary>
    [RelayCommand]
    private async Task GoToArtifactDefinition(ArtifactRow? row)
    {
        if (row is null) return;
        var thconfig = ResolveBuildEntry();
        if (thconfig is null || !File.Exists(thconfig))
        {
            await Views.MessageDialog.ShowOverMainAsync("Go to definition",
                "No thconfig is available to search for this output's definition.").ConfigureAwait(true);
            return;
        }

        string text;
        try { text = await File.ReadAllTextAsync(thconfig).ConfigureAwait(true); }
        catch
        {
            await Views.MessageDialog.ShowOverMainAsync("Go to definition",
                $"Could not read {Path.GetFileName(thconfig)}.").ConfigureAwait(true);
            return;
        }

        var fileName = Path.GetFileName(row.Path);
        int idx = text.IndexOf(fileName, StringComparison.OrdinalIgnoreCase);
        int len = fileName.Length;
        if (idx < 0)
        {
            var stem = Path.GetFileNameWithoutExtension(row.Path);
            idx = text.IndexOf(stem, StringComparison.OrdinalIgnoreCase);
            len = stem.Length;
        }
        if (idx < 0)
        {
            await Views.MessageDialog.ShowOverMainAsync("Go to definition",
                $"Could not find where \"{fileName}\" is defined in {Path.GetFileName(thconfig)}.\n\n" +
                "Therion may derive this output name automatically rather than from an explicit export line.")
                .ConfigureAwait(true);
            return;
        }

        await _documents.NavigateToSpanAsync(SpanAt(thconfig, text, idx, len)).ConfigureAwait(true);
    }

    private static Therion.Core.SourceSpan SpanAt(string path, string text, int offset, int length)
    {
        int line = 1, col = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 1; } else col++;
        }
        return new Therion.Core.SourceSpan(path,
            new Therion.Core.SourceLocation(line, col),
            new Therion.Core.SourceLocation(line, col + length), offset, length);
    }

    // ---- #3: clickable "N artifact(s)" status link ----

    /// <summary>Raised when the artifact-count link is clicked, so the shell can surface + flash the Generated Files panel.</summary>
    public event EventHandler? ShowOutputsRequested;

    /// <summary>
    /// #3: surface the Generated Files panel and open the active thconfig at its first
    /// <c>export model</c> command (highlighting that line).
    /// </summary>
    [RelayCommand]
    private async Task ShowOutputs()
    {
        ShowOutputsRequested?.Invoke(this, EventArgs.Empty);   // panel show + flash (shell)
        await NavigateToFirstModelExportAsync().ConfigureAwait(true);
    }

    private async Task NavigateToFirstModelExportAsync()
    {
        var cfg = ResolveBuildEntry();
        if (cfg is null || !File.Exists(cfg)) return;
        try
        {
            var text = await File.ReadAllTextAsync(cfg).ConfigureAwait(true);
            var model = ThconfigExportEditor.ParseExports(text)
                .FirstOrDefault(e => e.Title.StartsWith("export model", StringComparison.Ordinal));
            if (model is null) return;
            await _documents.NavigateToSpanAsync(LineSpan(cfg, text, model.StartLine)).ConfigureAwait(true);
        }
        catch { /* best-effort navigation */ }
    }

    /// <summary>A span covering the whole 1-based <paramref name="line1"/> of <paramref name="text"/> (for highlight).</summary>
    private static Therion.Core.SourceSpan LineSpan(string path, string text, int line1)
    {
        int line = 1, offset = 0;
        while (line < line1 && offset < text.Length) { if (text[offset] == '\n') line++; offset++; }
        int end = offset;
        while (end < text.Length && text[end] is not ('\n' or '\r')) end++;
        int len = end - offset;
        return new Therion.Core.SourceSpan(path,
            new Therion.Core.SourceLocation(line1, 1),
            new Therion.Core.SourceLocation(line1, len + 1), offset, len);
    }

    // ---- Designer-only null sinks (kept private � never resolved at runtime) ----

    private sealed class NullCompiler : ITherionCompiler
    {
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default) =>
            new(new CompileResult(0, ImmutableArray<Diagnostic>.Empty, ImmutableArray<OutputArtifact>.Empty));
    }
    private sealed class NullLocator : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => new((ToolInfo?)null);
    }
    private sealed class NullArtifactCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => ImmutableArray<OutputArtifact>.Empty;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) { }
        public void Clear(string entryPointPath, string therionVersion) { }
    }
}
