// Main shell ViewModel. Hosts the Dock.Avalonia layout (VS-classic) and owns the
// menu/toolbar commands. Documents are multi-file (MDI) via IDocumentService;
// the document-tracking tools (Object Browser, Diagnostics) follow the active doc.

using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Localization;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Docking;
using TherionProc.Resources;
using TherionProc.Services;
using TherionProc.ViewModels.Docking;

namespace TherionProc.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IStringLocalizer<Strings> _l;
    private readonly ILanguageService _language;
    private readonly IDocumentService _documents;
    private readonly IModelEditService? _editService;
    private readonly ILayoutService? _layout;
    private readonly IAppSettingsService? _settings;
    private readonly IWorkspaceSession? _session;
    private readonly DockFactory _factory;
    private IStoragePicker? _picker;

    public ILayoutService? LayoutService => _layout;

    // Dock layout bound by MainWindow.axaml.
    public IRootDock Layout { get; }
    public DockFactory Factory => _factory;

    // Tool wrappers (shown in the dock); content VMs are reached through them.
    public WorkspaceExplorerToolViewModel WorkspaceTool { get; }
    public ObjectBrowserToolViewModel ObjectBrowserTool { get; }
    public DiagnosticsToolViewModel DiagnosticsTool { get; }
    public CompilerOutputToolViewModel CompilerOutputTool { get; }
    public GeneratedFilesToolViewModel GeneratedFilesTool { get; }
    public XviToolViewModel XviTool { get; }
    public SettingsToolViewModel SettingsTool { get; }

    // Convenience accessors so menu/toolbar/keyboard bindings stay stable.
    public BuildViewModel Build => CompilerOutputTool.Build;
    public DiagnosticsViewModel Diagnostics => DiagnosticsTool.Diagnostics;
    public ObjectBrowserViewModel ObjectBrowser => ObjectBrowserTool.Browser;
    public WorkspaceExplorerViewModel WorkspaceExplorer => WorkspaceTool.Explorer;
    public XviReferencesViewModel XviReferences => XviTool.Xvi;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>True while the previous session's files are being reopened — drives the
    /// startup loading spinner overlay (#14).</summary>
    [ObservableProperty] private bool _isLoading;

    // ----- status bar: open-file metrics (#10) -------------------------------
    /// <summary>True when a real text file is active — shows the file-info status groups.</summary>
    [ObservableProperty] private bool _hasStatusFile;
    [ObservableProperty] private string _statusFilePath = string.Empty;
    [ObservableProperty] private int _statusLength;
    [ObservableProperty] private int _statusLines;
    [ObservableProperty] private int _statusCaretLine = 1;
    [ObservableProperty] private int _statusCaretCol = 1;
    [ObservableProperty] private int _statusCaretPos;
    [ObservableProperty] private string _statusEncoding = string.Empty;

    [ObservableProperty] private bool _strictParserMode;
    partial void OnStrictParserModeChanged(bool value)
    {
        ParserOptionsHost.Current = ParserOptionsHost.Current with
        {
            Mode = value ? ParserMode.Strict : ParserMode.Lenient,
        };
    }

    /// <summary>Editor word-wrap toggle (#7); persisted and applied live to all editors.</summary>
    [ObservableProperty] private bool _wordWrap;
    partial void OnWordWrapChanged(bool value)
    {
        if (_settings is { } s) s.Save(s.Current with { EditorWordWrap = value });
    }

    // Localized menu labels.
    public string MenuFile           => L("Menu_File",                   "_File");
    public string MenuFileOpenFile   => L("Menu_File_OpenFile",          "Open _File...");
    public string MenuFileOpenFolder => L("Menu_File_OpenFolder",        "Open F_older...");
    public string MenuFileExit       => L("Menu_File_Exit",              "E_xit");
    public string MenuView           => L("Menu_View",                   "_View");
    public string MenuViewLanguage   => L("Menu_View_Language",          "_Language");
    public string MenuViewLanguageEn => L("Menu_View_Language_English",  "English");
    public string MenuViewLanguageRo => L("Menu_View_Language_Romanian", "Română");
    public string MenuBuild          => L("Menu_Build",                  "_Build");

    public MainWindowViewModel(
        IStringLocalizer<Strings> localizer,
        ILanguageService language,
        IDocumentService documents,
        DockFactory factory,
        WorkspaceExplorerToolViewModel workspaceTool,
        ObjectBrowserToolViewModel objectBrowserTool,
        DiagnosticsToolViewModel diagnosticsTool,
        CompilerOutputToolViewModel compilerOutputTool,
        GeneratedFilesToolViewModel generatedFilesTool,
        XviToolViewModel xviTool,
        SettingsToolViewModel settingsTool,
        IModelEditService? editService = null,
        ILayoutService? layout = null,
        IAppSettingsService? settings = null,
        IWorkspaceSession? session = null)
    {
        _l = localizer;
        _language = language;
        _documents = documents;
        _editService = editService;
        _layout = layout;
        _settings = settings;
        _session = session;
        _wordWrap = settings?.Current.EditorWordWrap ?? false; // seed without persisting
        _factory = factory;

        WorkspaceTool = workspaceTool;
        ObjectBrowserTool = objectBrowserTool;
        DiagnosticsTool = diagnosticsTool;
        CompilerOutputTool = compilerOutputTool;
        GeneratedFilesTool = generatedFilesTool;
        XviTool = xviTool;
        SettingsTool = settingsTool;

        Layout = _factory.CreateLayout();
        _factory.InitLayout(Layout);

        // Bridge the document service and the dock host (kept decoupled to avoid a DI cycle).
        // OpenFileAsync resumes on a thread-pool thread (ConfigureAwait(false)), so adding
        // the document to the dock — which mutates UI-bound VisibleDockables — must be
        // marshalled to the UI thread.
        _documents.OpenInDockRequested += (_, doc) => OnUiThread(() => _factory.OpenDocument(doc));
        _factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.SetActive(doc);
        };
        _factory.DockableClosed += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.CloseDocument(doc);
        };

        _language.LanguageChanged += (_, _) => Refresh();
        if (_session is not null)
        {
            _session.Changed += (_, _) => OnUiThread(Refresh);          // active config / graph (#7)
            _session.CandidatesChanged += (_, _) => OnUiThread(Refresh);
        }
        _documents.DocumentChanged += (_, _) => RefreshActiveTools();
        _documents.DocumentChanged += (_, _) => OnUiThread(UpdateFileStatus);   // status bar (#10)
        _documents.CaretMoved += (_, span) => OnUiThread(() => UpdateCaretStatus(span));
        _documents.HistoryChanged += (_, _) => OnUiThread(() =>
        {
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        });
        ObjectBrowser.ShotEditRequested += async (_, e) => await ApplyShotEditAsync(e).ConfigureAwait(true);
        WorkspaceExplorer.OpenRequested += async (_, node) => await OpenNodeAsync(node).ConfigureAwait(true);
        WorkspaceExplorer.NavigateRequested += (_, span) => NavigateTo(span);

        Build.CompileCompleted += (_, diags) =>
        {
            var combined = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
            combined.AddRange(DiagnosticsSource());
            combined.AddRange(diags);
            Diagnostics.Load(combined.ToImmutable());
        };
        Diagnostics.NavigateRequested += (_, row) => NavigateTo(row.Span);
        Diagnostics.ScopeChanged += (_, _) => RefreshDiagnostics();
        Build.NavigateRequested += (_, span) => NavigateTo(span);
        // Surface the Compiler Output panel when a build starts (#2).
        Build.BuildStarted += (_, _) => OnUiThread(() => _factory.ShowCompilerOutput());

        Refresh();
        RestoreSession();
    }

    public MainWindowViewModel() : this(
        new NullLocalizer(),
        new LanguageService(),
        new NullDocumentService(),
        DesignFactory(),
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new SettingsToolViewModel(new SettingsViewModel(), new KeyboardShortcutsViewModel()))
    {
        // Designer-only.
    }

    private static DockFactory DesignFactory() => new(
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new SettingsToolViewModel(new SettingsViewModel(), new KeyboardShortcutsViewModel()));

    /// <summary>Wires the storage picker once the View is attached to a TopLevel.</summary>
    public void AttachStoragePicker(IStoragePicker picker) => _picker = picker;

    /// <summary>Restores the workspace root + active thconfig (#9) and last session's files.</summary>
    private void RestoreSession() => _ = RestoreSessionAsync(_settings?.Current);

    private async Task RestoreSessionAsync(AppSettings? s)
    {
        // Re-establish the single-root workspace from last shutdown (#9).
        if (_session is not null && s is not null)
        {
            try
            {
                if (!string.IsNullOrEmpty(s.LastWorkspaceRoot) && System.IO.Directory.Exists(s.LastWorkspaceRoot))
                    await _session.SetRootAsync(s.LastWorkspaceRoot).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(s.LastActiveThconfig) && System.IO.File.Exists(s.LastActiveThconfig))
                    await _session.SetActiveThconfigAsync(s.LastActiveThconfig).ConfigureAwait(true);
            }
            catch { /* fall through to file/sample restore */ }
        }

        var files = s is { RestoreSessionOnStartup: true } ? s.LastSessionFiles : Array.Empty<string>();

        // Show the loading spinner while reopening the previous session's files — parsing each
        // can take a few seconds (#14).
        bool anyToLoad = files.Any(System.IO.File.Exists);
        if (anyToLoad) OnUiThread(() => IsLoading = true);
        try
        {
            foreach (var path in files)
            {
                if (!System.IO.File.Exists(path)) continue;
                // Each open swaps the file into its saved tab slot (dock/float/order) when a
                // restore placeholder is holding it; otherwise it lands in the main well.
                try { await _documents.OpenFileAsync(path).ConfigureAwait(true); }
                catch { /* skip files that fail to open */ }
            }
        }
        finally { if (anyToLoad) OnUiThread(() => IsLoading = false); }

        // Drop any restore placeholders whose file wasn't reopened (deleted on disk, or
        // session-restore disabled) so no blank "ghost" tab remains. Posted at Background
        // priority so it runs AFTER the queued per-file OpenDocument swaps (which marshal
        // onto the UI thread), never before them. Startup intentionally shows no sample
        // document when nothing is restored (task 5).
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _factory.RemoveUnresolvedPlaceholders(),
            Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Records the open files + workspace root/active thconfig for next launch (#9).</summary>
    public void PersistSession()
    {
        if (_settings is null) return;
        var paths = new System.Collections.Generic.List<string>();
        foreach (var doc in _documents.Documents)
        {
            if (!string.IsNullOrEmpty(doc.FilePath) && System.IO.File.Exists(doc.FilePath))
                paths.Add(doc.FilePath);
        }
        try
        {
            _settings.Save(_settings.Current with
            {
                LastSessionFiles = paths,
                LastWorkspaceRoot = _session?.RootPath,
                LastActiveThconfig = _session?.ActiveThconfig?.FullPath,
            });
        }
        catch { /* best-effort */ }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickOpenFileAsync(MenuFileOpenFile).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await _documents.OpenFileAsync(path).ConfigureAwait(true);
            StatusText = path;
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (_picker is null) return;
        var folder = await _picker.PickOpenFolderAsync(MenuFileOpenFolder).ConfigureAwait(true);
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            await _documents.OpenFolderAsync(folder).ConfigureAwait(true);
            StatusText = _documents.CurrentPath ?? folder;
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand] private void SwitchToEnglish()  => _language.SetLanguage("en");
    [RelayCommand] private void SwitchToRomanian() => _language.SetLanguage("ro");

    [RelayCommand] private void ToggleWorkspaceExplorer() => Activate(WorkspaceTool);
    [RelayCommand] private void ToggleDiagnostics()       => Activate(DiagnosticsTool);
    [RelayCommand] private void ToggleSettings()          => Activate(SettingsTool);

    // ---- commands wired to keyboard shortcut service (#5) -------------------
    // ShowFindInFiles / ShowReplaceInFiles / RenameSymbol are routed through the
    // shortcut system and delegated to the View via events.
    public event EventHandler? ShowFindInFilesRequested;
    public event EventHandler? ShowReplaceInFilesRequested;
    public event EventHandler? RenameSymbolRequested;

    [RelayCommand] private void ShowFindInFiles()    => ShowFindInFilesRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void ShowReplaceInFiles() => ShowReplaceInFilesRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void RenameSymbol()       => RenameSymbolRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task Save()
    {
        if (_documents.Active is not { } doc) return;
        try
        {
            await _documents.WriteCurrentTextAsync(doc.DocumentText).ConfigureAwait(true);
            StatusText = $"Saved {doc.FilePath}";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    // ---- navigation history (back/forward across files, #1) ----
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task GoBack() => _documents.GoBackAsync();
    private bool CanGoBack() => _documents.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task GoForward() => _documents.GoForwardAsync();
    private bool CanGoForward() => _documents.CanGoForward;

    private void Activate(IDockable tool)
    {
        try { _factory.SetActiveDockable(tool); } catch { /* best-effort focus */ }
    }

    private async Task OpenNodeAsync(WorkspaceTreeNode node)
    {
        if (node.FullPath is not { } path) return;
        try { await _documents.OpenFileAsync(path).ConfigureAwait(true); }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private void NavigateTo(Therion.Core.SourceSpan span)
    {
        // Open the document the span lives in (if needed), activate it, then scroll/flash.
        _ = _documents.NavigateToSpanAsync(span);
    }

    private async Task ApplyShotEditAsync(ShotEditEventArgs e)
    {
        if (_editService is null || _documents.CurrentAst is null) return;
        if (e.Row.SourceRow is not { } source || e.Row.FieldDefinition is not { } fields) return;

        int idx = -1;
        for (int i = 0; i < fields.Fields.Length; i++)
        {
            if (string.Equals(fields.Fields[i], e.Field, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        }
        if (idx < 0) return;

        var values = source.Values.ToBuilder();
        while (values.Count <= idx) values.Add("-");
        values[idx] = e.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

        var replacement = source with { Values = values.ToImmutable() };
        var result = _editService.ReplaceNode(_documents.CurrentAst, source, replacement);
        if (!result.Success || result.UpdatedText is null)
        {
            StatusText = result.Diagnostics.Length > 0 ? result.Diagnostics[0].Message : "Edit failed.";
            return;
        }
        try
        {
            await _documents.WriteCurrentTextAsync(result.UpdatedText).ConfigureAwait(true);
            StatusText = $"Shot {e.Row.From} → {e.Row.To} updated.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private static void OnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private void RefreshActiveTools()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshActiveTools);
            return;
        }

        if (_documents.Workspace is { } workspace)
            ObjectBrowser.Load(workspace);
        else if (_documents.CurrentSemantics is { } model)
            ObjectBrowser.Load(model);
        else
            ObjectBrowser.Load(SemanticModel.Empty);

        RefreshDiagnostics();
        XviReferences.Refresh();
        WorkspaceExplorer.Refresh();
    }

    /// <summary>Returns the diagnostics source based on the current scope toggle.</summary>
    private System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> DiagnosticsSource()
    {
        if (Diagnostics.ShowProjectScope && _documents.Workspace is { } ws && !ws.Diagnostics.IsDefaultOrEmpty)
        {
            // Merge workspace-level diagnostics with current-file parser/semantic ones
            // so the user sees both graph-level warnings and local parse errors.
            var merged = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
            merged.AddRange(ws.Diagnostics);
            // Also include per-file semantic diagnostics from every loaded file.
            foreach (var model in ws.PerFile.Values)
                merged.AddRange(model.Diagnostics);
            return merged.ToImmutable();
        }
        return _documents.CurrentDiagnostics;
    }

    private void RefreshDiagnostics()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshDiagnostics);
            return;
        }
        Diagnostics.Load(DiagnosticsSource());
    }

    // ----- status bar (#10) --------------------------------------------------

    /// <summary>Refreshes the open-file metrics (path, length, lines, encoding) on the status bar.</summary>
    private void UpdateFileStatus()
    {
        var doc = _documents.Active;
        if (doc is null || string.IsNullOrEmpty(doc.FilePath))
        {
            HasStatusFile = false;
            return;
        }

        HasStatusFile = true;
        StatusFilePath = doc.FilePath;
        var text = doc.DocumentText;
        StatusLength = text.Length;
        StatusLines = CountLines(text);
        StatusEncoding = DetectEncoding(doc.FilePath);
    }

    /// <summary>Updates the caret line/column/offset on the status bar (#10).</summary>
    private void UpdateCaretStatus(Therion.Core.SourceSpan span)
    {
        // Only the active document's caret drives the status bar.
        if (!string.Equals(span.FilePath, _documents.CurrentPath, StringComparison.OrdinalIgnoreCase)) return;
        StatusCaretLine = span.Start.Line;
        StatusCaretCol = span.Start.Column;
        StatusCaretPos = span.StartOffset;
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0) return 1;
        int lines = 1;
        foreach (var c in text) if (c == '\n') lines++;
        return lines;
    }

    /// <summary>BOM-based encoding label for the status bar (text / UTF-8 / Unicode / other, #10).</summary>
    private static string DetectEncoding(string path)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            Span<byte> bom = stackalloc byte[4];
            int n = fs.Read(bom);
            if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return "UTF-8 BOM";
            if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return "Unicode (UTF-16 LE)";
            if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return "Unicode (UTF-16 BE)";
            return "UTF-8"; // no BOM — Therion files are UTF-8/ASCII text
        }
        catch { return "UTF-8"; }
    }

    private void Refresh()
    {
        Title = ComposeTitle();
        OnPropertyChanged(nameof(MenuFile));
        OnPropertyChanged(nameof(MenuFileOpenFile));
        OnPropertyChanged(nameof(MenuFileOpenFolder));
        OnPropertyChanged(nameof(MenuFileExit));
        OnPropertyChanged(nameof(MenuView));
        OnPropertyChanged(nameof(MenuViewLanguage));
        OnPropertyChanged(nameof(MenuViewLanguageEn));
        OnPropertyChanged(nameof(MenuViewLanguageRo));
        OnPropertyChanged(nameof(MenuBuild));
    }

    private string L(string key, string fallback)
    {
        var v = _l[key];
        return v.ResourceNotFound ? fallback : v.Value;
    }

    /// <summary>App title + active thconfig filename (+ main survey title when detectable, #7).</summary>
    private string ComposeTitle()
    {
        var appTitle = L("AppTitle", "TherionProc");
        if (_session?.ActiveThconfig is not { } active) return appTitle;

        var name = System.IO.Path.GetFileName(active.FullPath);
        var survey = MainSurveyLabel(_session.Model);
        return survey is null ? $"{appTitle} — {name}" : $"{appTitle} — {name} > {survey}";
    }

    /// <summary>The single root survey's title/name, or null when the graph has 0 or many roots.</summary>
    private static string? MainSurveyLabel(WorkspaceSemanticModel? model)
    {
        if (model is null) return null;
        SurveySymbol? root = null;
        foreach (var perFile in model.PerFile.Values)
        {
            foreach (var sv in perFile.Surveys.Values)
            {
                if (sv.Parent is not null) continue;
                if (root is not null) return null; // more than one root → ambiguous
                root = sv;
            }
        }
        if (root is null) return null;
        return string.IsNullOrWhiteSpace(root.Title) ? root.Name.Last : root.Title;
    }

    private sealed class NullLocalizer : IStringLocalizer<Strings>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);
        public System.Collections.Generic.IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            System.Array.Empty<LocalizedString>();
    }
}
