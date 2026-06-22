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
using Avalonia.Threading;
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

    public FileDocumentViewModel(string filePath, string text, MeasurementsViewModel measurements,
        ICommandRegistry? commands = null)
    {
        FilePath = filePath;
        _commands = commands;
        Measurements = measurements;

        Id = filePath;
        Title = System.IO.Path.GetFileName(filePath);
        CanFloat = true;
        CanClose = true;
        CanPin = true;

        SetText(text, reparse: true);
    }

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

    private void Reparse()
    {
        if (_disposed) return;
        var parsed = DocumentParser.Parse(FilePath, _documentText, _commands);
        Ast = parsed.Ast;
        Semantics = parsed.Semantics;
        UpdateNavigation();
        Diagnostics = parsed.Diagnostics;
        CompletionTerms = BuildCompletionTerms(parsed.Semantics);
        if (parsed.Semantics is { } model) Measurements.Load(model);
        Reparsed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Stops the debounced re-parse timer so a closed document doesn't keep firing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reparseTimer?.Stop();
        _reparseTimer = null;
    }
}
