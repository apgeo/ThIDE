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
using Therion.Workspace;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public enum OutputRowKind { Command, Normal, Summary }

/// <summary>Coarse Therion build stage parsed from its output. Monotonic: only advances.</summary>
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
    /// <summary>The resolved (absolute, when possible) path this row links to, or empty (#1).</summary>
    public string LinkPath => Span is { } s ? s.FilePath : string.Empty;
    /// <summary>True when the linked file is viewable in an in-app viewer (3D model or PDF map), so the
    /// "Open in application viewer" context-menu item is enabled (#1).</summary>
    public bool CanOpenInAppViewer =>
        System.IO.Path.GetExtension(LinkPath).ToLowerInvariant() is ".lox" or ".3d" or ".pdf";

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
    /// <summary>True when a project input is newer than this output.</summary>
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
    public string StateText => ThIDE.Resources.Tr.Get(IsStale ? "Gen_State_Stale" : "Gen_State_Current");
    /// <summary>The kind in the UI language (Therion.Build names it in English); the English kind when there is no key.</summary>
    public string KindText =>
        System.IO.Path.GetExtension(Path).TrimStart('.').ToLowerInvariant() is { Length: > 0 } ext
        && ThIDE.Resources.Tr.Find("Gen_Kind_" + ext) is { Length: > 0 } text ? text : Kind;
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

/// <summary>A runnable export target parsed from the active thconfig.</summary>
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

    // ---- build phases: a stage label + stepped progress, alongside the spinner ----
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
    /// <summary>True when at least one output is older than a project input.</summary>
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
    /// <summary>What the last build compiled: the thconfig file name, or a derived build's label.</summary>
    [ObservableProperty] private string _lastBuildTarget = string.Empty;

    /// <summary>
    /// The error text from the last failed build (the error line(s) Therion reported), kept as state
    /// until the next compilation. Empty after a successful build or before any build. Surfaced in the
    /// toolbar status tooltip, the activity log, and the project dashboard.
    /// </summary>
    [ObservableProperty] private string _lastError = string.Empty;
    /// <summary>True when <see cref="LastError"/> holds a non-empty error to show.</summary>
    public bool HasLastError => _lastError.Length > 0;
    partial void OnLastErrorChanged(string value) => OnPropertyChanged(nameof(HasLastError));

    // Error lines collected during the running build (reset at each build start), used to compose
    // LastError. Mirrors CompilerOutputRow.IsError, so "average loop error" and warnings are excluded.
    private readonly List<string> _buildErrorLines = new();

    /// <summary>Raised when a compiler-output row with a span is activated.</summary>
    public event System.EventHandler<Therion.Core.SourceSpan>? NavigateRequested;

    /// <summary>
    /// Extensions we open in the editor (Therion source + logs). Everything else detected as a link in
    /// the compiler output is a generated artifact: it opens in a viewer or the OS default app, never
    /// as binary text in the editor (#1).
    /// </summary>
    private static readonly HashSet<string> EditableSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".th", ".th2", ".thc", ".thconfig", ".xvi", ".log", ".tlx" };

    /// <summary>
    /// Single-click action for a file link in the compiler output (#1). Source files (and the
    /// configuration file) open in the editor at the reported line; generated artifacts open in the
    /// appropriate viewer or the OS default app per the viewer settings — not as binary text.
    /// </summary>
    [RelayCommand]
    private void NavigateOutput(CompilerOutputRow? row)
    {
        if (row?.Span is not { } span || string.IsNullOrEmpty(span.FilePath)) return;
        if (EditableSourceExtensions.Contains(Path.GetExtension(span.FilePath)))
        {
            OpenInEditor(span);
            return;
        }
        OpenArtifactByPath(span.FilePath, forceInternal: null);
    }

    /// <summary>Opens a source-file span in the editor, jumping to its reported line (#1).</summary>
    private void OpenInEditor(SourceSpan span)
    {
        // Output-link spans carry a file + line but a zero length (so IsEmpty is true). NavigateToSpanAsync
        // ignores zero-length spans, so give the caret stop a minimal extent — exactly as the back/forward
        // history does — else the jump is silently dropped (#3/#4).
        if (span.IsEmpty) span = span with { Length = 1 };
        NavigateRequested?.Invoke(this, span);
    }

    /// <summary>
    /// Opens a generated artifact by path (#1). <c>.lox</c>/<c>.3d</c> open in the embedded 3D viewer
    /// when that's enabled (else the external Loch/Aven tool, else the OS default app); <c>.pdf</c> opens
    /// in the in-app map viewer when that option is on (else the OS default); anything else opens with the
    /// OS default app. <paramref name="forceInternal"/> overrides the setting for the context-menu actions:
    /// <c>true</c> forces the in-app viewer (when the type supports one), <c>false</c> forces external/OS.
    /// </summary>
    private void OpenArtifactByPath(string path, bool? forceInternal)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path)) { Status = $"File not found: {path}"; return; }

        var ext = Path.GetExtension(path).ToLowerInvariant();

        // 3D models → embedded viewer / external tool / OS default.
        if (ext is ".lox" or ".3d")
        {
            bool useInternal = forceInternal
                ?? (_settings?.Current is { Open3dInInternalViewer: true, EnableModel3DViewer: true });
            if (useInternal) { View3DRequested?.Invoke(this, path); return; }
            _ = OpenInExternalViewerByPathAsync(path);
            return;
        }

        // PDF → in-app map viewer / OS default.
        if (ext == ".pdf")
        {
            bool useInternal = forceInternal
                ?? (_settings?.Current is { OpenPdfInInternalViewer: true, EnableInAppViewer: true });
            if (useInternal) { ViewMapRequested?.Invoke(this, path); return; }
            _shell.Open(path);
            return;
        }

        // Everything else → OS default application.
        _shell.Open(path);
    }

    /// <summary>Context-menu "Open in application viewer": force the embedded 3D / map viewer (#1).</summary>
    [RelayCommand]
    private void OpenOutputInApp(CompilerOutputRow? row)
    {
        if (LinkPathOf(row) is { } path) OpenArtifactByPath(path, forceInternal: true);
    }

    /// <summary>Context-menu "Open in external viewer": force the external tool / OS default app (#1).</summary>
    [RelayCommand]
    private void OpenOutputExternally(CompilerOutputRow? row)
    {
        if (LinkPathOf(row) is { } path)
        {
            // Source files have no external "viewer" — just open them in the OS default app.
            if (EditableSourceExtensions.Contains(Path.GetExtension(path)) && File.Exists(path))
                _shell.Open(path);
            else
                OpenArtifactByPath(path, forceInternal: false);
        }
    }

    /// <summary>Context-menu "Reveal in File Manager" for a compiler-output link (#1).</summary>
    [RelayCommand]
    private void RevealOutput(CompilerOutputRow? row)
    {
        if (LinkPathOf(row) is { } path) _shell.RevealInFileManager(path);
    }

    private static string? LinkPathOf(CompilerOutputRow? row) =>
        row?.Span is { } s && !string.IsNullOrEmpty(s.FilePath) ? s.FilePath : null;

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
        _documents.DocumentChanged += (_, _) => { ProjectFormat.OnUi(OnBuildTargetMayHaveChanged); RefreshExportTargets(); };
        // Marshal to the UI thread: Changed is raised on a background thread (the session rebuilds
        // the graph under Task.Run/ConfigureAwait(false)) and RefreshExportTargets writes UI-bound
        // state — running it off-thread throws "the calling thread cannot access this object".
        if (_session is not null)
            _session.Changed += (_, _) => ProjectFormat.OnUi(() =>
            {
                RefreshExportTargets();
                OnBuildTargetMayHaveChanged();
                RefreshArtifactStaleness();   // the graph is (re)loaded: the stale check can see every input now
            });
        RefreshExportTargets();
        OnBuildTargetMayHaveChanged();
    }

    private readonly ILogService? _log;
    private readonly INotificationService? _notifications;   // (tool-not-found toast)

    public BuildViewModel() : this(
        new NullCompiler(), new CompileGate(), new ShellOpener(),
        new NullLocator(), new NullDocumentService(), new NullArtifactCache())
    { } // Designer-only.

    public event EventHandler<ImmutableArray<Diagnostic>>? CompileCompleted;

    /// <summary>Raised when a build begins so the shell can surface the Compiler Output panel (#2).</summary>
    /// <summary>True while a build saves the files it involves, before it starts compiling.</summary>
    public bool IsPreparing { get; private set; }

    /// <summary>A Therion build is starting; the argument is the entry being compiled.</summary>
    public event EventHandler<string>? BuildStarted;

    /// <summary>An external tool run (dump3d, extend, --version) is starting.</summary>
    public event EventHandler? ExternalToolStarted;

    /// <summary>A build ended — succeeded, failed, was cancelled or threw. Raised on the UI thread.</summary>
    public event EventHandler<Therion.Build.Automation.BuildOutcome>? BuildFinished;

    public bool CanBuild => !IsBuilding && ResolveBuildEntry() is not null;

    /// <summary>
    /// The build target: always the workspace's active thconfig, whichever tab has focus. Only when
    /// no thconfig is active (a lone file opened without a project) does it fall back to the active file.
    /// A focused thconfig other than the active one is built by <see cref="BuildThisThconfigCommand"/>.
    /// </summary>
    public string? ResolveBuildEntry() => _session?.ActiveThconfig?.FullPath ?? _documents.CurrentPath;

    /// <summary>File name of what Compile builds (tooltip, status), or empty when there is nothing to build.</summary>
    public string BuildTargetName => ResolveBuildEntry() is { } entry ? Path.GetFileName(entry) : string.Empty;

    /// <summary>The focused editor file when it is a thconfig other than the build target, else null.</summary>
    public string? FocusedOtherThconfig =>
        _documents.CurrentPath is { } current && IsThconfig(current)
        && !(ResolveBuildEntry() is { } target && SamePath(current, target))
            ? current : null;

    /// <summary>"Build this thconfig": builds the focused thconfig without making it the active one.</summary>
    [RelayCommand(CanExecute = nameof(CanBuildThisThconfig))]
    private async Task BuildThisThconfigAsync()
    {
        if (FocusedOtherThconfig is not { } cfg) return;
        await RunBuildAsync(cfg).ConfigureAwait(true);
    }

    private bool CanBuildThisThconfig() => !IsBuilding && FocusedOtherThconfig is not null;

    partial void OnIsBuildingChanged(bool value) => BuildThisThconfigCommand.NotifyCanExecuteChanged();

    // The target last seen by OnBuildTargetMayHaveChanged; Generated Files follows it when it changes.
    private string? _syncedTarget;

    /// <summary>Re-reads the build target after a focus or active-thconfig change.</summary>
    private void OnBuildTargetMayHaveChanged()
    {
        OnPropertyChanged(nameof(BuildTargetName));
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(FocusedOtherThconfig));
        BuildThisThconfigCommand.NotifyCanExecuteChanged();

        // Generated Files lists the outputs of the build target. It switches only when the target
        // itself changes, so focusing another tab never swaps in the outputs of a different thconfig,
        // and the outputs of a "Build this thconfig" run stay listed until the active one changes.
        var target = ResolveBuildEntry();
        if (target is null || IsBuilding || (_syncedTarget is not null && SamePath(_syncedTarget, target))) return;
        _syncedTarget = target;
        ShowOutputsOf(target);
    }

    private static bool SamePath(string a, string b) => Therion.Workspace.Editing.FilePaths.Same(a, b);

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
        if (entry is null) { Status = ThIDE.Resources.Tr.Get("Build_NoProject"); return; }
        await RunBuildAsync(entry).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs Therion on <paramref name="entry"/> (which may be a generated temporary thconfig for
    /// ). <paramref name="tempThconfig"/>, when set, is deleted after the build.
    /// </summary>
    /// <returns>How the build ended, or null when it did not start (another build runs, or the
    /// user cancelled saving the involved files).</returns>
    public async Task<Therion.Build.Automation.BuildOutcome?> RunBuildAsync(string entry, string? tempThconfig = null, string? label = null)
    {
        using var lease = _gate.TryAcquire();
        if (lease is null) { Status = ThIDE.Resources.Tr.Get("Build_InProgress"); return null; }

        // Before compiling, make sure the involved unsaved files are on disk (Therion reads from
        // disk). Auto-save when configured, otherwise ask (naming the files). Cancel aborts.
        IsPreparing = true;
        try { if (!await EnsureInvolvedFilesSavedAsync(entry).ConfigureAwait(true)) return null; }
        finally { IsPreparing = false; }
        // What the build ends as; updated as it goes and reported in finally.
        var outcome = new Therion.Build.Automation.BuildOutcome(entry, false, false, -1, 0, 0, [], null, TimeSpan.Zero, label);

        // What is being built, by name: the thconfig, or the label of a derived (temporary) build.
        var target = tempThconfig is not null && label is not null ? label : Path.GetFileName(entry);
        IsBuilding = true;
        HasBuildResult = false;          // clear the previous success/error status-bar icon
        LastBuildTarget = target;
        // clears the previous "succeeded/failed" label (#3)
        Status = string.Format(ThIDE.Resources.Tr.Get("Build_CompilingTarget"), target);
        StatusArtifactCount = string.Empty;
        LastError = string.Empty;        // the last error is state only until the next compilation
        _buildErrorLines.Clear();
        HasLog = false; LogFilePath = null;           // clear the previous build's log (#3)
        SetPhase(BuildPhase.Starting);
        _log?.Info($"Build started: {label ?? entry}");
        BuildStarted?.Invoke(this, entry);  // surface the Compiler Output panel (#2)
        ClearOutputState();
        _cts = new CancellationTokenSource();

        // First row: the full command with its arguments.
        var tool = await _locator.FindAsync(ExternalToolLocator.Therion, _cts.Token).ConfigureAwait(true);
        var command = tool is null ? $"therion \"{entry}\"" : $"\"{tool.Path}\" \"{entry}\"";
        _buildStart = DateTimeOffset.Now;
        _prevRowTime = _buildStart;
        _buildWorkDir = Path.GetDirectoryName(Path.GetFullPath(entry)); // resolve output paths (#1)
        AddRow(TargetRow(entry, tempThconfig is null ? null : target));
        AddRow(new CompilerOutputRow(command, "Command", null, OutputRowKind.Command,
            _buildStart, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode));

        EnsureExportOutputDirectories(entry);   // create any missing output folders (opt-out in Preferences)

        var sw = Stopwatch.StartNew();
        var progress = new Progress<CompilerOutputLine>(line => { AddRow(MakeRow(line)); UpdatePhase(line.Text); });

        try
        {
            var result = await _compiler.CompileAsync(entry, progress, _cts.Token).ConfigureAwait(true);
            sw.Stop();
            bool ok = result.ExitCode == 0;
            RecordOutputs(entry, tempThconfig is null ? null : target, result.Artifacts, ok);
            DetectLog(entry, _buildStart, result.LogPath);
            var warnCount = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            HasBuildResult = true;
            LastBuildSucceeded = ok;
            LastBuildHasWarnings = warnCount > 0;
            LastBuildWarningCount = warnCount;
            AddRow(SummaryRow(ok, warnCount, sw.Elapsed, result.Artifacts.Length, _buildStart));
            // #3: on success the "N artifact(s)" count is shown as a separate clickable link.
            if (ok)
            {
                LastError = string.Empty;
                Status = ThIDE.Resources.Tr.Get(result.Artifacts.Length > 0 ? "Build_Succeeded" : "Build_SucceededNoOutputs");
                StatusArtifactCount = result.Artifacts.Length > 0
                    ? string.Format(ThIDE.Resources.Tr.Get("Build_ArtifactCount"), result.Artifacts.Length)
                    : string.Empty;
            }
            else
            {
                // Keep the last few error lines (the last one is usually the real error); the toolbar
                // label shows the first line, the tooltip/log/dashboard show the full text.
                LastError = _buildErrorLines.Count == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, _buildErrorLines.TakeLast(6));
                var shortErr = FirstLine(LastError);
                Status = shortErr.Length > 0
                    ? string.Format(ThIDE.Resources.Tr.Get("Build_FailedWithError"), result.ExitCode, shortErr)
                    : string.Format(ThIDE.Resources.Tr.Get("Build_Failed"), result.ExitCode);
                StatusArtifactCount = string.Empty;
            }
            _log?.Log(
                ok ? (warnCount > 0 ? LogVerbosity.Warning : LogVerbosity.Info) : LogVerbosity.Error,
                ok
                    ? $"Build succeeded: {result.Artifacts.Length} artifact(s)" + (warnCount > 0 ? $", {warnCount} warning(s)" : "")
                    : $"Build failed (exit {result.ExitCode}, {warnCount} warning(s))."
                      + (LastError.Length > 0 ? $" Error: {LastError.Replace(Environment.NewLine, " ⏎ ")}" : ""));
            outcome = outcome with
            {
                Succeeded = ok, ExitCode = result.ExitCode,
                Errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
                Warnings = warnCount,
                Artifacts = result.Artifacts.Select(a => a.Path).ToList(),
                LogPath = LogFilePath, Elapsed = sw.Elapsed,
            };
            CompileCompleted?.Invoke(this, result.Diagnostics);
            if (ok) AutoOpenOutputs(result.Artifacts);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Status = ThIDE.Resources.Tr.Get("Build_Cancelled");
            outcome = outcome with { Cancelled = true, Elapsed = sw.Elapsed };
            _log?.Warning("Build cancelled.");
            AddRow(new CompilerOutputRow(ThIDE.Resources.Tr.Get("Build_Cancelled"), "Error", null, OutputRowKind.Summary,
                DateTimeOffset.Now, TimeSpan.Zero, sw.Elapsed, TimeColumnMode));
        }
        catch (Exception ex)
        {
            Status = string.Format(ThIDE.Resources.Tr.Get("Build_Error"), ex.Message);
            outcome = outcome with { Elapsed = sw.Elapsed };
            _log?.Error("Build error: " + ex.Message);
        }
        finally
        {
            BuildFinished?.Invoke(this, outcome);
            IsBuilding = false;
            OnBuildTargetMayHaveChanged();   // the active thconfig may have changed while it built
            _cts?.Dispose();
            _cts = null;
            SetPhase(BuildPhase.Idle); // clear the phase indicator
            if (!string.IsNullOrEmpty(tempThconfig))
                try { File.Delete(tempThconfig); } catch { /* best-effort temp cleanup */ }
        }
        return outcome;
    }

    /// <summary>
    /// Ensures the unsaved files that take part in this build are on disk before Therion runs (it
    /// reads from disk, not the editor buffers). Auto-saves when <c>AutoSaveBeforeCompile</c> is on;
    /// otherwise prompts (naming the files) with Save / Compile without saving / Cancel. Returns
    /// <c>false</c> only when the user cancels — the build should then not run.
    /// </summary>
    public async Task<bool> EnsureInvolvedFilesSavedAsync(string entry)
    {
        string entryFull; try { entryFull = Path.GetFullPath(entry); } catch { entryFull = entry; }
        var dirty = _documents.Documents
            .Where(d => d.IsDirty && !string.IsNullOrEmpty(d.FilePath) && File.Exists(d.FilePath))
            .Where(d => (_session?.Covers(d.FilePath) ?? false)
                        || string.Equals(SafeFull(d.FilePath), entryFull, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dirty.Count == 0) return true;

        if (_settings?.Current.AutoSaveBeforeCompile == true)
        {
            foreach (var d in dirty) await _documents.SaveDocumentAsync(d).ConfigureAwait(true);
            _log?.Info($"Auto-saved {dirty.Count} unsaved file(s) before compiling.");
            return true;
        }

        var list = string.Join(Environment.NewLine, dirty.Select(d => "  • " + Path.GetFileName(d.FilePath)));
        var choice = await Views.SaveBeforeBuildDialog.ShowOverMainAsync(
            ThIDE.Resources.Tr.Get("Build_SavePromptTitle"),
            string.Format(ThIDE.Resources.Tr.Get("Build_SavePromptMsg"), dirty.Count) + Environment.NewLine + Environment.NewLine + list,
            ThIDE.Resources.Tr.Get("Build_SaveAndCompile"),
            ThIDE.Resources.Tr.Get("Build_CompileWithoutSaving"),
            ThIDE.Resources.Tr.Get("Common_Cancel"),
            noWindowResult: Views.SaveBeforeBuildChoice.DontSave).ConfigureAwait(true);

        switch (choice)
        {
            case Views.SaveBeforeBuildChoice.Save:
                foreach (var d in dirty) await _documents.SaveDocumentAsync(d).ConfigureAwait(true);
                _log?.Info($"Saved {dirty.Count} file(s) before compiling.");
                return true;
            case Views.SaveBeforeBuildChoice.DontSave:
                return true;
            default:
                Status = ThIDE.Resources.Tr.Get("Build_CancelledUnsaved");
                return false;
        }
    }

    private static string SafeFull(string p) { try { return Path.GetFullPath(p); } catch { return p; } }

    /// <summary>The first non-empty line of <paramref name="text"/>, trimmed and capped for a one-line label.</summary>
    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            return line.Length > 160 ? line[..159] + "…" : line;
        }
        return string.Empty;
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
        var key = ok
            ? (warnCount > 0 ? "Build_SummaryOkWarn" : "Build_SummaryOk") + (artifactCount > 0 ? "Files" : "")
            : warnCount > 0 ? "Build_SummaryFailedWarn" : "Build_SummaryFailed";
        var culture = ThIDE.Resources.Tr.Culture;
        var text = string.Format(culture, ThIDE.Resources.Tr.Get(key),
            elapsed.TotalSeconds.ToString("0.00", culture), artifactCount, warnCount);
        var now = DateTimeOffset.Now;
        return new CompilerOutputRow(text, ok ? "Info" : "Error", null, OutputRowKind.Summary,
            now, TimeSpan.Zero, now - buildStart, TimeColumnMode, success: ok);
    }

    /// <summary>
    /// First output row: what this build compiles. A real thconfig is named by its full path, as a link
    /// that opens it; a derived build by its <paramref name="derivedLabel"/>.
    /// </summary>
    private CompilerOutputRow TargetRow(string entry, string? derivedLabel)
    {
        string full; try { full = Path.GetFullPath(entry); } catch { full = entry; }
        var shown = derivedLabel ?? full;
        var text = string.Format(ThIDE.Resources.Tr.Get("Build_OutputTarget"), shown);
        SourceSpan? link = derivedLabel is null
            ? new SourceSpan(full, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0)
            : null;
        return new CompilerOutputRow(text, "Command", link, OutputRowKind.Command,
            _buildStart, TimeSpan.Zero, TimeSpan.Zero, TimeColumnMode, linkText: derivedLabel is null ? full : null);
    }

    private void AddRow(CompilerOutputRow row)
    {
        _outputBuffer.Add(row);
        Output = _outputBuffer.ToArray();   // immutable swap for binding
        _rawBuffer.AppendLine(row.Text);
        RawOutput = _rawBuffer.ToString();
        // Capture the real error lines (same rule the red highlight uses) to compose LastError.
        if (row.Kind == OutputRowKind.Normal && row.IsError && !string.IsNullOrWhiteSpace(row.Text))
            _buildErrorLines.Add(row.Text.Trim());
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
        _settings.Update(st => st with { AutoOpenOverrides = dict });
    }

    /// <summary>#1: open a specific .lox in Loch / .3d in Aven (falls back to the OS default app).</summary>
    [RelayCommand]
    private async Task OpenInExternalViewer(ArtifactRow? row)
    {
        if (row is null) return;
        WarnIfStale(row);
        await OpenInExternalViewerByPathAsync(row.Path).ConfigureAwait(true);
    }

    /// <summary>Opens a .lox in Loch / a .3d in Aven, falling back to the OS default app (#1).</summary>
    private async Task OpenInExternalViewerByPathAsync(string path)
    {
        string toolId = HasExt(path, ".lox") ? ExternalToolLocator.Loch
            : HasExt(path, ".3d") ? ExternalToolLocator.Aven : string.Empty;
        if (toolId.Length > 0 && await _locator.FindAsync(toolId).ConfigureAwait(true) is { } tool)
        {
            try
            {
                Process.Start(new ProcessStartInfo(tool.Path, $"\"{path}\"") { UseShellExecute = false });
                return;
            }
            catch { /* fall through to shell-open */ }
        }
        _shell.Open(path);
    }

    /// <summary>Finds a Therion log (.log/.tlx) written during this build, for the open-log button (#3).</summary>
    private void DetectLog(string entry, DateTimeOffset buildStart, string? reportedLog = null)
    {
        // The compiler knows the log it wrote when it asked for one; otherwise take the newest.
        if (reportedLog is not null && File.Exists(reportedLog)) { LogFilePath = reportedLog; HasLog = true; return; }
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

    // ---- : export targets parsed from the active thconfig ----

    /// <summary>The active thconfig's export commands, each runnable on its own.</summary>
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

    /// <summary>builds a derived thconfig that keeps only <paramref name="info"/>'s export.</summary>
    private async Task BuildSingleExportAsync(string cfg, ExportTargetInfo info)
    {
        try
        {
            // Save first: the copy is made from disk, so it must see the editor's unsaved edits.
            if (!await EnsureInvolvedFilesSavedAsync(cfg).ConfigureAwait(true)) return;
            var temp = Path.Combine(Path.GetDirectoryName(cfg)!, ".thide-target.thconfig");
            DerivedThconfig.Write(cfg, temp, text => ThconfigExportEditor.IsolateExport(text, info));
            await RunBuildAsync(temp, temp, $"{Path.GetFileName(cfg)} :: {info.Title}").ConfigureAwait(true);
        }
        catch (Exception ex) { Status = ex.Message; _log?.Error("Target build error: " + ex.Message); }
    }

    /// <summary>runs a composed quick-export (writes a temp thconfig, builds, opens the result).</summary>
    public async Task RunQuickExportAsync(string exportBlock, string outputFileName)
    {
        var cfg = _session?.ActiveThconfig?.FullPath ?? ResolveBuildEntry();
        if (cfg is null || !File.Exists(cfg)) { Status = "No project to export."; return; }
        try
        {
            var dir = Path.GetDirectoryName(cfg)!;
            if (!await EnsureInvolvedFilesSavedAsync(cfg).ConfigureAwait(true)) return;
            var temp = Path.Combine(dir, ".thide-export.thconfig");
            DerivedThconfig.Write(cfg, temp, text => ThconfigExportEditor.ComposeExport(text, exportBlock));
            await RunBuildAsync(temp, temp, $"Quick export → {outputFileName}").ConfigureAwait(true);
            var outPath = Path.Combine(dir, outputFileName);
            if (LastBuildSucceeded && File.Exists(outPath)) _shell.Open(outPath);
        }
        catch (Exception ex) { Status = ex.Message; _log?.Error("Quick export error: " + ex.Message); }
    }

    /// <summary>Raised when the user asks for the Quick Export dialog; the shell shows it.</summary>
    public event EventHandler? QuickExportRequested;
    [RelayCommand] private void ShowQuickExport() => QuickExportRequested?.Invoke(this, EventArgs.Empty);

    // ---- : external round-trips (survex tools on .3d + therion --print-*) ----

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
    /// panel. Returns true on exit code 0. Used for the round-trips (not the main compile).
    /// </summary>
    private async Task<bool> RunExternalAsync(string toolId, string args, string label, string? workDir)
    {
        if (IsBuilding) { Status = ThIDE.Resources.Tr.Get("Build_InProgress"); return false; }
        var tool = await _locator.FindAsync(toolId).ConfigureAwait(true);
        if (tool is null)
        {
            Status = $"{toolId} not found.";
            _log?.Warning($"{toolId} not found on PATH or in External Tools settings.");
            _notifications?.Warning(ThIDE.Resources.Tr.Get("Notif_ToolNotFoundTitle"),
                string.Format(ThIDE.Resources.Tr.Get("Notif_ToolNotFoundMsg"), toolId));
            return false;
        }

        ExternalToolStarted?.Invoke(this, EventArgs.Empty); // surface the output panel (#2)
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

    /// <summary>Raised to view an artifact in the in-app map viewer instead of externally.</summary>
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

    /// <summary>Raised when the user asks to view an artifact in the embedded 3D viewer.</summary>
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

    // ---- Generated Files: the outputs of one thconfig ------------------------------------------

    // Artifact-cache version key (the cache is keyed by entry + Therion version; the version is not probed).
    private const string CacheVersion = "unknown";

    /// <summary>Full path of the thconfig whose outputs are listed; null for a derived build or none.</summary>
    private string? _artifactsEntry;
    /// <summary>File name (or derived-build label) shown in the Generated Files header.</summary>
    private string _artifactsName = string.Empty;
    private OutputListState _artifactsState = OutputListState.Unknown;
    private ImmutableArray<OutputArtifact> _artifactsRaw = ImmutableArray<OutputArtifact>.Empty;

    /// <summary>"Outputs of thconfig_x · built 20:21"; empty before any thconfig is known.</summary>
    [ObservableProperty] private string _artifactsHeader = string.Empty;
    /// <summary>Shown in place of the empty grid: "not built yet" or "the last build wrote no outputs".</summary>
    [ObservableProperty] private string _artifactsEmptyText = string.Empty;
    [ObservableProperty] private bool _hasArtifactsEmptyText;
    /// <summary>True when the empty state offers Compile (never built, and Compile builds this thconfig).</summary>
    [ObservableProperty] private bool _canCompileFromEmptyState;
    /// <summary>Why the outputs are stale: a later build failed, or an input changed since.</summary>
    [ObservableProperty] private string _staleBannerText = string.Empty;

    /// <summary>Lists the saved outputs of <paramref name="entry"/> (restored across restarts, stale flag included).</summary>
    private void ShowOutputsOf(string entry)
    {
        var cached = _artifactCache.Load(entry, CacheVersion);
        var state = _artifactCache.LoadState(entry, CacheVersion);
        SetArtifacts(entry, Path.GetFileName(entry), ExistingOnly(cached), state);
    }

    /// <summary>
    /// Saves and lists what a build of <paramref name="entry"/> produced. A failed build keeps the
    /// earlier outputs listed (they are still on disk) and marks them stale, persistently, so a
    /// restart does not relabel them "current". A derived build (<paramref name="derivedLabel"/>)
    /// is listed under its label and not saved, its temporary thconfig being gone after the build.
    /// </summary>
    private void RecordOutputs(string entry, string? derivedLabel, ImmutableArray<OutputArtifact> produced, bool ok)
    {
        if (derivedLabel is not null)
        {
            SetArtifacts(null, derivedLabel, produced, new OutputListState(DateTimeOffset.UtcNow, !ok));
            return;
        }
        ImmutableArray<OutputArtifact> list;
        OutputListState state;
        if (ok)
        {
            list = produced;
            state = new OutputListState(DateTimeOffset.UtcNow, false);
        }
        else
        {
            var earlier = ExistingOnly(_artifactCache.Load(entry, CacheVersion));
            var producedPaths = produced.Select(a => a.Path).ToHashSet(StringComparer.Ordinal);
            list = earlier.Where(a => !producedPaths.Contains(a.Path)).Concat(produced).ToImmutableArray();
            state = new OutputListState(_artifactCache.LoadState(entry, CacheVersion).BuiltUtc, Stale: list.Length > 0);
        }
        _artifactCache.Save(entry, CacheVersion, list, state);
        SetArtifacts(entry, Path.GetFileName(entry), list, state);
    }

    private static ImmutableArray<OutputArtifact> ExistingOnly(ImmutableArray<OutputArtifact> list) =>
        list.IsDefaultOrEmpty ? ImmutableArray<OutputArtifact>.Empty
            : list.Where(a => { try { return File.Exists(a.Path); } catch { return false; } }).ToImmutableArray();

    private void SetArtifacts(string? entry, string name, ImmutableArray<OutputArtifact> list, OutputListState state)
    {
        _artifactsEntry = entry;
        _artifactsName = name;
        _artifactsState = state;
        _artifactsRaw = list;
        UpdateArtifacts(list);

        var built = state.BuiltUtc ?? (list.IsDefaultOrEmpty ? null : list.Max(a => a.LastWriteUtc));
        ArtifactsHeader = built is { } b
            ? string.Format(ThIDE.Resources.Tr.Get("Gen_HeaderBuilt"), name, FormatBuiltTime(b))
            : string.Format(ThIDE.Resources.Tr.Get("Gen_Header"), name);
        bool neverBuilt = built is null;
        ArtifactsEmptyText = !list.IsDefaultOrEmpty ? string.Empty
            : string.Format(ThIDE.Resources.Tr.Get(neverBuilt ? "Gen_NotBuiltYet" : "Gen_NoOutputs"), name);
        HasArtifactsEmptyText = ArtifactsEmptyText.Length > 0;
        CanCompileFromEmptyState = HasArtifactsEmptyText && neverBuilt
                                   && entry is not null && ResolveBuildEntry() is { } t && SamePath(t, entry);
    }

    /// <summary>"20:21" for today, else "2026-11-03 20:21" (local time).</summary>
    private static string FormatBuiltTime(DateTimeOffset builtUtc)
    {
        var local = builtUtc.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Recomputes the per-row stale flags (an input may be newer now, or the graph just loaded).</summary>
    private void RefreshArtifactStaleness()
    {
        if (IsBuilding || _artifactsRaw.IsDefaultOrEmpty) return;
        UpdateArtifacts(_artifactsRaw);
    }

    private void UpdateArtifacts(ImmutableArray<OutputArtifact> artifacts)
    {
        // an output is "stale" if a later build of its thconfig failed, or if any project input is
        // newer than it.
        bool failed = _artifactsState.Stale;
        var newest = NewestInputUtc(_artifactsEntry);
        var overrides = _settings?.Current.AutoOpenOverrides;
        var rows = artifacts
            .Select(a => new ArtifactRow(a.Path, a.Kind, a.SizeBytes, a.LastWriteUtc)
            {
                IsStale = failed || (newest is { } n && a.LastWriteUtc < n),
                AutoOpen = LookupAutoOpen(overrides, a.Path),   // #7: restore the persisted override
            })
            .OrderBy(a => a.Kind, StringComparer.Ordinal)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();
        Artifacts = rows;
        HasLoxArtifact  = rows.Any(a => HasExt(a.Path, ".lox"));
        HasAvenArtifact = rows.Any(a => HasExt(a.Path, ".3d"));
        HasStaleArtifacts = rows.Any(a => a.IsStale);
        StaleBannerText = failed
            ? string.Format(ThIDE.Resources.Tr.Get("Gen_StaleAfterFailedBuild"), _artifactsName)
            : ThIDE.Resources.Tr.Get("Gen_StaleBanner");
    }

    /// <summary>
    /// Newest last-write time across the inputs of <paramref name="entry"/> (stale check). The loaded
    /// graph is the active thconfig's, so it only counts when the listed outputs are that thconfig's.
    /// </summary>
    private DateTimeOffset? NewestInputUtc(string? entry)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = _session?.ActiveThconfig?.FullPath;
        if (entry is not null) files.Add(entry);
        if (_session?.Model is { } model && entry is not null && active is not null && SamePath(entry, active))
        {
            foreach (var k in model.PerFile.Keys) files.Add(k);
            foreach (var (from, to, _) in model.FileGraphEdges) { files.Add(from); files.Add(to); }
        }

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

    /// <summary>surface a warning before opening an out-of-date output.</summary>
    private void WarnIfStale(ArtifactRow row)
    {
        if (!row.IsStale) return;
        Status = $"Opening a stale output ({Path.GetFileName(row.Path)}) — rebuild to refresh.";
        _log?.Warning($"Opening a stale output '{Path.GetFileName(row.Path)}': a project input changed since the last build. Rebuild to refresh.");
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
