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
using Therion.Workspace.Import;
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
    private readonly INotificationService _notifications;   // UX-07 toast/bell center
    private readonly ICrashRecoveryService? _crashRecovery; // PERF-06 safe-mode + buffer recovery
    private readonly IScriptHookService? _hooks;            // EXT-03 on-build hook
    private readonly IWorkspaceSession? _session;
    private readonly DockFactory _factory;
    private IStoragePicker? _picker;

    public ILayoutService? LayoutService => _layout;

    /// <summary>UX-07: the notification hub — bound by the toolbar bell flyout and the toast layer.</summary>
    public INotificationService Notifications => _notifications;

    /// <summary>UX-07: unread notification count for the toolbar bell badge.</summary>
    [ObservableProperty] private int _unreadNotifications;
    /// <summary>UX-07: true when there are unread notifications (drives the badge visibility).</summary>
    [ObservableProperty] private bool _hasUnreadNotifications;
    /// <summary>UX-07: true when the notification history has any entries (drives the empty-state text).</summary>
    [ObservableProperty] private bool _hasNotificationHistory;

    /// <summary>UX-07: clears the notification history (bound to the bell flyout "Clear" button).</summary>
    [RelayCommand] private void ClearNotifications() => _notifications.Clear();
    /// <summary>UX-07: resets the unread badge (called when the bell flyout is opened).</summary>
    public void MarkNotificationsRead() => _notifications.MarkAllRead();

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
    public LivePreviewToolViewModel LivePreviewTool { get; } // VIS-02
    public MapViewerToolViewModel MapViewerTool { get; }     // VIS-03/05
    public Model3DViewerToolViewModel Model3DViewerTool { get; }  // VIS-01
    public StructuralGeologyToolViewModel StructuralGeologyTool { get; }  // STRUCT-01
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

    /// <summary>PERF-02: true while the cross-file object graph is being (re)built off-thread —
    /// drives the status-bar "Indexing…" indicator.</summary>
    [ObservableProperty] private bool _isIndexing;

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

    // ----- QOL-06: selection stats -------------------------------------------
    /// <summary>True when the active editor has a non-empty selection (shows the selection group).</summary>
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private int _selectionChars;
    [ObservableProperty] private int _selectionLines;

    /// <summary>Updates the status-bar selection counters (QOL-06).</summary>
    public void SetSelectionStats(int chars, int lines)
    {
        HasSelection = chars > 0;
        SelectionChars = chars;
        SelectionLines = lines;
    }

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
    public void ShowCommandPalette() => OpenCommandPalette(string.Empty);

    /// <summary>Opens the palette pre-routed to symbol search: workspace (<c>#</c>) or document (<c>@</c>).</summary>
    public void ShowGoToSymbol(bool workspace) => OpenCommandPalette(workspace ? "#" : "@");

    private void OpenCommandPalette(string initialText)
    {
        var docs = TherionProc.AppServices.Provider.GetService(typeof(IThbookDocumentationService))
            as IThbookDocumentationService;
        var stationLimit = _settings?.Current.StationSearchLimit ?? 4000;
        var provider = new CommandPaletteProvider(this, docs, _documents, _session, Push, stationLimit);
        var palette = provider.CreatePalette(initialText);
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
    public string MenuFileRecentDirectories => L("Menu_File_RecentDirectories", "Recent _Directories");
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
        nameof(MenuFileRecent), nameof(MenuFileRecentDirectories),
        nameof(MenuViewObjectBrowser), nameof(MenuViewWorkspace),
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
        LivePreviewToolViewModel livePreviewTool,
        MapViewerToolViewModel mapViewerTool,
        Model3DViewerToolViewModel model3dViewerTool,
        StructuralGeologyToolViewModel structuralGeologyTool,
        SettingsToolViewModel settingsTool,
        IModelEditService? editService = null,
        ILayoutService? layout = null,
        IAppSettingsService? settings = null,
        IWorkspaceSession? session = null,
        Therion.Semantics.ISemanticRuleRunner? ruleRunner = null,
        QuickOpenProvider? quickOpen = null,
        ILogService? log = null,
        INotificationService? notifications = null,
        ICrashRecoveryService? crashRecovery = null,
        IScriptHookService? hooks = null)
    {
        _log = log;
        _crashRecovery = crashRecovery;
        _hooks = hooks;
        _notifications = notifications ?? new NotificationService();
        _notifications.UnreadChanged += (_, _) => OnUiThread(() =>
        {
            UnreadNotifications = _notifications.UnreadCount;
            HasUnreadNotifications = _notifications.UnreadCount > 0;
            HasNotificationHistory = _notifications.History.Count > 0;
        });
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
        LivePreviewTool = livePreviewTool;
        MapViewerTool = mapViewerTool;
        Model3DViewerTool = model3dViewerTool;
        StructuralGeologyTool = structuralGeologyTool;
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
            // VIS-01: when the 3D Viewer pane is shown, refresh its model list and auto-open the
            // default export-model output (even if stale).
            else if (e.Dockable is Docking.Model3DViewerToolViewModel m3d)
                OnUiThread(m3d.Viewer.OnPanelActivated);
            // STRUCT-01: run the structural analysis the first time its panel is shown.
            else if (e.Dockable is Docking.StructuralGeologyToolViewModel sg)
                OnUiThread(sg.Structural.OnPanelActivated);
        };
        _factory.DockableClosed += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.CloseDocument(doc);
        };

        _language.LanguageChanged += (_, _) => Refresh();
        if (_settings is not null)
        {
            _settings.Changed += (_, _) => OnUiThread(() =>
            {
                RecentFilesChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(LivePreviewEnabled));   // VIS-01/02/05 menu gates
                OnPropertyChanged(nameof(MapViewerEnabled));
                OnPropertyChanged(nameof(Model3DViewerEnabled));
                OnPropertyChanged(nameof(StructuralGeologyEnabled));   // STRUCT-01 menu gate
                ConfigureAutoSave();   // QOL-09
            });
            ConfigureAutoSave();   // QOL-09 (apply persisted mode at startup)
            // Apply the persisted UI language at startup (#9).
            var lang = _settings.Current.UiLanguage;
            if (!string.IsNullOrEmpty(lang)) _language.SetLanguage(lang);
        }
        if (_session is not null)
        {
            _session.Changed += (_, _) => OnUiThread(Refresh);          // active config / graph (#7)
            _session.CandidatesChanged += (_, _) => OnUiThread(Refresh);
            // PERF-02: mirror the background-indexing state for the status-bar indicator.
            _session.IndexingChanged += (_, _) => OnUiThread(() => IsIndexing = _session.IsIndexing);
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
        // STRUCT-01: double-click a measurement / plane → jump to its source span.
        StructuralGeologyTool.Structural.NavigateRequested += (_, span) => NavigateTo(span);
        // VIS-01: "Show in 3D" from a station/survey context menu → reveal it in the embedded viewer.
        _documents.ShowInModel3DRequested += (_, name) => OnUiThread(() =>
        {
            if (!Model3DViewerEnabled) return;
            _factory.ShowTool(Model3DViewerTool);
            Model3DViewerTool.Viewer.OnPanelActivated();   // ensure a default model is loaded first
            Model3DViewerTool.Viewer.SelectInModel(name);
        });

        Build.CompileCompleted += (_, diags) =>
        {
            var combined = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
            combined.AddRange(DiagnosticsSource());
            combined.AddRange(diags);
            Diagnostics.Load(combined.ToImmutable());
        };
        // UX-07: toast the build result with a one-click "Show output" action. The result flags
        // are set on BuildViewModel before CompileCompleted fires, so they are current here.
        Build.CompileCompleted += (_, _) => OnUiThread(() =>
        {
            void ShowOutput() => _factory.ShowCompilerOutput();
            if (Build.LastBuildSucceeded)
            {
                var warn = Build.LastBuildWarningCount;
                _notifications.Success("Build succeeded",
                    $"{Build.Artifacts.Count} artifact(s)" + (warn > 0 ? $", {warn} warning(s)." : "."),
                    "Show output", ShowOutput);
            }
            else
            {
                _notifications.Error("Build failed",
                    "The compilation reported errors.", "Show output", ShowOutput);
            }
        });
        // VIS-03: after a build, auto-load the newest rendered map into the in-app viewer.
        Build.CompileCompleted += (_, _) =>
        {
            if (_settings?.Current is { EnableMapAutoPreview: true, EnableInAppViewer: true })
                OnUiThread(() => MapViewerTool.Map.ShowLatest(Build.Artifacts.Select(a => a.Path)));
        };
        // VIS-01: after a build, auto-load the newest .lox/.3d into the embedded 3D viewer.
        Build.CompileCompleted += (_, _) =>
        {
            if (_settings?.Current is { EnableModel3DAutoPreview: true, EnableModel3DViewer: true })
                OnUiThread(() => Model3DViewerTool.Viewer.ShowLatest(Build.Artifacts));
        };
        // VIS-01: Generated Files → "View in internal 3D viewer".
        Build.View3DRequested += (_, path) => OnUiThread(() =>
        {
            if (!Model3DViewerEnabled)
            {
                _notifications.Info("3D viewer disabled",
                    "Enable the embedded 3D model viewer in Preferences ▸ Build / Visualization.");
                return;
            }
            _factory.ShowTool(Model3DViewerTool);
            Model3DViewerTool.Viewer.LoadModel(path);
        });
        // #3: the "N artifact(s)" status link surfaces + flashes the Generated Files panel.
        Build.ShowOutputsRequested += (_, _) => OnUiThread(() =>
        {
            _factory.ShowTool(GeneratedFilesTool);
            GeneratedFilesTool.Flash();
        });
        Diagnostics.NavigateRequested += (_, row) => NavigateTo(row.Span);
        Diagnostics.ScopeChanged += (_, _) => RefreshDiagnostics();
        Build.NavigateRequested += (_, span) => NavigateTo(span);
        // Surface the Compiler Output panel when a build starts (#2).
        Build.BuildStarted += (_, _) => OnUiThread(() => _factory.ShowCompilerOutput());
        // EXT-03: run the on-build script hook (active thconfig as {file}).
        Build.BuildStarted += (_, _) => _hooks?.Run(ScriptHookEvent.Build, _session?.ActiveThconfig?.FullPath);

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
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel(), new DataAnalyticsViewModel(), new LeadsViewModel(), new TodoScanViewModel(), new ProjectMetadataViewModel(), new MediaManagerViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new LivePreviewToolViewModel(new LivePreviewViewModel()),
        new MapViewerToolViewModel(new MapViewerViewModel()),
        new Model3DViewerToolViewModel(new Model3DViewerViewModel()),
        new StructuralGeologyToolViewModel(new StructuralGeologyViewModel()),
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
        new ProjectToolViewModel(new ProjectDashboardViewModel(), new SurveyTreeViewModel(), new ProjectAuditViewModel(), new DataAnalyticsViewModel(), new LeadsViewModel(), new TodoScanViewModel(), new ProjectMetadataViewModel(), new MediaManagerViewModel()),
        new LogToolViewModel(new LogViewModel()),
        new LivePreviewToolViewModel(new LivePreviewViewModel()),
        new MapViewerToolViewModel(new MapViewerViewModel()),
        new Model3DViewerToolViewModel(new Model3DViewerViewModel()),
        new StructuralGeologyToolViewModel(new StructuralGeologyViewModel()),
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
                try
                {
                    await _documents.OpenFileAsync(path).ConfigureAwait(true); loaded++;
                    // QOL-10: restore the caret offset so the tab reopens where it was left.
                    if (s?.SessionCaretOffsets is { } carets && carets.TryGetValue(path, out var off) && off > 0 &&
                        _documents.Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.OrdinalIgnoreCase)) is { } reopened)
                        reopened.SavedCaretOffset = off;
                }
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

        // UX-05: re-create the floated tool/document windows from the last session. Posted at
        // Background priority AFTER the placeholder removal above (same priority = FIFO), so the
        // restored documents already exist and can be resolved by id and floated.
        // PERF-06: SAFE MODE — after a crash, skip the float-window restore so a bad saved layout
        // can't bring the app down on relaunch.
        if (_crashRecovery is not { PreviousRunCrashed: true })
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _factory.RestoreFloatWindows(_layout?.Current.FloatWindows),
                Avalonia.Threading.DispatcherPriority.Background);
        else
            _log?.Warning("Started in safe mode after an unclean shutdown — floated-window layout was not restored.");

        // PERF-06: offer to recover unsaved buffers left by a crashed run.
        OfferCrashRecovery();
    }

    /// <summary>PERF-06: if a crashed run left unsaved buffers, surface a recovery notification.</summary>
    private void OfferCrashRecovery()
    {
        if (_crashRecovery is null) return;
        var recoverable = _crashRecovery.GetRecoverable();
        if (recoverable.Count == 0) return;
        OnUiThread(() => _notifications.Warning(
            "Unsaved changes recovered",
            $"{recoverable.Count} file(s) had unsaved changes when the app last closed unexpectedly.",
            "Restore", () => _ = RecoverBuffersAsync(recoverable)));
    }

    private async Task RecoverBuffersAsync(System.Collections.Generic.IReadOnlyList<RecoveredBuffer> buffers)
    {
        foreach (var b in buffers)
        {
            try
            {
                if (System.IO.File.Exists(b.OriginalPath))
                {
                    await _documents.OpenFileAsync(b.OriginalPath).ConfigureAwait(true);
                    // Apply the recovered text on top of the on-disk content → the editor reopens
                    // dirty with the unsaved work, ready to save.
                    if (_documents.Documents.FirstOrDefault(d =>
                            string.Equals(d.FilePath, b.OriginalPath, StringComparison.OrdinalIgnoreCase)) is { } doc)
                        doc.DocumentText = b.Text;
                }
                else
                {
                    _documents.OpenTextDocument(b.OriginalPath, b.Text);
                }
            }
            catch (Exception ex) { _log?.Warning($"Failed to recover '{b.OriginalPath}': {ex.Message}"); }
        }
        _crashRecovery?.ClearRecovery();
        StatusText = $"Recovered {buffers.Count} unsaved file(s).";
    }

    /// <summary>Records the open files + workspace root/active thconfig for next launch (#9).</summary>
    public void PersistSession()
    {
        if (_settings is null) return;
        var paths = new System.Collections.Generic.List<string>();
        // QOL-10: also remember each tab's caret offset so it restores where the user left off.
        var carets = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in _documents.Documents)
        {
            if (string.IsNullOrEmpty(doc.FilePath) || !System.IO.File.Exists(doc.FilePath)) continue;
            paths.Add(doc.FilePath);
            if (doc.SavedCaretOffset > 0) carets[doc.FilePath] = doc.SavedCaretOffset;
        }
        try
        {
            _settings.Save(_settings.Current with
            {
                LastSessionFiles = paths,
                SessionCaretOffsets = carets,
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
    /// <summary>Pinned recent files (QOL-05).</summary>
    public IReadOnlyList<string> PinnedRecentFiles => _settings?.Current.PinnedRecentFiles ?? Array.Empty<string>();
    /// <summary>Recently-opened working directories, most-recent first (File ▸ Recent Directories).</summary>
    public IReadOnlyList<string> RecentDirectories => _settings?.Current.RecentDirectories ?? Array.Empty<string>();

    /// <summary>QOL-05: pin a recent file so it stays at the top and survives "Clear Recent".</summary>
    [RelayCommand]
    private void PinRecent(string? path)
    {
        if (_settings is null || string.IsNullOrEmpty(path)) return;
        var s = _settings.Current;
        if (s.PinnedRecentFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        var pinned = new List<string>(s.PinnedRecentFiles) { path };
        _settings.Save(s with { PinnedRecentFiles = pinned });
    }

    /// <summary>QOL-05: unpin a previously-pinned recent file.</summary>
    [RelayCommand]
    private void UnpinRecent(string? path)
    {
        if (_settings is null || string.IsNullOrEmpty(path)) return;
        var s = _settings.Current;
        var pinned = s.PinnedRecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pinned.Count == s.PinnedRecentFiles.Count) return;
        _settings.Save(s with { PinnedRecentFiles = pinned });
    }

    /// <summary>QOL-05: clears the (unpinned) recent-files list.</summary>
    [RelayCommand]
    private void ClearRecent()
    {
        if (_settings is null) return;
        _settings.Save(_settings.Current with { RecentFiles = Array.Empty<string>() });
    }

    /// <summary>Opens a directory from the File ▸ Recent Directories submenu as the workspace folder.</summary>
    [RelayCommand]
    private async Task OpenRecentDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!System.IO.Directory.Exists(path)) { StatusText = $"Folder not found: {path}"; return; }
        try { await _documents.OpenFolderAsync(path).ConfigureAwait(true); StatusText = _documents.CurrentPath ?? path; }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    /// <summary>Clears the recent-directories list.</summary>
    [RelayCommand]
    private void ClearRecentDirectories()
    {
        if (_settings is null) return;
        _settings.Save(_settings.Current with { RecentDirectories = Array.Empty<string>() });
    }

    /// <summary>UX-10: reopen the most-recently-closed tab (Ctrl+Shift+T).</summary>
    [RelayCommand]
    private async Task ReopenClosedTab()
    {
        try
        {
            if (!await _documents.ReopenLastClosedAsync().ConfigureAwait(true))
                StatusText = "No recently-closed tab to reopen.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

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

    // ───────── Tools: import / GIS export / surface / scaffold (IMP-01, GIS-01/03, TH2-04) ─────────

    /// <summary>IMP-01: import a Survex (.svx) or Compass (.dat) file → a new .th.</summary>
    [RelayCommand]
    private async Task ImportSurvey()
    {
        if (_picker is null) return;
        var src = await _picker.PickOpenFileAsync("Import Survex (.svx) or Compass (.dat)").ConfigureAwait(true);
        if (string.IsNullOrEmpty(src)) return;
        try
        {
            var text = System.IO.File.ReadAllText(src);
            var ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
            var th = ext switch
            {
                ".dat" => CompassImporter.Import(text),
                _ => SurvexImporter.Import(text),   // .svx and anything else → Survex
            };
            var suggested = System.IO.Path.GetFileNameWithoutExtension(src) + ".th";
            var outPath = await _picker.PickSaveFileAsync("Save imported Therion file", suggested).ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, th);
            await _documents.OpenFileAsync(outPath).ConfigureAwait(true);
            StatusText = $"Imported {System.IO.Path.GetFileName(src)} → {System.IO.Path.GetFileName(outPath)}";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"Import failed: {ex.Message}"); _notifications.Error("Import failed", ex.Message); }
    }

    /// <summary>GIS-01: export entrances / fixed points in the project CRS. Format via CommandParameter.</summary>
    [RelayCommand]
    private async Task ExportGis(string? format)
    {
        if (_picker is null) return;
        var model = _documents.Workspace;
        if (model is null || model.PerFile.Count == 0) { StatusText = "No project open to export."; return; }
        var fmt = (format ?? "csv").ToLowerInvariant() switch
        {
            "kml" => GisFormat.Kml,
            "gpx" => GisFormat.Gpx,
            "geojson" => GisFormat.GeoJson,
            _ => GisFormat.Csv,
        };
        try
        {
            var text = GisExport.Export(model, fmt);
            var suggested = "entrances" + GisExport.FileExtension(fmt);
            var outPath = await _picker.PickSaveFileAsync("Export entrances / fixed points", suggested).ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, text);
            StatusText = $"Exported {GisExport.CollectPoints(model).Count} point(s) → {System.IO.Path.GetFileName(outPath)}";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"GIS export failed: {ex.Message}"); _notifications.Error("GIS export failed", ex.Message); }
    }

    /// <summary>PUB-02: export the stations or shots table to CSV / Markdown / HTML / LaTeX.
    /// CommandParameter is "&lt;which&gt;|&lt;format&gt;", e.g. "shots|html".</summary>
    [RelayCommand]
    private async Task ExportTable(string? spec)
    {
        if (_picker is null) return;
        var model = _documents.Workspace;
        if (model is null || model.PerFile.Count == 0) { StatusText = "No project open to export."; return; }

        var parts = (spec ?? "shots|csv").Split('|');
        var which = parts[0].ToLowerInvariant();
        var format = (parts.Length > 1 ? parts[1] : "csv").ToLowerInvariant();

        var (headers, rows) = which == "stations"
            ? Therion.Semantics.SurveyTables.StationsTable(model)
            : Therion.Semantics.SurveyTables.ShotsTable(model);

        var (text, ext) = format switch
        {
            "markdown" => (DataExport.ToMarkdown(headers, rows), ".md"),
            "html"     => (HtmlDocument($"{which} table", DataExport.ToHtml(headers, rows)), ".html"),
            "latex"    => (DataExport.ToLatex(headers, rows), ".tex"),
            _          => (DataExport.ToCsv(headers, rows), ".csv"),
        };
        try
        {
            var outPath = await _picker.PickSaveFileAsync($"Export {which} table", which + ext).ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, text);
            StatusText = $"Exported {rows.Count} {which} row(s) → {System.IO.Path.GetFileName(outPath)}";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"Table export failed: {ex.Message}"); _notifications.Error("Table export failed", ex.Message); }
    }

    /// <summary>MEDIA-04: import GPX waypoints/track points → a Therion survey of fixed stations.</summary>
    [RelayCommand]
    private async Task ImportGpx()
    {
        if (_picker is null) return;
        var src = await _picker.PickOpenFileAsync("Import GPX waypoints").ConfigureAwait(true);
        if (string.IsNullOrEmpty(src)) return;
        try
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(src);
            var th = Therion.Workspace.Import.GpxImporter.ToTherion(System.IO.File.ReadAllText(src), name);
            var outPath = await _picker.PickSaveFileAsync("Save fixed-stations Therion file", name + "_gps.th").ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, th);
            await _documents.OpenFileAsync(outPath).ConfigureAwait(true);
            StatusText = $"Imported GPX → {System.IO.Path.GetFileName(outPath)}";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"GPX import failed: {ex.Message}"); _notifications.Error("GPX import failed", ex.Message); }
    }

    /// <summary>PUB-01: generate a one-click HTML survey report and open it.</summary>
    [RelayCommand]
    private async Task GenerateReport()
    {
        if (_picker is null) return;
        var model = _documents.Workspace;
        if (model is null || model.PerFile.Count == 0) { StatusText = "No project open to report on."; return; }

        var name = _session?.ActiveThconfig is { } a
            ? System.IO.Path.GetFileNameWithoutExtension(a.FullPath)
            : "survey";
        try
        {
            var html = Therion.Semantics.SurveyReport.BuildHtml(model, name);
            var outPath = await _picker.PickSaveFileAsync("Save survey report", name + "-report.html").ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, html);
            StatusText = $"Report written → {System.IO.Path.GetFileName(outPath)}";
            try { (AppServices.Provider.GetService(typeof(Therion.Build.IShellOpener)) as Therion.Build.IShellOpener)?.Open(outPath); }
            catch { /* opening is best-effort */ }
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"Report failed: {ex.Message}"); _notifications.Error("Report failed", ex.Message); }
    }

    /// <summary>Wraps an HTML table fragment in a minimal standalone document.</summary>
    private static string HtmlDocument(string title, string bodyHtml) =>
        $"<!doctype html>\n<html><head><meta charset=\"utf-8\"><title>{System.Net.WebUtility.HtmlEncode(title)}</title>\n" +
        "<style>body{font-family:sans-serif}table{border-collapse:collapse}th,td{border:1px solid #ccc;padding:3px 8px;text-align:left}</style>\n" +
        $"</head><body>\n<h2>{System.Net.WebUtility.HtmlEncode(title)}</h2>\n{bodyHtml}</body></html>\n";

    /// <summary>GIS-03: convert an ESRI ASCII grid (.asc) into a Therion surface .th.</summary>
    [RelayCommand]
    private async Task ImportDemSurface()
    {
        if (_picker is null) return;
        var src = await _picker.PickOpenFileAsync("Import DEM (ESRI ASCII .asc) as surface").ConfigureAwait(true);
        if (string.IsNullOrEmpty(src)) return;
        try
        {
            var surface = SurfaceFromDem.FromEsriAscii(System.IO.File.ReadAllText(src));
            var th = $"survey {System.IO.Path.GetFileNameWithoutExtension(src)}_surface\n{surface}endsurvey\n";
            var suggested = System.IO.Path.GetFileNameWithoutExtension(src) + "_surface.th";
            var outPath = await _picker.PickSaveFileAsync("Save Therion surface file", suggested).ConfigureAwait(true);
            if (string.IsNullOrEmpty(outPath)) return;
            System.IO.File.WriteAllText(outPath, th);
            await _documents.OpenFileAsync(outPath).ConfigureAwait(true);
            StatusText = $"Generated surface from {System.IO.Path.GetFileName(src)}";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"DEM import failed: {ex.Message}"); _notifications.Error("DEM import failed", ex.Message); }
    }

    /// <summary>TH2-04: scaffold a new .th2 scrap stub; the scrap id is taken from the chosen filename.</summary>
    [RelayCommand]
    private async Task NewScrapScaffold()
    {
        if (_picker is null) return;
        var outPath = await _picker.PickSaveFileAsync("Create new .th2 scrap", "scrap1.th2").ConfigureAwait(true);
        if (string.IsNullOrEmpty(outPath)) return;
        try
        {
            var id = System.IO.Path.GetFileNameWithoutExtension(outPath);
            System.IO.File.WriteAllText(outPath, Th2Scaffold.NewScrap(id));
            await _documents.OpenFileAsync(outPath).ConfigureAwait(true);
            var inputLine = Th2Scaffold.InputLine(System.IO.Path.GetFileName(outPath));
            ClipboardHelper.SetText(inputLine);
            StatusText = $"Created scrap '{id}'. Copied '{inputLine}' to clipboard — paste it into your .th.";
        }
        catch (Exception ex) { StatusText = ex.Message; _log?.Warning($"Scaffold failed: {ex.Message}"); _notifications.Error("Scaffold failed", ex.Message); }
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
    [RelayCommand] private void ToggleLivePreview()       => Activate(LivePreviewTool); // VIS-02
    [RelayCommand] private void ToggleMapViewer()         => Activate(MapViewerTool);    // VIS-05
    [RelayCommand] private void ToggleModel3DViewer()     => _factory.ShowTool(Model3DViewerTool); // VIS-01 (may be off-by-default → add on demand)
    [RelayCommand] private void ToggleStructuralGeology() => _factory.ShowToolInDocuments(StructuralGeologyTool); // STRUCT-01 (big central panel, on demand)
    [RelayCommand] private void ToggleSettings()          => Activate(SettingsTool);

    /// <summary>VIS-01/02/05 gates — drive the View-menu entries (hidden when the feature is off).</summary>
    public bool LivePreviewEnabled => _settings?.Current.EnableLivePreview ?? true;
    public bool MapViewerEnabled   => _settings?.Current.EnableInAppViewer ?? true;
    public bool Model3DViewerEnabled => _settings?.Current.EnableModel3DViewer ?? false;
    public bool StructuralGeologyEnabled => _settings?.Current.EnableStructuralGeology ?? false;

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

    // ---- auto-save (QOL-09) -------------------------------------------------
    private Avalonia.Threading.DispatcherTimer? _autoSaveTimer;

    /// <summary>Applies the current auto-save setting: starts/stops the periodic timer.</summary>
    private void ConfigureAutoSave()
    {
        var mode = _settings?.Current.AutoSave ?? AutoSaveMode.Off;
        if (mode == AutoSaveMode.AfterDelay)
        {
            int sec = Math.Max(5, _settings?.Current.AutoSaveDelaySeconds ?? 30);
            _autoSaveTimer ??= CreateAutoSaveTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(sec);
            _autoSaveTimer.Start();
        }
        else _autoSaveTimer?.Stop();
    }

    private Avalonia.Threading.DispatcherTimer CreateAutoSaveTimer()
    {
        var t = new Avalonia.Threading.DispatcherTimer();
        t.Tick += async (_, _) => await SaveAllDirtyAsync().ConfigureAwait(true);
        return t;
    }

    /// <summary>QOL-09: persists every dirty document with a real on-disk path.</summary>
    public async Task SaveAllDirtyAsync()
    {
        foreach (var doc in _documents.Documents.ToList())
        {
            if (!doc.IsDirty) continue;
            try { await _documents.SaveDocumentAsync(doc).ConfigureAwait(true); }
            catch (Exception ex) { _log?.Warning($"Auto-save failed for '{doc.FilePath}': {ex.Message}"); }
        }
    }

    /// <summary>QOL-09: auto-saves on focus loss when that mode is selected (called by the view).</summary>
    public void AutoSaveOnFocusLoss()
    {
        if (_settings?.Current.AutoSave == AutoSaveMode.OnFocusLoss) _ = SaveAllDirtyAsync();
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
