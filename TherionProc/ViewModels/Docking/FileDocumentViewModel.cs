// One open .th/.th2/.thconfig file as a Dock document (multi-file MDI).
// Carries its own parsed model + Measurements grid, so each document tab is a
// fully independent view of its file and can be floated/docked on its own.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
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

    /// <summary>Cancels the previous in-flight background parse when a newer reparse starts.</summary>
    private CancellationTokenSource? _parseCts;

    private string _documentText = string.Empty;
    private TherionFile? _ast;
    private SemanticModel? _semantics;
    private ISymbolNavigationService? _navigation;
    private WorkspaceSemanticModel? _workspace;
    private ImmutableArray<Diagnostic> _diagnostics = ImmutableArray<Diagnostic>.Empty;
    // Parse + per-file semantic diagnostics, before the workspace equate reconciliation is layered on.
    private ImmutableArray<Diagnostic> _baseDiagnostics = ImmutableArray<Diagnostic>.Empty;

    /// <summary>Raised when something wants the editor to scroll to a span (e.g. diagnostics).</summary>
    public event EventHandler<SourceSpan>? ScrollToSpanRequested;

    /// <summary>Raised after a re-parse so document-tracking tools can refresh.</summary>
    public event EventHandler? Reparsed;

    /// <summary>
    /// Raised just before a save so the editor view can clean the document in place
    /// (: trim trailing whitespace + final newline) while preserving the caret.
    /// </summary>
    public event EventHandler? SaveCleanupRequested;

    /// <summary>Asks the bound editor view to apply the on-save cleanup (caret-preserving).</summary>
    public void RequestSaveCleanup() => SaveCleanupRequested?.Invoke(this, EventArgs.Empty);

    public string FilePath { get; }

    public MeasurementsViewModel Measurements { get; }

    /// <summary>True only for .th files — the Measurements tab is hidden for any other type (#4).</summary>
    public bool IsThFile =>
        string.Equals(System.IO.Path.GetExtension(FilePath), ".th", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for .th2 sketch files — gates the "Edit with Mapiah" button.</summary>
    public bool IsTh2File =>
        string.Equals(System.IO.Path.GetExtension(FilePath), ".th2", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for thconfig project files (.thconfig/.thc or a bare "thconfig") — gates the
    /// "set as active thconfig" / "set working directory" tab buttons (task 1).</summary>
    public bool IsThconfigFile
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FilePath);
            return ext.Length == 0
                ? string.Equals(System.IO.Path.GetFileName(FilePath), "thconfig", StringComparison.OrdinalIgnoreCase)
                : ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
                  || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
        }
    }

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
        if (_settings is not null) { _guardLimits = GuardLimits(); _settings.Changed += OnSettingsChanged; }

        SetText(text, reparse: true);
    }

    // Settings are saved for many unrelated reasons (recent files, layout, MRU…); only the
    // large-file limits affect this document's parse, so gate the reparse on those — otherwise
    // every settings save re-parses every open document (quadratic during session restore).
    private (int, int, int, int) _guardLimits;

    private (int, int, int, int) GuardLimits()
    {
        var s = _settings?.Current;
        return s is null ? default : (s.MaxHighlightLines, s.MaxHighlightKB, s.MaxParseLines, s.MaxParseKB);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var limits = GuardLimits();
        if (limits == _guardLimits) return;
        _guardLimits = limits;
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
        // header's right edge (#12); the leading dot is always visible. A pinned tab
        // gets a leading pin glyph so it reads as "kept open".
        var name = System.IO.Path.GetFileName(FilePath);
        var prefix = IsPinned ? "📌 " : string.Empty;
        Title = prefix + (_isDirty ? "● " + name : name);
    }

    // ----- tab pinning ----------------------------------------------
    // A pinned document is excluded from the bulk "close others / right / all" actions and is
    // marked with a pin glyph. It can still be closed individually (and unpinned). Pure model
    // state — no Dock auto-hide rail involved (that is Dock's own "pin", which we don't use here).
    [ObservableProperty] private bool _isPinned;
    partial void OnIsPinnedChanged(bool value) => UpdateTitle();

    [RelayCommand] private void TogglePin() => IsPinned = !IsPinned;

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
        // Navigation and the equate-reference diagnostics both depend on the workspace; refresh both
        // on the UI thread (mutating them raises PropertyChanged the editor binds to).
        if (Dispatcher.UIThread.CheckAccess()) ApplyWorkspace();
        else Dispatcher.UIThread.Post(ApplyWorkspace);
    }

    private void ApplyWorkspace()
    {
        UpdateNavigation();
        UpdateParentFiles();
        Diagnostics = CombineDiagnostics();
    }

    // ----- parent-file navigation (the files that include/reference this one) -----

    /// <summary>
    /// Full paths of the files that pull this one into the active project's object graph via
    /// <c>input</c> / <c>load</c> / <c>source</c>. Usually one — the enclosing <c>.th</c> or the
    /// project's <c>.thconfig</c> — but a file can be included from several places. Empty for a
    /// root file (e.g. the top-level thconfig), which gates the "go to parent" editor button.
    /// </summary>
    public IReadOnlyList<string> ParentFiles { get; private set; } = Array.Empty<string>();

    /// <summary>True when <see cref="ParentFiles"/> has at least one entry (drives the button's visibility).</summary>
    [ObservableProperty] private bool _hasParentFile;

    private void UpdateParentFiles()
    {
        var parents = ComputeParentFiles(_workspace, FilePath);
        ParentFiles = parents;
        HasParentFile = parents.Count > 0;
    }

    /// <summary>
    /// The distinct files whose object-graph edge points at <paramref name="filePath"/> (i.e. that
    /// include it), in a stable order. A self-loop is ignored. Pure, so it is unit-testable.
    /// </summary>
    internal static IReadOnlyList<string> ComputeParentFiles(WorkspaceSemanticModel? workspace, string filePath)
    {
        if (workspace is null || string.IsNullOrEmpty(filePath)) return Array.Empty<string>();
        string self;
        try { self = System.IO.Path.GetFullPath(filePath); } catch { self = filePath; }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in workspace.FileGraphEdges)
        {
            if (!string.Equals(to, self, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(from, self, StringComparison.OrdinalIgnoreCase)) continue; // ignore self-loop
            if (seen.Add(from)) result.Add(from);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private void UpdateNavigation()
    {
        Navigation = _workspace is { } ws
            ? new WorkspaceSymbolNavigationService(ws, _semantics, FilePath)
            : (_semantics is null ? null : new SymbolNavigationService(_semantics));
    }

    /// <summary>
    /// Base (parse + per-file) diagnostics plus equate-reference validation: cross-file via the
    /// workspace when attached, else the per-file fallback that flags every unresolved reference.
    /// </summary>
    private ImmutableArray<Diagnostic> CombineDiagnostics()
    {
        if (_semantics is not { } model) return _baseDiagnostics;
        var equate = _workspace is { } ws
            ? ws.ValidateEquateReferences(model)
            : model.UnresolvedEquateDiagnostics();
        return equate.IsDefaultOrEmpty ? _baseDiagnostics : _baseDiagnostics.AddRange(equate);
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
                _baseDiagnostics = ImmutableArray<Diagnostic>.Empty;
                Diagnostics = CombineDiagnostics();
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
                _baseDiagnostics = ImmutableArray.Create(warn);
                Diagnostics = CombineDiagnostics();
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

        // run the (potentially expensive) parse + semantic bind on a background thread so
        // typing never blocks the UI. A snapshot of the text is captured; a fresh cancellation
        // token supersedes any in-flight parse so only the latest result is applied.
        var snapshot = _documentText;
        _parseCts?.Cancel();
        var cts = new CancellationTokenSource();
        _parseCts = cts;
        var token = cts.Token;

        void Apply(ParsedDocument parsed)
        {
            if (_disposed || token.IsCancellationRequested) return;
            Ast = parsed.Ast;
            Semantics = parsed.Semantics;
            UpdateNavigation();
            _baseDiagnostics = parsed.Diagnostics;
            Diagnostics = CombineDiagnostics();
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

        Task.Run(() =>
        {
            var parsed = DocumentParser.Parse(FilePath, snapshot, _commands);
            if (token.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() => Apply(parsed));
        }, token);
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
            // station/survey names (and their shared leaf segments) repeat heavily across
            // every open document — intern them so the per-document completion lists share instances.
            var interner = Therion.Core.StringInterner.Shared;
            foreach (var s in model.Stations.Values)
            {
                var qn = interner.Intern(s.Name.ToString());
                set.Add(qn);
                int dot = qn.LastIndexOf('.');
                if (dot >= 0 && dot + 1 < qn.Length) set.Add(interner.Intern(qn[(dot + 1)..]));
            }
            foreach (var sv in model.Surveys.Values) set.Add(interner.Intern(sv.Name.ToString()));
        }
        return set.ToList();
    }

    // ----- tab context-menu commands (#7) --------------------------------

    [RelayCommand] private void CopyFileName() => SetClipboard(System.IO.Path.GetFileName(FilePath));
    [RelayCommand] private void CopyFullPath() => SetClipboard(FilePath);
    [RelayCommand] private void CopyRelativePath() => SetClipboard(RelativePathToProjectRoot());

    /// <summary>reveal this document's file in the OS file manager.</summary>
    [RelayCommand]
    private void RevealInFileManager()
    {
        try { (TherionProc.AppServices.Provider.GetService(typeof(Therion.Build.IShellOpener)) as Therion.Build.IShellOpener)?.RevealInFileManager(FilePath); }
        catch { /* design-time / no container */ }
    }
    [RelayCommand] private void FloatTab() => Factory?.FloatDockable(this);
    [RelayCommand] private void CloseTab() => Factory?.CloseDockable(this);

    // ----- bulk tab close -------------------------------------------
    // Operate on this tab's sibling documents within the same dock (the central well or a float
    // window), honouring the pin flag. Driven from the document tab context menu.

    /// <summary>Closes every other document in this tab's dock, keeping pinned tabs.</summary>
    [RelayCommand]
    private void CloseOtherTabs()
    {
        foreach (var sibling in SiblingDocuments())
            if (!ReferenceEquals(sibling, this) && !sibling.IsPinned)
                Factory?.CloseDockable(sibling);
    }

    /// <summary>Closes the documents to the right of this tab in its dock, keeping pinned tabs.</summary>
    [RelayCommand]
    private void CloseTabsToRight()
    {
        var siblings = SiblingDocuments();
        int self = siblings.IndexOf(this);
        if (self < 0) return;
        for (int i = self + 1; i < siblings.Count; i++)
            if (!siblings[i].IsPinned) Factory?.CloseDockable(siblings[i]);
    }

    /// <summary>Closes every document in this tab's dock, keeping pinned tabs.</summary>
    [RelayCommand]
    private void CloseAllTabs()
    {
        foreach (var sibling in SiblingDocuments())
            if (!sibling.IsPinned) Factory?.CloseDockable(sibling);
    }

    /// <summary>Snapshot of the document tabs that share this tab's dock (stable for iteration).</summary>
    private System.Collections.Generic.List<FileDocumentViewModel> SiblingDocuments()
    {
        var result = new System.Collections.Generic.List<FileDocumentViewModel>();
        if (Owner is IDock dock && dock.VisibleDockables is { } list)
            foreach (var d in list)
                if (d is FileDocumentViewModel f) result.Add(f);
        return result;
    }

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

    /// <summary>keep the editor's version by writing it to disk (overwrites the external
    /// change, or recreates a deleted file). The document service performs the actual save.</summary>
    [RelayCommand]
    private void OverwriteExternal()
    {
        SaveToDiskRequested?.Invoke(this, EventArgs.Empty);
        ClearExternalBanner();
    }

    /// <summary>Raised when the user chooses "Keep mine (save to disk)" on the external-change banner.</summary>
    public event EventHandler? SaveToDiskRequested;

    /// <summary>show a read-only side-by-side comparison of the on-disk vs editor text.</summary>
    [RelayCommand]
    private void CompareExternal()
    {
        string disk = string.Empty;
        try { if (System.IO.File.Exists(FilePath)) disk = System.IO.File.ReadAllText(FilePath); } catch { }
        CompareExternalRequested?.Invoke(this, disk);
    }

    /// <summary>Raised with the on-disk text when the user clicks "Compare" (the view opens a diff).</summary>
    public event EventHandler<string>? CompareExternalRequested;

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
        _parseCts?.Cancel();
        _parseCts?.Dispose();
        _parseCts = null;
    }
}
