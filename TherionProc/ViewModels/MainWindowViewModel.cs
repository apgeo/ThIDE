// Main shell ViewModel. Hosts the Dock.Avalonia layout (VS-classic) and owns the
// menu/toolbar commands. Documents are multi-file (MDI) via IDocumentService;
// the document-tracking tools (Object Browser, Diagnostics) follow the active doc.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    private readonly Therion.Semantics.ISemanticRuleRunner? _ruleRunner;
    // Cache the rule diagnostics per workspace snapshot so they aren't recomputed every refresh.
    private WorkspaceSemanticModel? _ruleDiagWorkspace;
    private System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> _ruleDiagCache;
    private readonly IModelEditService? _editService;
    private readonly ILayoutService? _layout;
    private readonly IAppSettingsService? _settings;
    private readonly ILogService? _log;   // #3 in-app activity log
    private readonly IWorkspaceSession? _session;
    private readonly DockFactory _factory;
    private IStoragePicker? _picker;

    public ILayoutService? LayoutService => _layout;

    // Dock layout bound by MainWindow.axaml (swappable so "reset layout" can rebuild it, #16).
    private IRootDock _dockLayout = null!;
    public IRootDock Layout { get => _dockLayout; private set => SetProperty(ref _dockLayout, value); }
    public DockFactory Factory => _factory;

    // Tool wrappers (shown in the dock); content VMs are reached through them.
    public WorkspaceExplorerToolViewModel WorkspaceTool { get; }
    public ObjectBrowserToolViewModel ObjectBrowserTool { get; }
    public DiagnosticsToolViewModel DiagnosticsTool { get; }
    public CompilerOutputToolViewModel CompilerOutputTool { get; }
    public GeneratedFilesToolViewModel GeneratedFilesTool { get; }
    public XviToolViewModel XviTool { get; }
    public OutlineToolViewModel OutlineTool { get; }
    public ProjectToolViewModel ProjectTool { get; }   // PROJ-02/03/07
    public LogToolViewModel LogTool { get; }           // #3 activity log
    public SettingsToolViewModel SettingsTool { get; }

    /// <summary>PROJ-08: clickable breadcrumb of the @-qualified name at the caret (status bar).</summary>
    public BreadcrumbViewModel Breadcrumb { get; }

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
    /// <summary>Interpreted file type + parsed/not-parsed, shown on the status bar (#5).</summary>
    [ObservableProperty] private string _statusFileType = string.Empty;

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

    // ---- EDIT-13: whitespace / EOL / indent-guide render toggles (View menu) ----
    /// <summary>True when EDIT-13 is enabled (compile-time flag + runtime setting) — gates the View-menu items.</summary>
    public bool WhitespaceFeatureEnabled =>
        EditorFeatureFlags.IsEnabled(EditorFeature.WhitespaceGuides, _settings?.Current);

    [ObservableProperty] private bool _showWhitespace;
    partial void OnShowWhitespaceChanged(bool value)
    {
        if (_settings is { } s) s.Save(s.Current with { EditorShowWhitespace = value });
    }

    [ObservableProperty] private bool _showEndOfLine;
    partial void OnShowEndOfLineChanged(bool value)
    {
        if (_settings is { } s) s.Save(s.Current with { EditorShowEndOfLine = value });
    }

    [ObservableProperty] private bool _showIndentGuides;
    partial void OnShowIndentGuidesChanged(bool value)
    {
        if (_settings is { } s) s.Save(s.Current with { EditorShowIndentGuides = value });
    }

    // ---- EDIT-07: minimap toggle (View menu) ----
    /// <summary>EDIT-07 gate (compile-time flag + runtime setting).</summary>
    public bool MinimapFeatureEnabled =>
        EditorFeatureFlags.IsEnabled(EditorFeature.Minimap, _settings?.Current);

    [ObservableProperty] private bool _showMinimap;
    partial void OnShowMinimapChanged(bool value)
    {
        if (_settings is { } s) s.Save(s.Current with { EditorShowMinimap = value });
    }

    // ---- EDIT-11: split editor (side-by-side via a float window) ----
    /// <summary>EDIT-11 gate (compile-time flag + runtime setting).</summary>
    public bool SplitFeatureEnabled =>
        EditorFeatureFlags.IsEnabled(EditorFeature.SplitView, _settings?.Current);

    [RelayCommand]
    private void SplitEditor()
    {
        if (!SplitFeatureEnabled) return;
        if (_documents.Active is { } doc)
            try { _factory.FloatDockable(doc); } catch { /* best-effort */ }
    }

    // ---- #3: Go-to-File quick open (Ctrl+P) + #4: command palette (Ctrl+Shift+P) ----
    private readonly QuickOpenProvider? _quickOpen;

    /// <summary>The active quick-pick overlay VM (Go-to-File or Command palette), or null when hidden.</summary>
    [ObservableProperty] private QuickPick.QuickPickViewModel? _quickPick;

    /// <summary>Raised when the user picks a directory in Go-to-File; the view confirms then loads it.</summary>
    public event EventHandler<string>? ConfirmLoadFolderRequested;

    public Task OpenFolderPathAsync(string path) => _documents.OpenFolderAsync(path);

    public void ShowQuickOpen()
    {
        if (_quickOpen is null) return;
        var palette = _quickOpen.CreatePalette(path =>
        {
            ConfirmLoadFolderRequested?.Invoke(this, path);
            return Task.CompletedTask;
        });
        palette.CloseRequested += (_, _) => QuickPick = null;
        QuickPick = palette;
    }

    // View-level command-palette actions (handled by MainWindow code-behind: open windows / settings).
    public event EventHandler<string?>? ShowPreferencesRequested;
    public event EventHandler? ShowAboutRequested;
    public event EventHandler? ShowThbookRequested;
    public event EventHandler? ShowBookmarksRequested;
    public event EventHandler? ShowRelationalMapRequested;

    public void RaiseShowPreferences(string? section) => ShowPreferencesRequested?.Invoke(this, section);
    public void RaiseShowAbout() => ShowAboutRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseShowThbook() => ShowThbookRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseShowBookmarks() => ShowBookmarksRequested?.Invoke(this, EventArgs.Empty);
    public void RaiseShowRelationalMap() => ShowRelationalMapRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the Command palette (Ctrl+Shift+P, #4).</summary>
    public void ShowCommandPalette()
    {
        var docs = TherionProc.AppServices.Provider.GetService(typeof(IThbookDocumentationService))
            as IThbookDocumentationService;
        var stationLimit = _settings?.Current.StationSearchLimit ?? 4000;
        var provider = new CommandPaletteProvider(this, docs, _documents, _session, Push, stationLimit);
        var palette = provider.CreatePalette();
        palette.CloseRequested += (_, _) => QuickPick = null;
        QuickPick = palette;

        void Push(QuickPick.QuickPickViewModel next)
        {
            next.CloseRequested += (_, _) => QuickPick = null;
            QuickPick = next;
        }
    }

    // ---- Ctrl+Tab document switcher (#2) --------------------------------

    /// <summary>Overlay state for the Ctrl+Tab document switcher (bound in MainWindow.axaml).</summary>
    public DocumentSwitcherViewModel DocumentSwitcher { get; } = new();

    private readonly List<FileDocumentViewModel> _mru = new();
    private int _switcherIndex;

    /// <summary>Moves the active document to the front of the most-recently-used list.</summary>
    private void TouchMru()
    {
        if (_documents.Active is not { } active) return;
        _mru.Remove(active);
        _mru.Insert(0, active);
    }

    /// <summary>
    /// Opens the switcher (first Ctrl+Tab) or advances the highlighted entry (subsequent Tabs while
    /// Ctrl is held). Forward = Ctrl+Tab, backward = Ctrl+Shift+Tab. MRU-ordered like Alt+Tab.
    /// Driven from a window-level handler so it works no matter where focus is.
    /// </summary>
    public void ShowOrAdvanceDocumentSwitcher(bool forward)
    {
        var open = _documents.Documents;
        if (open.Count < 2) return;

        if (!DocumentSwitcher.IsOpen)
        {
            // Refresh MRU: drop closed docs, append any open ones not seen yet.
            _mru.RemoveAll(d => !open.Contains(d));
            foreach (var d in open) if (!_mru.Contains(d)) _mru.Add(d);
            var ordered = _mru.Where(open.Contains).ToList();

            DocumentSwitcher.Items.Clear();
            foreach (var d in ordered)
                DocumentSwitcher.Items.Add(new DocumentSwitcherItem(d, d.Title, FolderOf(d.FilePath)));

            // Index 0 is the current document; start on the previous (forward) or last (backward).
            _switcherIndex = forward ? 1 : DocumentSwitcher.Items.Count - 1;
            DocumentSwitcher.IsOpen = true;
        }
        else
        {
            int n = DocumentSwitcher.Items.Count;
            _switcherIndex = forward ? (_switcherIndex + 1) % n : (_switcherIndex - 1 + n) % n;
        }
        HighlightSwitcher();
    }

    private void HighlightSwitcher()
    {
        for (int i = 0; i < DocumentSwitcher.Items.Count; i++)
            DocumentSwitcher.Items[i].IsSelected = i == _switcherIndex;
    }

    /// <summary>Activates the highlighted document and closes the overlay (Ctrl released).</summary>
    public void CommitDocumentSwitcher()
    {
        if (!DocumentSwitcher.IsOpen) return;
        DocumentSwitcher.IsOpen = false;
        if (_switcherIndex >= 0 && _switcherIndex < DocumentSwitcher.Items.Count)
        {
            var target = DocumentSwitcher.Items[_switcherIndex].Document;
            OnUiThread(() => _factory.OpenDocument(target));
        }
        DocumentSwitcher.Items.Clear();
    }

    /// <summary>Closes the overlay without switching (Escape).</summary>
    public void CancelDocumentSwitcher()
    {
        DocumentSwitcher.IsOpen = false;
        DocumentSwitcher.Items.Clear();
    }

    private static string FolderOf(string path)
    {
        try { return System.IO.Path.GetDirectoryName(path) ?? string.Empty; }
        catch { return string.Empty; }
    }

    // Localized menu labels.
    public string MenuFile           => L("Menu_File",                   "_File");
    public string MenuFileOpenFile   => L("Menu_File_OpenFile",          "Open _File...");
    public string MenuFileOpenThconfig => L("Menu_File_OpenThconfig",    "Open _thconfig...");
    public string MenuFileOpenFolder => L("Menu_File_OpenFolder",        "Open F_older...");
    public string MenuSettings       => L("Menu_Settings",               "_Settings");
    public string MenuHelp           => L("Menu_Help",                   "_Help");
    public string MenuHelpThbook     => L("Menu_Help_Thbook",            "Therion _Book");
    public string MenuHelpAbout      => L("Menu_Help_About",             "_About TherionProc");
    public string MenuFileExit       => L("Menu_File_Exit",              "E_xit");
    public string MenuView           => L("Menu_View",                   "_View");
    public string MenuViewLanguage   => L("Menu_View_Language",          "_Language");
    public string MenuViewLanguageEn => L("Menu_View_Language_English",  "English");
    public string MenuViewLanguageRo => L("Menu_View_Language_Romanian", "Română");
    public string MenuBuild          => L("Menu_Build",                  "_Compile");
    public string MenuBuildBuild     => L("Menu_Build_Build",            "_Compile");
    public string MenuBuildRebuild   => L("Menu_Build_Rebuild",          "_Recompile");
    public string MenuBuildCancel    => L("Menu_Build_Cancel",           "_Cancel");
    public string MenuFileRecent     => L("Menu_File_Recent",           "Recent _Files");
    // View menu
    public string MenuViewObjectBrowser => L("Menu_View_ObjectBrowser", "Object Browser");
    public string MenuViewWorkspace     => L("Menu_View_Workspace",     "Workspace");
    public string MenuViewDiagnostics   => L("Menu_View_Diagnostics",   "Diagnostics");
    public string MenuViewRelationalMap => L("Menu_View_RelationalMap", "Relational Map…");
    public string MenuViewStrictParser  => L("Menu_View_StrictParser",  "Strict parser mode");
    // Edit menu
    public string MenuEdit           => L("Menu_Edit",                  "_Edit");
    public string MenuEditCut        => L("Menu_Edit_Cut",              "Cut");
    public string MenuEditCopy       => L("Menu_Edit_Copy",             "Copy");
    public string MenuEditPaste      => L("Menu_Edit_Paste",            "Paste");
    public string MenuEditDelete     => L("Menu_Edit_Delete",           "Delete");
    public string MenuEditSelectAll  => L("Menu_Edit_SelectAll",        "Select All");
    public string MenuEditUpper      => L("Menu_Edit_Upper",            "UPPERCASE");
    public string MenuEditLower      => L("Menu_Edit_Lower",            "lowercase");
    public string MenuEditToggleComment => L("Menu_Edit_ToggleComment", "Toggle Comment");
    public string MenuEditFoldAll    => L("Menu_Edit_FoldAll",          "Fold All");
    public string MenuEditUnfoldAll  => L("Menu_Edit_UnfoldAll",        "Unfold All");
    public string MenuEditAddBookmark => L("Menu_Edit_AddBookmark",     "Add Bookmark…");
    // Search menu
    public string MenuSearch         => L("Menu_Search",                "_Search");
    public string MenuSearchFind     => L("Menu_Search_Find",           "_Find");
    public string MenuSearchReplace  => L("Menu_Search_Replace",        "_Replace");
    public string MenuSearchFindInFiles    => L("Menu_Search_FindInFiles",    "Find in Files…");
    public string MenuSearchReplaceInFiles => L("Menu_Search_ReplaceInFiles", "Replace in Files…");
    public string MenuSearchGoTo     => L("Menu_Search_GoTo",           "_Go To Line…");
    public string MenuSearchBookmarks => L("Menu_Search_Bookmarks",     "_Bookmarks…");
    // Build menu extras
    public string MenuBuildOpenLoch  => L("Menu_Build_OpenLoch",        "Open in _Loch");
    public string MenuBuildOpenAven  => L("Menu_Build_OpenAven",        "Open in _Aven");
    public string MenuBuildOpenOutputFolder => L("Menu_Build_OpenOutputFolder", "Open last _output folder");

    private static readonly string[] LocalizedMenuProps =
    {
        nameof(MenuFileRecent), nameof(MenuViewObjectBrowser), nameof(MenuViewWorkspace),
        nameof(MenuViewDiagnostics), nameof(MenuViewRelationalMap), nameof(MenuViewStrictParser),
        nameof(MenuEdit), nameof(MenuEditCut), nameof(MenuEditCopy), nameof(MenuEditPaste),
        nameof(MenuEditDelete), nameof(MenuEditSelectAll), nameof(MenuEditUpper), nameof(MenuEditLower),
        nameof(MenuEditToggleComment), nameof(MenuEditFoldAll), nameof(MenuEditUnfoldAll),
        nameof(MenuEditAddBookmark), nameof(MenuSearch), nameof(MenuSearchFind), nameof(MenuSearchReplace),
        nameof(MenuSearchFindInFiles), nameof(MenuSearchReplaceInFiles), nameof(MenuSearchGoTo),
        nameof(MenuSearchBookmarks), nameof(MenuBuildOpenLoch), nameof(MenuBuildOpenAven),
        nameof(MenuBuildOpenOutputFolder), nameof(MenuBuildBuild), nameof(MenuBuildRebuild),
        nameof(MenuBuildCancel),
    };

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
        OutlineToolViewModel outlineTool,
        ProjectToolViewModel projectTool,
        LogToolViewModel logTool,
        SettingsToolViewModel settingsTool,
        IModelEditService? editService = null,
        ILayoutService? layout = null,
        IAppSettingsService? settings = null,
        IWorkspaceSession? session = null,
        Therion.Semantics.ISemanticRuleRunner? ruleRunner = null,
        QuickOpenProvider? quickOpen = null,
        ILogService? log = null)
    {
        _log = log;
        _quickOpen = quickOpen;
        _l = localizer;
        _language = language;
        _documents = documents;
        _ruleRunner = ruleRunner;
        _editService = editService;
        _layout = layout;
        _settings = settings;
        _session = session;
        _wordWrap = settings?.Current.EditorWordWrap ?? false; // seed without persisting
        _showWhitespace = settings?.Current.EditorShowWhitespace ?? false;   // EDIT-13 (seed, no persist)
        _showEndOfLine = settings?.Current.EditorShowEndOfLine ?? false;
        _showIndentGuides = settings?.Current.EditorShowIndentGuides ?? false;
        _showMinimap = settings?.Current.EditorShowMinimap ?? false;          // EDIT-07 (seed, no persist)
        _factory = factory;

        WorkspaceTool = workspaceTool;
        ObjectBrowserTool = objectBrowserTool;
        DiagnosticsTool = diagnosticsTool;
        CompilerOutputTool = compilerOutputTool;
        GeneratedFilesTool = generatedFilesTool;
        XviTool = xviTool;
        OutlineTool = outlineTool;
        ProjectTool = projectTool;
        LogTool = logTool;
        SettingsTool = settingsTool;
        Breadcrumb = new BreadcrumbViewModel(_documents);   // PROJ-08

        Layout = _factory.CreateLayout();
        _factory.InitLayout(Layout);

        // Bridge the document service and the dock host (kept decoupled to avoid a DI cycle).
        // OpenFileAsync resumes on a thread-pool thread (ConfigureAwait(false)), so adding
        // the document to the dock — which mutates UI-bound VisibleDockables — must be
        // marshalled to the UI thread.
        _documents.OpenInDockRequested += (_, doc) => OnUiThread(() => _factory.OpenDocument(doc));
        // Track most-recently-used documents for the Ctrl+Tab switcher (#2).
        _documents.ActiveDocumentChanged += (_, _) => OnUiThread(TouchMru);
        _factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.SetActive(doc);
        };
        _factory.DockableClosed += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.CloseDocument(doc);
        };

        _language.LanguageChanged += (_, _) => Refresh();
        if (_settings is not null)
        {
            _settings.Changed += (_, _) => OnUiThread(() => RecentFilesChanged?.Invoke(this, EventArgs.Empty));
            // Apply the persisted UI language at startup (#9).
            var lang = _settings.Current.UiLanguage;
            if (!string.IsNullOrEmpty(lang)) _language.SetLanguage(lang);
        }
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
        new OutlineToolViewModel(new OutlineViewModel()),
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel()),
        new LogToolViewModel(new LogViewModel()),
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
        new OutlineToolViewModel(new OutlineViewModel()),
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new SettingsToolViewModel(new SettingsViewModel(), new KeyboardShortcutsViewModel()));

    /// <summary>Wires the storage picker once the View is attached to a TopLevel.</summary>
    public void AttachStoragePicker(IStoragePicker picker) => _picker = picker;

    /// <summary>Restores the workspace root + active thconfig (#9) and last session's files.</summary>
    private void RestoreSession() => _ = RestoreSessionAsync(_settings?.Current);

    private async Task RestoreSessionAsync(AppSettings? s)
    {
        var files = s is { RestoreSessionOnStartup: true } ? s.LastSessionFiles : Array.Empty<string>();
        bool anyToLoad = (_session is not null && !string.IsNullOrEmpty(s?.LastWorkspaceRoot))
                         || files.Any(System.IO.File.Exists);

        // Keep the loading overlay/spinner on top for the WHOLE restore — including the (often
        // slow on big projects) workspace-graph build — not just the file reopen loop (#1/#14).
        if (anyToLoad) OnUiThread(() => IsLoading = true);
        try
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

            // Reopen last-session files, but stop once the configured time budget is exceeded so a
            // huge project stays launchable; the remaining files can be opened manually (#1).
            int timeoutSec = s?.StartupLoadTimeoutSeconds ?? 20;
            var pending = files.Where(System.IO.File.Exists).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int loaded = 0;
            foreach (var path in pending)
            {
                if (timeoutSec > 0 && sw.Elapsed.TotalSeconds > timeoutSec)
                {
                    _log?.Warning(
                        $"Startup file loading exceeded {timeoutSec}s — skipped {pending.Count - loaded} remaining " +
                        $"file(s). Open them manually, or raise the limit in Preferences ▸ Performance.");
                    break;
                }
                // Each open swaps the file into its saved tab slot (dock/float/order) when a
                // restore placeholder is holding it; otherwise it lands in the main well.
                try { await _documents.OpenFileAsync(path).ConfigureAwait(true); loaded++; }
                catch (Exception ex) { _log?.Warning($"Failed to load '{path}': {ex.Message}"); }
            }
            if (loaded > 0) _log?.Info($"Restored {loaded} file(s) from the last session.");
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

    // ---- recent files (#8) --------------------------------------------------
    /// <summary>Raised when the persisted recent-files list changes so the menu can rebuild.</summary>
    public event EventHandler? RecentFilesChanged;
    /// <summary>Recently-opened files, most-recent first (persisted across launches, #8).</summary>
    public IReadOnlyList<string> RecentFiles => _settings?.Current.RecentFiles ?? Array.Empty<string>();

    [RelayCommand]
    private async Task OpenRecent(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!System.IO.File.Exists(path)) { StatusText = $"File not found: {path}"; return; }
        try { await _documents.OpenFileAsync(path).ConfigureAwait(true); StatusText = path; }
        catch (Exception ex) { StatusText = ex.Message; }
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
    private async Task OpenThconfig()
    {
        if (_picker is null) return;
        var path = await _picker.PickOpenThconfigAsync(MenuFileOpenThconfig).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await _documents.OpenFileAsync(path).ConfigureAwait(true);
            // Make the opened thconfig the active project configuration.
            if (_session is not null) await _session.SetActiveThconfigAsync(path).ConfigureAwait(true);
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

    [RelayCommand] private void SwitchToEnglish()  => SetLanguage("en");
    [RelayCommand] private void SwitchToRomanian() => SetLanguage("ro");

    /// <summary>Applies a UI language and persists the choice for next launch (#9).</summary>
    public void SetLanguage(string culture)
    {
        _language.SetLanguage(culture);
        if (_settings is { } s && !string.Equals(s.Current.UiLanguage, culture, StringComparison.Ordinal))
            s.Save(s.Current with { UiLanguage = culture });
    }

    // ---- layout controls (#16) ----------------------------------------------
    /// <summary>Rebuilds the default dock arrangement and re-opens the current documents into it.</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        var layout = _factory.ResetToDefault();
        _factory.InitLayout(layout);
        Layout = layout;
        // The fresh layout has an empty document well — re-open the live documents into it.
        foreach (var doc in _documents.Documents.ToList())
            _factory.OpenDocument(doc);
        if (_documents.Active is { } active) _factory.OpenDocument(active);
    }

    /// <summary>Tears the active document off into its own floating window.</summary>
    [RelayCommand]
    private void FloatActiveDocument()
    {
        if (_documents.Active is { } doc)
            try { _factory.FloatDockable(doc); } catch { /* best-effort */ }
    }

    [RelayCommand] private void ToggleWorkspaceExplorer() => Activate(WorkspaceTool);
    [RelayCommand] private void ToggleDiagnostics()       => Activate(DiagnosticsTool);
    [RelayCommand] private void ToggleObjectBrowser()     => Activate(ObjectBrowserTool);
    [RelayCommand] private void ToggleOutline()           => Activate(OutlineTool); // EDIT-09
    [RelayCommand] private void ToggleProject()           => Activate(ProjectTool); // PROJ-02/03/07
    [RelayCommand] private void ToggleLog()               => Activate(LogTool);      // #3
    [RelayCommand] private void ToggleSettings()          => Activate(SettingsTool);

    /// <summary>EDIT-09 gate (compile-time flag + runtime setting) — drives the Outline menu/toolbar entry.</summary>
    public bool OutlineFeatureEnabled =>
        EditorFeatureFlags.IsEnabled(EditorFeature.Outline, _settings?.Current);

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
            doc.RequestSaveCleanup(); // EDIT-14: in-place trim/final-newline (caret-preserving) before write
            await _documents.WriteCurrentTextAsync(doc.DocumentText).ConfigureAwait(true);
            StatusText = $"Saved {doc.FilePath}";
            TriggerCompileOnSave(); // BUILD-07
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private System.Threading.CancellationTokenSource? _autoBuildCts;

    /// <summary>BUILD-07: debounced background (re)build after a save, when the setting is on.</summary>
    private async void TriggerCompileOnSave()
    {
        if (_settings?.Current.CompileOnSave != true) return;
        _autoBuildCts?.Cancel();
        var cts = _autoBuildCts = new System.Threading.CancellationTokenSource();
        try { await Task.Delay(700, cts.Token).ConfigureAwait(true); }
        catch (TaskCanceledException) { return; }
        if (Build.BuildCommand.CanExecute(null))
        {
            _log?.Info("Compile-on-save: rebuilding the project.");
            Build.BuildCommand.Execute(null);
        }
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
            // LANG-13: run the (config-driven) semantic rules over the workspace and include their
            // diagnostics. Cached per workspace snapshot so repeated refreshes don't re-run them.
            merged.AddRange(RuleDiagnostics(ws));
            // DIAG-02..06: project-wide correctness analysis (loops, blunders, fore/back,
            // collisions, dangling includes). Cached per workspace snapshot like the rules.
            merged.AddRange(ProjectAnalysisDiagnostics(ws));
            return merged.ToImmutable();
        }
        return _documents.CurrentDiagnostics;
    }

    private WorkspaceSemanticModel? _projDiagWorkspace;
    private System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> _projDiagCache;

    /// <summary>Runs the workspace correctness analysis (DIAG-02..06), cached per snapshot.</summary>
    private System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> ProjectAnalysisDiagnostics(
        WorkspaceSemanticModel ws)
    {
        if (ReferenceEquals(_projDiagWorkspace, ws) && !_projDiagCache.IsDefault) return _projDiagCache;
        try { _projDiagCache = Therion.Semantics.ProjectDiagnostics.Analyze(ws, null, System.IO.File.Exists); }
        catch { _projDiagCache = System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty; }
        _projDiagWorkspace = ws;
        return _projDiagCache;
    }

    /// <summary>Runs the semantic rule runner over <paramref name="ws"/>, caching by snapshot (LANG-13).</summary>
    private System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic> RuleDiagnostics(
        WorkspaceSemanticModel ws)
    {
        if (_ruleRunner is null) return System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty;
        if (ReferenceEquals(_ruleDiagWorkspace, ws) && !_ruleDiagCache.IsDefault) return _ruleDiagCache;
        try { _ruleDiagCache = _ruleRunner.Run(ws); }
        catch { _ruleDiagCache = System.Collections.Immutable.ImmutableArray<Therion.Core.Diagnostic>.Empty; }
        _ruleDiagWorkspace = ws;
        return _ruleDiagCache;
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
        StatusFileType = string.IsNullOrEmpty(doc.InterpretedTypeText)
            ? string.Empty
            : doc.InterpretedTypeText + (doc.IsParsed ? " · parsed" : " · not parsed");
    }

    /// <summary>Updates the caret line/column/offset on the status bar (#10).</summary>
    private void UpdateCaretStatus(Therion.Core.SourceSpan span)
    {
        // Only the active document's caret drives the status bar.
        if (!string.Equals(span.FilePath, _documents.CurrentPath, StringComparison.OrdinalIgnoreCase)) return;
        StatusCaretLine = span.Start.Line;
        StatusCaretCol = span.Start.Column;
        StatusCaretPos = span.StartOffset;
        Breadcrumb.Update(_documents.Active?.DocumentText, span.StartOffset);   // PROJ-08
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
        OnPropertyChanged(nameof(MenuFileOpenThconfig));
        OnPropertyChanged(nameof(MenuSettings));
        OnPropertyChanged(nameof(MenuHelp));
        OnPropertyChanged(nameof(MenuHelpThbook));
        OnPropertyChanged(nameof(MenuHelpAbout));
        OnPropertyChanged(nameof(MenuFileOpenFolder));
        OnPropertyChanged(nameof(MenuFileExit));
        OnPropertyChanged(nameof(MenuView));
        OnPropertyChanged(nameof(MenuViewLanguage));
        OnPropertyChanged(nameof(MenuViewLanguageEn));
        OnPropertyChanged(nameof(MenuViewLanguageRo));
        OnPropertyChanged(nameof(MenuBuild));
        foreach (var p in LocalizedMenuProps) OnPropertyChanged(p);
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
