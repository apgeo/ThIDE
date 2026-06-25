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

    public CompilerOutputRow(string text, string severity, SourceSpan? span,
        OutputRowKind kind, DateTimeOffset timestamp, TimeSpan delta, TimeSpan sinceStart,
        TimeColumnMode mode, bool success = false)
    {
        Text = text;
        Severity = severity;
        Span = span;
        Kind = kind;
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
    [ObservableProperty] private IReadOnlyList<CompilerOutputRow> _output = Array.Empty<CompilerOutputRow>();
    [ObservableProperty] private IReadOnlyList<ArtifactRow> _artifacts = Array.Empty<ArtifactRow>();
    [ObservableProperty] private bool _hasLoxArtifact;
    [ObservableProperty] private bool _hasAvenArtifact;
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
        if (row?.Span is { } span && !span.IsEmpty)
            NavigateRequested?.Invoke(this, span);
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
        IAppSettingsService? settings = null)
    {
        _compiler = compiler;
        _gate = gate;
        _shell = shell;
        _locator = locator;
        _documents = documents;
        _artifactCache = artifactCache;
        _session = session;
        _settings = settings;
        _documents.DocumentChanged += (_, _) => RestoreLastArtifacts();
    }

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

        using var lease = _gate.TryAcquire();
        if (lease is null) { Status = "A compilation is already in progress."; return; }

        IsBuilding = true;
        HasBuildResult = false;          // clear the previous success/error status-bar icon
        HasLog = false; LogFilePath = null;           // clear the previous build's log (#3)
        BuildStarted?.Invoke(this, EventArgs.Empty);  // surface the Compiler Output panel (#2)
        ClearOutputState();
        _cts = new CancellationTokenSource();

        // First row: the full command with its arguments.
        var tool = await _locator.FindAsync(ExternalToolLocator.Therion, _cts.Token).ConfigureAwait(true);
        var command = tool is null ? $"therion \"{entry}\"" : $"\"{tool.Path}\" \"{entry}\"";
        _buildStart = DateTimeOffset.Now;
        _prevRowTime = _buildStart;
        AddRow(new CompilerOutputRow(command, "Command", null, OutputRowKind.Command,
            _buildStart, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode));

        var sw = Stopwatch.StartNew();
        var progress = new Progress<CompilerOutputLine>(line => AddRow(MakeRow(line)));

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
            Status = ok
                ? $"Compilation succeeded ({result.Artifacts.Length} artifact(s))."
                : $"Compilation failed (exit {result.ExitCode}).";
            CompileCompleted?.Invoke(this, result.Diagnostics);
            if (ok) AutoOpenOutputs(result.Artifacts);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Status = "Compilation cancelled.";
            AddRow(new CompilerOutputRow("Compilation cancelled.", "Error", null, OutputRowKind.Summary,
                DateTimeOffset.Now, TimeSpan.Zero, sw.Elapsed, TimeColumnMode));
        }
        catch (Exception ex)
        {
            Status = "Compilation error: " + ex.Message;
        }
        finally
        {
            IsBuilding = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private CompilerOutputRow MakeRow(CompilerOutputLine line)
    {
        var now = DateTimeOffset.Now;
        var delta = now - _prevRowTime;
        _prevRowTime = now;
        return new CompilerOutputRow(line.Text, line.Severity.ToString(), line.Span,
            OutputRowKind.Normal, now, delta, now - _buildStart, TimeColumnMode);
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

    /// <summary>Opens the configured output types after a successful build.</summary>
    private void AutoOpenOutputs(ImmutableArray<OutputArtifact> artifacts)
    {
        if (_settings is null) return;
        var s = _settings.Current;
        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (s.OpenLoxAfterBuild) kinds.Add("lox");
        if (s.Open3dAfterBuild) kinds.Add("3d");
        if (s.OpenPdfAfterBuild) kinds.Add("pdf");
        if (kinds.Count == 0) return;

        var matches = artifacts.Where(a => kinds.Contains(a.Kind)).Select(a => a.Path).ToList();
        foreach (var path in s.OpenAllOutputsAfterBuild ? matches : matches.Take(1))
            _shell.Open(path);
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
        var lox = Artifacts.FirstOrDefault(a => a.Kind.Equals("lox", StringComparison.OrdinalIgnoreCase));
        if (lox is null) return;
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
        var d3 = Artifacts.FirstOrDefault(a => a.Kind.Equals("3d", StringComparison.OrdinalIgnoreCase));
        if (d3 is null) return;
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

    [RelayCommand]
    private void OpenArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        _shell.Open(row.Path);
    }

    [RelayCommand]
    private void RevealArtifact(ArtifactRow? row)
    {
        if (row is null) return;
        _shell.RevealInFileManager(row.Path);
    }

    private void UpdateArtifacts(ImmutableArray<OutputArtifact> artifacts)
    {
        var rows = artifacts
            .Select(a => new ArtifactRow(a.Path, a.Kind, a.SizeBytes, a.LastWriteUtc))
            .OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();
        Artifacts = rows;
        HasLoxArtifact  = rows.Any(a => a.Kind.Equals("lox", StringComparison.OrdinalIgnoreCase));
        HasAvenArtifact = rows.Any(a => a.Kind.Equals("3d",  StringComparison.OrdinalIgnoreCase));
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
