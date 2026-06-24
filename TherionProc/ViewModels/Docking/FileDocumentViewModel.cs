// One open .th/.th2/.thconfig file as a Dock document (multi-file MDI).
// Carries its own parsed model + Measurements grid, so each document tab is a
// fully independent view of its file and can be floated/docked on its own.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;

namespace TherionProc.ViewModels.Docking;

public sealed partial class FileDocumentViewModel : Document, IDockContent, IDisposable
{
    private readonly ICommandRegistry? _commands;
    private readonly IAppSettingsService? _settings;
    private bool _disposed;

    private string _documentText = string.Empty;
    private TherionFile? _ast;
    private SemanticModel? _semantics;
    private ISymbolNavigationService? _navigation;
    private WorkspaceSemanticModel? _workspace;
    private ImmutableArray<Diagnostic> _diagnostics = ImmutableArray<Diagnostic>.Empty;

    /// <summary>Raised when something wants the editor to scroll to a span (e.g. diagnostics).</summary>
    public event EventHandler<SourceSpan>? ScrollToSpanRequested;

    /// <summary>Raised after a re-parse so document-tracking tools can refresh.</summary>
    public event EventHandler? Reparsed;

    public string FilePath { get; }

    public MeasurementsViewModel Measurements { get; }

    /// <summary>True only for .th files — the Measurements tab is hidden for any other type (#4).</summary>
    public bool IsThFile =>
        string.Equals(System.IO.Path.GetExtension(FilePath), ".th", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for .th2 sketch files — gates the "Edit with Mapiah" button.</summary>
    public bool IsTh2File =>
        string.Equals(System.IO.Path.GetExtension(FilePath), ".th2", StringComparison.OrdinalIgnoreCase);

    public FileDocumentViewModel(string filePath, string text, MeasurementsViewModel measurements,
        ICommandRegistry? commands = null, IAppSettingsService? settings = null)
    {
        FilePath = filePath;
        _commands = commands;
        _settings = settings;
        Measurements = measurements;

        Id = filePath;
        Title = System.IO.Path.GetFileName(filePath);
        CanFloat = true;
        CanClose = true;
        CanPin = true;
        TabBrush = TabBrushForExtension(System.IO.Path.GetExtension(filePath));

        // Re-evaluate the large-file guards when their thresholds change in Preferences (#10).
        if (_settings is not null) _settings.Changed += OnSettingsChanged;

        SetText(text, reparse: true);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) Reparse();
        else Dispatcher.UIThread.Post(Reparse);
    }

    // ----- large-file guards (#10) -------------------------------------------
    /// <summary>True when syntax highlighting + hover features are suppressed for size.</summary>
    [ObservableProperty] private bool _highlightingSuppressed;
    /// <summary>True when parsing/object-graph is suppressed for size.</summary>
    [ObservableProperty] private bool _parsingSuppressed;
    /// <summary>Banner text describing which highlight limit was exceeded.</summary>
    [ObservableProperty] private string _highlightBannerText = string.Empty;
    /// <summary>Banner text describing which parse limit was exceeded.</summary>
    [ObservableProperty] private string _parseBannerText = string.Empty;

    // Per-session overrides set by the banner's "enable anyway" buttons.
    private bool _forceHighlightThisSession;
    private bool _forceParseThisSession;

    /// <summary>Raised when the user asks to open Preferences at the large-file limits (#10).</summary>
    public event EventHandler? OpenLimitSettingsRequested;

    [RelayCommand] private void ForceHighlight() { _forceHighlightThisSession = true; Reparse(); }
    [RelayCommand] private void ForceParse() { _forceParseThisSession = true; Reparse(); }
    [RelayCommand] private void OpenLimitSettings() => OpenLimitSettingsRequested?.Invoke(this, EventArgs.Empty);

    private int LineCount(string text)
    {
        int n = 1;
        foreach (var c in text) if (c == '\n') n++;
        return n;
    }

    /// <summary>
    /// A soft vertical "fade" tint for this document's tab header, keyed on file type so
    /// .th / .th2 / .thconfig tabs are visually distinguishable (task 2). Null for others.
    /// </summary>
    public IBrush? TabBrush { get; }

    private static IBrush? TabBrushForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".thconfig" or ".thc" => FadeBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), // green
        ".th2"                => FadeBrush(Color.FromRgb(0x6A, 0x1B, 0x9A)), // purple
        ".th"                 => FadeBrush(Color.FromRgb(0xE6, 0x51, 0x00)), // orange
        _                     => null,
    };

    // Built as an IMMUTABLE brush: a FileDocumentViewModel is constructed on a thread-pool
    // thread (OpenFileAsync continues off the UI thread), and a mutable AvaloniaObject brush
    // created there crashes the compositor with a cross-thread access when it is later rendered
    // as the tab background. Immutable brushes have no thread affinity, so they are safe.
    private static IBrush FadeBrush(Color c) => new ImmutableLinearGradientBrush(
        new[]
        {
            new ImmutableGradientStop(0, Color.FromArgb(0x70, c.R, c.G, c.B)),
            new ImmutableGradientStop(1, Color.FromArgb(0x10, c.R, c.G, c.B)),
        },
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private string _savedText = string.Empty;
    private bool _isDirty;

    /// <summary>True when the in-editor text differs from what's persisted on disk (#3).</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set { if (SetProperty(ref _isDirty, value)) UpdateTitle(); }
    }

    public string DocumentText
    {
        get => _documentText;
        set
        {
            if (SetProperty(ref _documentText, value))
            {
                IsDirty = !string.Equals(_documentText, _savedText, StringComparison.Ordinal);
                ScheduleReparse();
            }
        }
    }

    private void UpdateTitle()
    {
        // Prefix the dirty marker so it can't be clipped when the tab text hits the
        // header's right edge (#12); the leading dot is always visible.
        var name = System.IO.Path.GetFileName(FilePath);
        Title = _isDirty ? "● " + name : name;
    }

    private DispatcherTimer? _reparseTimer;

    /// <summary>Debounced re-parse so each keystroke doesn't re-run the whole pipeline.</summary>
    private void ScheduleReparse()
    {
        if (_reparseTimer is null)
        {
            _reparseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _reparseTimer.Tick += (_, _) => { _reparseTimer!.Stop(); Reparse(); };
        }
        _reparseTimer.Stop();
        _reparseTimer.Start();
    }

    public TherionFile? Ast { get => _ast; private set => SetProperty(ref _ast, value); }
    public SemanticModel? Semantics { get => _semantics; private set => SetProperty(ref _semantics, value); }
    public ISymbolNavigationService? Navigation { get => _navigation; private set => SetProperty(ref _navigation, value); }

    public ImmutableArray<Diagnostic> Diagnostics
    {
        get => _diagnostics;
        private set => SetProperty(ref _diagnostics, value);
    }

    private IReadOnlyList<string> _completionTerms = Array.Empty<string>();
    /// <summary>Editor autocomplete vocabulary: Therion keywords + this file's station/survey names.</summary>
    public IReadOnlyList<string> CompletionTerms
    {
        get => _completionTerms;
        private set => SetProperty(ref _completionTerms, value);
    }

    /// <summary>Replaces text without triggering a binding loop on the editor, then re-parses.</summary>
    public void SetText(string text, bool reparse)
    {
        _documentText = text;
        _savedText = text;          // SetText reflects the persisted (loaded/saved) content.
        IsDirty = false;
        OnPropertyChanged(nameof(DocumentText));
        if (reparse) Reparse();
    }

    /// <summary>
    /// The most recent scroll target that hasn't been consumed by the view yet. Lets a
    /// freshly-opened document (whose editor view isn't attached at request time) replay
    /// the scroll once its view binds.
    /// </summary>
    public SourceSpan? PendingScroll { get; private set; }

    public void RequestScrollTo(SourceSpan span)
    {
        PendingScroll = span;
        ScrollToSpanRequested?.Invoke(this, span);
    }

    /// <summary>Called by the view once it has applied <see cref="PendingScroll"/>.</summary>
    public void ClearPendingScroll() => PendingScroll = null;

    /// <summary>Latest caret location reported by the editor (drives navigation history).</summary>
    public SourceSpan CurrentCaret { get; private set; }
    public void SetCaret(SourceSpan span) => CurrentCaret = span;

    /// <summary>Saved caret offset, so the view position survives tab switches (#11).</summary>
    public int SavedCaretOffset { get; set; }

    /// <summary>
    /// Attaches (or clears) the cross-file workspace snapshot. When set, navigation
    /// becomes workspace-aware (resolves Therion's <c>@</c> notation across files);
    /// when null it falls back to this file's own symbol model.
    /// </summary>
    public void SetWorkspace(WorkspaceSemanticModel? workspace)
    {
        _workspace = workspace;
        // Mutating Navigation raises PropertyChanged the editor binds to — keep it on the UI thread.
        if (Dispatcher.UIThread.CheckAccess()) UpdateNavigation();
        else Dispatcher.UIThread.Post(UpdateNavigation);
    }

    private void UpdateNavigation()
    {
        Navigation = _workspace is { } ws
            ? new WorkspaceSymbolNavigationService(ws, _semantics, FilePath)
            : (_semantics is null ? null : new SymbolNavigationService(_semantics));
    }

    /// <summary>Interpreted file type for the status bar, e.g. "Therion survey (.th)" (#5).</summary>
    [ObservableProperty] private string _interpretedTypeText = string.Empty;
    /// <summary>True when this file is a Therion source type that gets parsed/syntax-checked (#5).</summary>
    [ObservableProperty] private bool _isParsed;

    private void Reparse()
    {
        if (_disposed) return;

        // Only Therion source files are parsed/syntax-checked; .log/.txt/etc. are plain text
        // and must not produce spurious diagnostics (#5).
        var (type, parseable) = DocumentParser.Classify(FilePath, _documentText);
        var typeText = DocumentParser.DescribeType(FilePath, type);

        if (!parseable)
        {
            void ApplyText()
            {
                if (_disposed) return;
                Ast = null;
                Semantics = null;
                UpdateNavigation();
                Diagnostics = ImmutableArray<Diagnostic>.Empty;
                CompletionTerms = BuildCompletionTerms(null);
                InterpretedTypeText = typeText;
                IsParsed = false;
                HighlightingSuppressed = false;
                ParsingSuppressed = false;
                Reparsed?.Invoke(this, EventArgs.Empty);
            }
            if (Dispatcher.UIThread.CheckAccess()) ApplyText();
            else Dispatcher.UIThread.Post(ApplyText);
            return;
        }

        // Large-file guards (#10): evaluate size against the configured limits. The constraint
        // lives here (the editor), never inside the parsing engine, so external callers of the
        // parser are unaffected.
        var s = _settings?.Current;
        int lines = LineCount(_documentText);
        int kb = _documentText.Length / 1024;
        bool overHighlight = s is not null && (lines > s.MaxHighlightLines || kb > s.MaxHighlightKB);
        bool overParse = s is not null && (lines > s.MaxParseLines || kb > s.MaxParseKB);
        bool suppressHighlight = overHighlight && !_forceHighlightThisSession;
        bool suppressParse = overParse && !_forceParseThisSession;

        if (suppressParse)
        {
            var reason = lines > (s?.MaxParseLines ?? int.MaxValue)
                ? $"{lines:N0} lines exceeds the {s!.MaxParseLines:N0}-line parse limit"
                : $"{kb:N0} KB exceeds the {s!.MaxParseKB:N0} KB parse limit";
            var warn = Diagnostic.Create("THP-LARGE-FILE", DiagnosticSeverity.Warning,
                $"'{System.IO.Path.GetFileName(FilePath)}' was not parsed: {reason}. Its identifiers are not in the object graph.",
                new SourceSpan(FilePath, new SourceLocation(1, 1), new SourceLocation(1, 1), 0, 0));
            void ApplyBig()
            {
                if (_disposed) return;
                Ast = null;
                Semantics = null;
                UpdateNavigation();
                Diagnostics = ImmutableArray.Create(warn);
                CompletionTerms = BuildCompletionTerms(null);
                InterpretedTypeText = typeText;
                IsParsed = false;
                ParsingSuppressed = true;
                ParseBannerText = $"Parsing is disabled — {reason}.";
                HighlightingSuppressed = suppressHighlight;
                HighlightBannerText = suppressHighlight ? HighlightReason(s, lines, kb) : string.Empty;
                Reparsed?.Invoke(this, EventArgs.Empty);
            }
            if (Dispatcher.UIThread.CheckAccess()) ApplyBig();
            else Dispatcher.UIThread.Post(ApplyBig);
            return;
        }

        var parsed = DocumentParser.Parse(FilePath, _documentText, _commands);
        void Apply()
        {
            if (_disposed) return;
            Ast = parsed.Ast;
            Semantics = parsed.Semantics;
            UpdateNavigation();
            Diagnostics = parsed.Diagnostics;
            CompletionTerms = BuildCompletionTerms(parsed.Semantics);
            if (parsed.Semantics is { } model) Measurements.Load(model);
            InterpretedTypeText = typeText;
            IsParsed = true;
            ParsingSuppressed = false;
            ParseBannerText = string.Empty;
            HighlightingSuppressed = suppressHighlight;
            HighlightBannerText = suppressHighlight ? HighlightReason(s, lines, kb) : string.Empty;
            Reparsed?.Invoke(this, EventArgs.Empty);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private static string HighlightReason(AppSettings? s, int lines, int kb)
    {
        if (s is null) return string.Empty;
        return lines > s.MaxHighlightLines
            ? $"Syntax highlighting is disabled — {lines:N0} lines exceeds the {s.MaxHighlightLines:N0}-line limit."
            : $"Syntax highlighting is disabled — {kb:N0} KB exceeds the {s.MaxHighlightKB:N0} KB limit.";
    }

    private static IReadOnlyList<string> BuildCompletionTerms(SemanticModel? model)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in TokenClassifier.Keywords) set.Add(k);
        if (model is not null)
        {
            foreach (var s in model.Stations.Values)
            {
                var qn = s.Name.ToString();
                set.Add(qn);
                int dot = qn.LastIndexOf('.');
                if (dot >= 0 && dot + 1 < qn.Length) set.Add(qn[(dot + 1)..]);
            }
            foreach (var sv in model.Surveys.Values) set.Add(sv.Name.ToString());
        }
        return set.ToList();
    }

    // ----- tab context-menu commands (#7) --------------------------------

    [RelayCommand] private void CopyFileName() => SetClipboard(System.IO.Path.GetFileName(FilePath));
    [RelayCommand] private void CopyFullPath() => SetClipboard(FilePath);
    [RelayCommand] private void CopyRelativePath() => SetClipboard(RelativePathToProjectRoot());
    [RelayCommand] private void FloatTab() => Factory?.FloatDockable(this);
    [RelayCommand] private void CloseTab() => Factory?.CloseDockable(this);

    /// <summary>Path relative to the project's highest parent (the entry .thconfig's folder).</summary>
    private string RelativePathToProjectRoot()
    {
        try
        {
            var entry = ProjectEntryDiscovery.FindEntryPoint(FilePath);
            var root = System.IO.Path.GetDirectoryName(entry);
            return string.IsNullOrEmpty(root)
                ? System.IO.Path.GetFileName(FilePath)
                : System.IO.Path.GetRelativePath(root, FilePath);
        }
        catch { return System.IO.Path.GetFileName(FilePath); }
    }

    private static void SetClipboard(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
    }

    // ----- banners: orphan (#4) + external-change reload (#6) ----------------

    /// <summary>True when this file isn't part of the active project's object graph (#4).</summary>
    [ObservableProperty] private bool _isOrphan;
    /// <summary>Light-blue orphan banner text (names the active thconfig).</summary>
    [ObservableProperty] private string _orphanBannerText = string.Empty;

    /// <summary>Transient light-blue info banner (e.g. "Reloaded from disk (12:04)").</summary>
    [ObservableProperty] private string? _infoBanner;

    /// <summary>Red banner shown when the on-disk file changed while we have unsaved edits (#6).</summary>
    [ObservableProperty] private bool _externalChangePending;
    [ObservableProperty] private string _externalChangeText = string.Empty;
    /// <summary>Second stage of the red banner: confirm discarding unsaved edits.</summary>
    [ObservableProperty] private bool _confirmDiscardPending;
    /// <summary>True when the on-disk file was deleted (reload is not offered).</summary>
    [ObservableProperty] private bool _externalDeleted;

    private DispatcherTimer? _infoBannerTimer;

    /// <summary>Sets the orphan banner from the active workspace membership (#4).</summary>
    public void SetWorkspaceMembership(bool isMember, string? activeThconfigName)
    {
        void Apply()
        {
            IsOrphan = !isMember;
            OrphanBannerText = isMember
                ? string.Empty
                : activeThconfigName is { Length: > 0 } name
                    ? $"This file seems like it is not bound to the active project relational tree ({name})."
                    :  "This file seems like it is not bound to the active project relational tree.";
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>
    /// Reacts to an on-disk change of this open file (#6): silently reloads a clean file
    /// (when auto-reload is on), otherwise raises the red "changed on disk" banner.
    /// </summary>
    public void NotifyExternalChange(DateTime localTime, bool deleted, bool autoReload)
    {
        void Apply()
        {
            if (deleted)
            {
                ExternalDeleted = true;
                ExternalChangeText = $"{System.IO.Path.GetFileName(FilePath)} was deleted on disk.";
                ExternalChangePending = true;
                ConfirmDiscardPending = false;
                return;
            }

            if (!IsDirty && autoReload)
            {
                ReloadFromDisk();
                FlashInfo($"Reloaded from disk ({localTime:HH:mm}).");
                return;
            }

            ExternalDeleted = false;
            ExternalChangeText =
                $"{System.IO.Path.GetFileName(FilePath)} was changed on disk ({localTime:HH:mm}).";
            ExternalChangePending = true;
            ConfirmDiscardPending = false;
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }

    [RelayCommand]
    private void ReloadExternal()
    {
        // Reloading would lose unsaved edits → ask first.
        if (IsDirty) { ConfirmDiscardPending = true; return; }
        ReloadFromDisk();
        ClearExternalBanner();
    }

    [RelayCommand]
    private void ConfirmDiscardReload()
    {
        ReloadFromDisk();
        ClearExternalBanner();
    }

    [RelayCommand]
    private void DismissExternal() => ClearExternalBanner();

    [RelayCommand]
    private void DismissInfo() => InfoBanner = null;

    private void ClearExternalBanner()
    {
        ExternalChangePending = false;
        ConfirmDiscardPending = false;
        ExternalDeleted = false;
    }

    private void ReloadFromDisk()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            SetText(System.IO.File.ReadAllText(FilePath), reparse: true);
        }
        catch { /* leave editor content as-is on read failure */ }
    }

    private void FlashInfo(string message)
    {
        InfoBanner = message;
        _infoBannerTimer ??= CreateInfoTimer();
        _infoBannerTimer.Stop();
        _infoBannerTimer.Start();
    }

    private DispatcherTimer CreateInfoTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        t.Tick += (_, _) => { t.Stop(); InfoBanner = null; };
        return t;
    }

    // ----- tab tooltip: full path + cached last-write-time (B1) ----------------

    private string? _cachedLastWrite;

    /// <summary>Tooltip shown on the tab header: full path + cached last-modified time.</summary>
    public string TabTooltip
    {
        get
        {
            _cachedLastWrite ??= LoadLastWriteDisplay();
            return string.IsNullOrEmpty(_cachedLastWrite)
                ? FilePath
                : FilePath + "\n" + _cachedLastWrite;
        }
    }

    private string LoadLastWriteDisplay()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return string.Empty;
            var t = System.IO.File.GetLastWriteTime(FilePath);
            return "Last modified: " + t.ToString("yyyy-MM-dd HH:mm");
        }
        catch { return string.Empty; }
    }

    /// <summary>Invalidates the cached last-write time (call after external-change events).</summary>
    public void InvalidateTabTooltip()
    {
        _cachedLastWrite = null;
        OnPropertyChanged(nameof(TabTooltip));
    }

    /// <summary>Stops the debounced re-parse timer so a closed document doesn't keep firing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        _reparseTimer?.Stop();
        _reparseTimer = null;
        _infoBannerTimer?.Stop();
        _infoBannerTimer = null;
    }
}
