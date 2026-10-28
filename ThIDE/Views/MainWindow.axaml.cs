using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> OpenableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".th", ".th2", ".thconfig", ".thc", "" };

    private IKeyboardShortcutService? _shortcuts;
    // Full screen is a view-only concern (WindowState), so it has no ViewModel command to bind.
    private ICommand? _toggleFullScreenCommand;
    private ILayoutService? _layout;
    private IGlobalHotkeyService? _globalHotkey;
    private ICrashRecoveryService? _crashRecovery;
    private IMcpHostService? _mcpHost;

    // System-wide build hotkey (Ctrl+Alt+B): triggers a compile even when the app isn't
    // focused, marshalled onto the UI thread (#3). No-op on platforms without support.
    private void AttachGlobalHotkey(MainWindowViewModel vm)
    {
        _globalHotkey = AppServices.Provider.GetService<IGlobalHotkeyService>();
        if (_globalHotkey is null) return;
        _globalHotkey.BuildHotkeyPressed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (vm.Build.BuildCommand.CanExecute(null)) vm.Build.BuildCommand.Execute(null);
            });
        _globalHotkey.Start();
    }

    public MainWindow()
    {
        InitializeComponent();
        // Ctrl+Tab document switching is registered in the tunnel phase so it fires before any
        // focused child (the dock tab-strip, toolbar, menu, editor) can swallow it — otherwise it
        // only works while the editor is focused and dies once focus lands on the toolbar (#4).
        AddHandler(KeyDownEvent, OnTunnelKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Ctrl release commits the switcher selection (Alt+Tab style); also tunnelled.
        AddHandler(KeyUpEvent, OnTunnelKeyUp, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.AttachStoragePicker(new AvaloniaStoragePicker(this));
                AttachLayout(vm);
                AttachKeyboardShortcuts(vm);
                vm.RecentFilesChanged += (_, _) => { BuildRecentMenu(vm); BuildRecentDirectoriesMenu(vm); };
                vm.ConfirmLoadFolderRequested += (_, path) => _ = ConfirmLoadFolderAsync(vm, path);
                // Command-palette (#4) view actions: open windows / settings.
                vm.ShowPreferencesRequested += (_, section) => _ = OpenPreferences(section);
                vm.ShowAboutRequested       += (_, _) => OnAboutClick(this, new Avalonia.Interactivity.RoutedEventArgs());
                vm.ShowThbookRequested      += (_, _) => OnOpenThbook(this, new Avalonia.Interactivity.RoutedEventArgs());
                vm.ShowBookmarksRequested   += (_, _) => OnBookmarksClick(this, new Avalonia.Interactivity.RoutedEventArgs());
                vm.ShowRelationalMapRequested += (_, _) => OnRelationalMapClick(this, new Avalonia.Interactivity.RoutedEventArgs());
                // Command-palette entries for the Tools calculators + Help ▸ Debug Info (window-hosted).
                vm.ShowToolWindowRequested += (_, key) =>
                {
                    var args = new Avalonia.Interactivity.RoutedEventArgs();
                    switch (key)
                    {
                        case "unit": OnUnitConverter(this, args); break;
                        case "coord": OnCoordinateConverter(this, args); break;
                        case "declination": OnDeclinationCalculator(this, args); break;
                        case "debuginfo": OnDebugInfoClick(this, args); break;
                    }
                };
                vm.Build.QuickExportRequested += (_, _) => _ = ShowQuickExportAsync(vm);
                vm.ScaffoldProjectRequested += (_, mode) => _ = ShowScaffoldProjectAsync(vm, mode);
                vm.LinkSurveyRequested += (_, _) => _ = ShowLinkSurveyAsync(null, 0);
                vm.NewFileRequested += (_, folder) => _ = ShowNewFileAsync(folder);
                vm.NewProjectRequested += (_, _) => _ = ShowNewProjectAsync();
                vm.NewTripRequested += (_, _) => _ = ShowNewTripAsync();
                vm.BatchBuildRequested += (_, entries) => _ = ShowBatchBuildAsync(vm, entries);
                vm.Diagnostics.FixRequested += (_, d) => _ = FixOneAsync(d);
                vm.SyncMapsRequested += (_, _) => _ = SyncMapsAsync();
                vm.SuggestEquatesRequested += (_, _) => _ = SuggestEquatesAsync();
                vm.FormatAllRequested += (_, _) => _ = FormatAllAsync();
                if (AppServices.Provider.GetService<IMacroService>() is MacroService macroService)
                {
                    macroService.AskVariables = m => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AskMacroVariablesAsync(m));
                    macroService.ConfirmApply = (m, p) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmMacroAsync(m, p));
                }
                if (AppServices.Provider.GetService<IIncomingFolderService>() is { } incoming)
                {
                    incoming.ImportRequested += (_, path) => Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ImportArrivedAsync(vm, path));
                    incoming.Changed += (_, _) => vm.RefreshWatchIncoming();
                }
                vm.Diagnostics.FixAllRequested += (_, code) => _ = FixAllAsync(vm, code);
                ThIDE.Editor.TherionTextEditor.LinkSurveyRequested += (_, req) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ShowLinkSurveyAsync(req.FilePath, req.Caret));
                BuildRecentMenu(vm);
                BuildRecentDirectoriesMenu(vm);
                // The layout rendered without crashing — clear the crash sentinel so the next
                // launch trusts it (deferred so the dock has finished materializing).
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => vm.Factory.ConfirmLayoutLoaded(),
                    Avalonia.Threading.DispatcherPriority.Background);
                StartAutosave();
                AttachGlobalHotkey(vm);
                AttachNotifications(vm);                 // toast layer
                StartModalDesyncWatchdog();              // un-freeze a window left disabled by a modal
                OpenStartupFileArgs(vm);                 // "open with" / file association
                // mirror the active editor's selection stats into the status bar.
                ThIDE.Editor.TherionTextEditor.SelectionStatsChanged += (_, s) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetSelectionStats(s.Chars, s.Lines));
            }
            try
            {
                if (AppServices.Provider.GetService<IDocumentService>() is { } docs)
                {
                    docs.FindReferencesRequested += (_, term) => ShowFindReferences(term);
                    docs.RenameSymbolRequested += (_, args) => _ = HandleRenameSymbolAsync(args.Raw, args.Kind);
                    docs.FindAllReferencesRequested += (_, args) => _ = HandleFindAllReferencesAsync(args.Raw, args.Kind);
                }
                AttachSnippetCatalog();
                AttachAutomationPanel();
                _crashRecovery = AppServices.Provider.GetService<ICrashRecoveryService>();
                _mcpHost = AppServices.Provider.GetService<IMcpHostService>();
            }
            catch { /* design-time / no container */ }
        };

        // Persist when focus leaves the app — covers a debugger stop that never fires Closing.
        Deactivated += (_, _) =>
        {
            (DataContext as MainWindowViewModel)?.AutoSaveOnFocusLoss();
            PersistAll();
            PersistRecoveryBuffers();
        };
        // A clean close clears the crash sentinel + recovery buffers so the next launch
        // doesn't enter safe mode or offer stale recovery.
        Closing += (_, _) =>
        {
            _autosaveTimer?.Stop();
            PersistAll();
            _crashRecovery?.MarkCleanShutdown();
            _globalHotkey?.Dispose();
            _modalWatchdog?.Dispose();
            // Remove the discovery file at once (no host should reconnect to a dying port) and stop the
            // listener without blocking the UI thread — a blocking wait can deadlock Kestrel's drain
            // against an in-flight request that is itself marshalling to this thread (code review).
            try { _mcpHost?.RequestShutdown(); } catch { /* exiting */ }
        };

        // Drag-and-drop file open (#17).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ----- recent files menu (#8) ----------------------------------------
    // Recent files are grouped per type (thconfig / th / th2 / other), capped at 8 per
    // group, with a separator between groups. Rebuilt whenever the persisted list changes.

    private static readonly (string Label, string[] Exts)[] RecentGroups =
    {
        ("Configurations", new[] { ".thconfig", ".thc" }),
        ("Surveys",        new[] { ".th" }),
        ("Sketches",       new[] { ".th2" }),
        ("Other",          Array.Empty<string>()),
    };

    private void BuildRecentMenu(MainWindowViewModel vm)
    {
        if (this.FindControl<MenuItem>("RecentMenu") is not { } menu) return;
        var items = new List<Control>();

        // pinned files first, in their own group; each can be unpinned.
        var pinned = vm.PinnedRecentFiles;
        if (pinned.Count > 0)
        {
            foreach (var path in pinned) items.Add(RecentItem(vm, path, isPinned: true));
            items.Add(new Separator());
        }

        var recent = vm.RecentFiles;
        for (int g = 0; g < RecentGroups.Length; g++)
        {
            var (_, exts) = RecentGroups[g];
            var groupFiles = new List<string>();
            foreach (var path in recent)
            {
                if (pinned.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;   // shown in pinned group
                var ext = Path.GetExtension(path);
                bool inGroup = exts.Length == 0
                    ? !RecentGroupedExts.Contains(ext)         // "Other": anything not in a named group
                    : Array.Exists(exts, x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
                if (inGroup) groupFiles.Add(path);
                if (groupFiles.Count == 8) break;              // cap per type (#8)
            }
            if (groupFiles.Count == 0) continue;

            if (items.Count > 0 && items[^1] is not Separator) items.Add(new Separator());
            foreach (var path in groupFiles) items.Add(RecentItem(vm, path, isPinned: false));
        }

        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
        else
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = "Clear Recent Files", Command = vm.ClearRecentCommand });
        }
        menu.ItemsSource = items;
    }

    // Closes the top menu after a recent-file/directory item is chosen. The open commands run
    // asynchronously, which suppresses the menu's normal click-to-close, leaving it open as though
    // the click did nothing.
    private void CloseMainMenu(object? sender, RoutedEventArgs e) =>
        this.FindControl<Menu>("MainMenu")?.Close();

    // a recent/pinned file entry — opens on click; right-click pins or unpins it.
    private MenuItem RecentItem(MainWindowViewModel vm, string path, bool isPinned)
    {
        var item = new MenuItem
        {
            Header = (isPinned ? "📌 " : string.Empty) + Path.GetFileName(path),
            Command = vm.OpenRecentCommand,
            CommandParameter = path,
            [ToolTip.TipProperty] = path,
        };
        // The open command runs async, so the menu doesn't auto-close on click and lingers open as
        // if nothing happened; close it explicitly once the item is invoked.
        item.Click += CloseMainMenu;
        var toggle = new MenuItem
        {
            Header = isPinned ? "Unpin" : "Pin",
            Command = isPinned ? vm.UnpinRecentCommand : vm.PinRecentCommand,
            CommandParameter = path,
        };
        item.ContextMenu = new ContextMenu { ItemsSource = new[] { toggle } };
        return item;
    }

    private static readonly HashSet<string> RecentGroupedExts =
        new(StringComparer.OrdinalIgnoreCase) { ".thconfig", ".thc", ".th", ".th2" };

    // ----- recent directories menu --------------------------------------------
    // Working directories (workspace roots) the user has opened, most-recent first. Each entry
    // re-opens that folder as the workspace; the full path is shown on hover.

    private void BuildRecentDirectoriesMenu(MainWindowViewModel vm)
    {
        if (this.FindControl<MenuItem>("RecentDirectoriesMenu") is not { } menu) return;
        var items = new List<Control>();

        foreach (var dir in vm.RecentDirectories)
        {
            var item = new MenuItem
            {
                Header = RecentDirectoryLabel(dir),
                Command = vm.OpenRecentDirectoryCommand,
                CommandParameter = dir,
                [ToolTip.TipProperty] = dir,
            };
            item.Click += CloseMainMenu;   // async open → close the lingering menu explicitly
            items.Add(item);
        }

        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(no recent directories)", IsEnabled = false });
        else
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = "Clear Recent Directories", Command = vm.ClearRecentDirectoriesCommand });
        }
        menu.ItemsSource = items;
    }

    // A directory's display label: the leaf folder name, plus its parent for context (e.g.
    // "caves › vladusca"). Falls back to the raw path for drive roots.
    private static string RecentDirectoryLabel(string dir)
    {
        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(dir);
            var leaf = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(leaf)) return dir;   // a drive root such as "C:\"
            var parent = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? string.Empty);
            return string.IsNullOrEmpty(parent) ? leaf : $"{parent} › {leaf}";
        }
        catch { return dir; }
    }

    // ----- bookmarks window (B3) -----------------------------------------

    private BookmarksWindow? _bookmarksWindow;

    private void OnBookmarksClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_bookmarksWindow is null || !_bookmarksWindow.IsVisible)
        {
            _bookmarksWindow = new BookmarksWindow { ShowInTaskbar = false };
            _bookmarksWindow.Show(this);
        }
        else
        {
            _bookmarksWindow.Activate();
        }
    }

    // ----- relational map window (object-relational tree diagram) ---------

    private RelationalMapWindow? _relationalMapWindow;

    // Help ▸ About (#1).
    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IThbookDocumentationService? thbook = null;
        try { thbook = AppServices.Provider.GetService<IThbookDocumentationService>(); } catch { }
        await new AboutWindow(thbook).ShowDialog(this);
    }

    // Help ▸ Debug Info (#2): a copyable diagnostic report for issue reports.
    private async void OnDebugInfoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => await new DebugInfoWindow().ShowDialog(this);

    // Help ▸ Therion Book: open the bundled thbook PDF at page 1 (#1).
    private void OnOpenThbook(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { AppServices.Provider.GetService<IThbookDocumentationService>()?.OpenAtPage(1); } catch { }
    }

    private void OnRelationalMapClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_relationalMapWindow is { } w && w.IsVisible) { w.Activate(); return; }

        IDocumentService? docs = null;
        IWorkspaceSession? session = null;
        try
        {
            docs = AppServices.Provider.GetService<IDocumentService>();
            session = AppServices.Provider.GetService<IWorkspaceSession>();
        }
        catch { /* design-time / no container */ }
        if (docs is null) return;

        var vm = new RelationalMapViewModel(docs, session);
        _relationalMapWindow = new RelationalMapWindow { DataContext = vm };
        _relationalMapWindow.Closed += (_, _) => _relationalMapWindow = null;
        _relationalMapWindow.Show(this);
    }

    // ----- Edit menu → active editor (#11) --------------------------------
    // The shell Edit/Search menus act on the most-recently focused editor.

    // Targets the ACTIVE document's editor first (so Find/Edit work right after a file is
    // opened via navigation, before it's been clicked into), falling back to the last focused
    // editor for design-time / no-active-document cases (#9).
    private static ThIDE.Editor.TherionTextEditor? ActiveEditor
    {
        get
        {
            try
            {
                var path = AppServices.Provider.GetService<IDocumentService>()?.Active?.FilePath;
                if (ThIDE.Editor.TherionTextEditor.ForPath(path) is { } ed) return ed;
            }
            catch { /* design-time / no container */ }
            return ThIDE.Editor.TherionTextEditor.LastFocused;
        }
    }

    private void OnEditCut(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuCut();
    private void OnEditCopy(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuCopy();
    private void OnEditPaste(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuPaste();
    private void OnEditDelete(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuDelete();
    private void OnEditSelectAll(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuSelectAll();
    private void OnEditUpper(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuUpperCase();
    private void OnEditLower(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuLowerCase();
    private void OnEditToggleComment(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuToggleComment();
    private void OnEditEncloseRegion(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuEncloseInRegion();
    private void OnEditFoldAll(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuFoldAll();
    private void OnEditUnfoldAll(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuUnfoldAll();
    private void OnEditAddBookmark(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuAddBookmark();

    // ----- Search menu (#12) ----------------------------------------------

    private void OnSearchFind(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuFind();
    private void OnSearchReplace(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ActiveEditor?.MenuReplace();
    private void OnSearchFindInFiles(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ShowSearch();
    private void OnSearchReplaceInFiles(object? s, Avalonia.Interactivity.RoutedEventArgs e) => ShowReplace();

    private async void OnSearchGoTo(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ActiveEditor is not { } ed) return;
        var input = await new InputDialog("Go To Line",
            $"Line number (1–{ed.LineCount}):", ed.CurrentLine.ToString()).ShowAsync(this);
        if (int.TryParse(input, out var line)) ed.GoToLine(line);
    }

    // ----- full screen (#8) ----------------------------------------------

    private void OnToggleFullScreen(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen() =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    // Go-to-File typed a directory path: confirm, then load it as the active workspace (#3).
    private async System.Threading.Tasks.Task ConfirmLoadFolderAsync(MainWindowViewModel vm, string path)
    {
        var ok = new Button { Content = ThIDE.Resources.Tr.Get("Ctx_Open"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = ThIDE.Resources.Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        var dialog = new Window
        {
            Title = ThIDE.Resources.Tr.Get("Dlg_LoadFolderTitle"),
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = ThIDE.Resources.Tr.Get("Dlg_LoadFolderMsg") + "\n\n" + path, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            },
        };
        Avalonia.Automation.AutomationProperties.SetAutomationId(dialog, "LoadFolder.Window");
        Avalonia.Automation.AutomationProperties.SetAutomationId(ok, "LoadFolder.Open");
        Avalonia.Automation.AutomationProperties.SetAutomationId(cancel, "LoadFolder.Cancel");
        bool confirmed = false;
        ok.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        if (confirmed) await vm.OpenFolderPathAsync(path);
    }

    // Global Ctrl+Tab / Ctrl+Shift+Tab editor-document switcher overlay (#2/#4). Tunnel phase =
    // focus-agnostic, so it works whether the editor, toolbar, menu or a panel has focus.
    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Escape dismisses the switcher without changing the active document.
        if (e.Key == Key.Escape && vm.DocumentSwitcher.IsOpen)
        {
            vm.CancelDocumentSwitcher();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            bool forward = (e.KeyModifiers & KeyModifiers.Shift) == 0;
            vm.ShowOrAdvanceDocumentSwitcher(forward);
            e.Handled = true;
        }
    }

    // Releasing Ctrl commits the switcher's highlighted document (Alt+Tab style).
    private void OnTunnelKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
            (DataContext as MainWindowViewModel)?.CommitDocumentSwitcher();
    }

    // No hardcoded shell gestures: every one of them is a Window.KeyBinding rebuilt from
    // IKeyboardShortcutService (see RebuildKeyBindings), so all are rebindable in
    // Settings ▸ Keyboard. base.OnKeyDown evaluates those bindings.

    // ----- quick export dialog --------------------------------
    private async System.Threading.Tasks.Task ShowQuickExportAsync(MainWindowViewModel vm)
    {
        var dialog = new QuickExportWindow();
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;
        var m = dialog.Model;
        await vm.Build.RunQuickExportAsync(m.ComposeBlock(), m.OutputFileName);
    }

    // ----- scaffold Therion project from a bare TopoDroid survey .th ------
    private async System.Threading.Tasks.Task ShowScaffoldProjectAsync(MainWindowViewModel vm, string? mode)
    {
        if (vm.StoragePicker is not { } picker) return;

        // Seed from the active editor when asked and it's a .th; otherwise prompt for a survey file.
        string? source = null;
        if (string.Equals(mode, "active", StringComparison.OrdinalIgnoreCase)
            && vm.ActiveDocumentPath is { } active
            && active.EndsWith(".th", StringComparison.OrdinalIgnoreCase))
            source = active;
        source ??= await picker.PickOpenFileAsync(ThIDE.Resources.Tr.Get("Scaffold_PickSource"));
        if (string.IsNullOrEmpty(source)) return;

        string text;
        try { text = File.ReadAllText(source); }
        catch (Exception ex) { vm.StatusText = ex.Message; return; }

        var model = ScaffoldProjectViewModel.FromSource(source, text);
        var dialog = new ScaffoldProjectWindow(model);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;

        var root = await picker.PickOpenFolderAsync(ThIDE.Resources.Tr.Get("Scaffold_PickTarget"));
        if (string.IsNullOrEmpty(root)) return;

        await vm.RunScaffoldAsync(model.BuildOptions(), source, root);
    }

    // ----- new file -------------------------------------------------------

    /// <summary>File ▸ New File…: templates, name, folder and an optional input line into a survey file.</summary>
    private async System.Threading.Tasks.Task ShowNewFileAsync(string? folder)
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        var session = AppServices.Provider.GetService<IWorkspaceSession>();
        var settings = AppServices.Provider.GetService<IAppSettingsService>();
        if (docs is null) return;
        var root = session?.RootPath;
        var location = settings?.Current.ProjectConfigLocation ?? Therion.Workspace.ProjectConfigLocation.ProjectFolder;

        // Template files: the user's (app data), then the project's (its config folder).
        var layers = new List<Therion.Workspace.Templates.SnippetLayer>();
        void AddDir(string? dir)
        {
            if (dir is null || !Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            {
                try { layers.Add(new(Path.GetFileName(file), File.ReadAllText(file))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        AddDir(Path.Combine(Therion.Workspace.ProjectSidecar.DirectoryFor("templates"), "files"));
        AddDir(Therion.Workspace.ProjectLocalConfig.PathFor(root, location, Path.Combine("templates", "files")));
        var (templates, errors) = Therion.Workspace.Templates.FileTemplateCatalog.Load(Therion.Workspace.Templates.FileTemplateCatalog.BuiltIns, layers);
        if (errors.Count > 0)
            AppServices.Provider.GetService<INotificationService>()?.Warning(ThIDE.Resources.Tr.Get("Notif_TemplatesInvalid"),
                string.Join("\n", errors.Take(3).Select(e => e.ToString())));

        var active = docs.Active;
        var start = folder
            ?? (active is { FilePath: { } ap } && Path.IsPathRooted(ap) ? Path.GetDirectoryName(ap) : null)
            ?? root
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var ws = docs.Workspace;
        var surveyFiles = ws?.PerFile.Keys.Where(k => k.EndsWith(".th", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var parents = new HashSet<string>(ws?.FileGraphEdges.Select(e => Path.GetFullPath(e.From)) ?? [], Therion.Workspace.Editing.FilePaths.Comparer);
        var buffers = docs.Documents.Where(d => Path.IsPathRooted(d.FilePath))
            .GroupBy(d => Path.GetFullPath(d.FilePath), Therion.Workspace.Editing.FilePaths.Comparer)
            .ToDictionary(g => g.Key, g => g.First().DocumentText, Therion.Workspace.Editing.FilePaths.Comparer);

        var context = active is null
            ? new Therion.Workspace.Templates.SnippetContext(null, "", 0, DateTime.Now)
            : new Therion.Workspace.Templates.SnippetContext(active.FilePath, active.DocumentText, active.CurrentCaret.StartOffset, DateTime.Now);
        var activeVars = Therion.Workspace.Templates.SnippetVariables.Standard(context);
        string? Vars(string name) => name.StartsWith("FILE_", StringComparison.Ordinal) ? null : activeVars(name);

        var env = new NewFileEnvironment(
            templates, start, root, surveyFiles, parents,
            p => buffers.TryGetValue(Path.GetFullPath(p), out var t) ? t : File.Exists(p) ? Therion.Syntax.EncodingResolver.ReadAllText(p) : null,
            p => File.Exists(p) || Directory.Exists(p),
            Vars,
            AppServices.Provider.GetService<IWorkspaceEditService>(),
            (path, caret) => OpenAtAsync(docs, path, caret),
            path => docs.ActivateThconfigAsync(path));
        await new NewFileWindow(new NewFileViewModel(env)).ShowDialog(this);
    }

    // ----- new trip -------------------------------------------------------

    /// <summary>Tools ▸ New trip…: a trip file prefilled from the latest trip and tied into the project.</summary>
    private async System.Threading.Tasks.Task ShowNewTripAsync()
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        var notifications = AppServices.Provider.GetService<INotificationService>();
        if (docs?.Workspace is not { PerFile.Count: > 0 } ws)
        {
            notifications?.Warning(ThIDE.Resources.Tr.Get("NewTrip_Title"), ThIDE.Resources.Tr.Get("LinkSurvey_NoWorkspace"));
            return;
        }
        var buffers = docs.Documents.Where(d => Path.IsPathRooted(d.FilePath))
            .GroupBy(d => Path.GetFullPath(d.FilePath), Therion.Workspace.Editing.FilePaths.Comparer)
            .ToDictionary(g => g.Key, g => g.First().DocumentText, Therion.Workspace.Editing.FilePaths.Comparer);
        var env = new NewTripEnvironment(
            ws,
            AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath,
            p => buffers.TryGetValue(Path.GetFullPath(p), out var t) ? t : File.Exists(p) ? Therion.Syntax.EncodingResolver.ReadAllText(p) : null,
            p => File.Exists(p) || Directory.Exists(p),
            AppServices.Provider.GetService<IWorkspaceEditService>(),
            (path, caret) => OpenAtAsync(docs, path, caret),
            DateTime.Now);
        var model = new NewTripViewModel(env);
        var load = model.LoadAsync();
        await new NewTripWindow(model).ShowDialog(this);
        await load;
    }

    /// <summary>Opens a file and puts the caret at an offset.</summary>
    private static async System.Threading.Tasks.Task OpenAtAsync(IDocumentService docs, string path, int caret)
    {
        await docs.OpenFileAsync(path);
        var doc = docs.Documents.FirstOrDefault(d => Therion.Workspace.Editing.FilePaths.Same(d.FilePath, path));
        if (doc is null) return;
        var text = doc.DocumentText;
        caret = Math.Clamp(caret, 0, text.Length);
        int line = 1, col = 1;
        for (int i = 0; i < caret; i++) { if (text[i] == '\n') { line++; col = 1; } else col++; }
        var at = new Therion.Core.SourceLocation(line, col);
        doc.RequestScrollTo(new Therion.Core.SourceSpan(path, at, at, caret, 0));
    }

    // ----- new project ----------------------------------------------------

    /// <summary>File ▸ New Project…: writes a project from a template, then makes its thconfig active.</summary>
    private async System.Threading.Tasks.Task ShowNewProjectAsync()
    {
        var library = Path.Combine(Therion.Workspace.ProjectSidecar.DirectoryFor("templates"), "projects");
        var start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var model = new NewProjectViewModel(library, Path.Combine(start, "cave"));
        var dialog = new NewProjectWindow(model);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed || model.CreatedThconfig is not { } thconfig) return;
        var docs = AppServices.Provider.GetService<IDocumentService>();
        if (docs is not null)
            await docs.ActivateThconfigAsync(thconfig, new ThconfigActivation(SetWorkingDirectory: true, OpenInEditor: true, Notify: true));
    }

    // ----- batch build ------------------------------------------------------

    /// <summary>Build ▸ Build multiple…: the workspace's thconfigs and their exports, built in order.</summary>
    private async System.Threading.Tasks.Task ShowBatchBuildAsync(MainWindowViewModel vm, IReadOnlyList<string>? preselect)
    {
        var session = AppServices.Provider.GetService<IWorkspaceSession>();
        var configs = session?.Candidates.Select(c => c.FullPath).ToList() ?? [];
        foreach (var p in preselect ?? []) if (!configs.Contains(p)) configs.Add(p);
        if (configs.Count == 0)
        {
            AppServices.Provider.GetService<INotificationService>()?.Info(ThIDE.Resources.Tr.Get("Batch_Title"), ThIDE.Resources.Tr.Get("Batch_NoThconfigs"));
            return;
        }
        var root = session?.RootPath;
        var key = Therion.Workspace.ProjectSidecar.KeyFor(root);
        var env = new BatchBuildEnvironment(
            configs, root,
            (entry, temp, label) => vm.Build.RunBuildAsync(entry, temp, label),
            () => vm.Build.CancelCommand.Execute(null),
            AppServices.Provider.GetService<IAutomationOutput>(),
            key is null ? null : Path.Combine(Therion.Workspace.ProjectSidecar.DirectoryFor("batch"), key + ".json"));
        var model = new BatchBuildViewModel(env, preselect);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchBuildViewModel.IsRunning) && !model.IsRunning && model.Outcomes.Count > 0)
            {
                var ok = model.Outcomes.Count(o => o.Succeeded);
                var n = AppServices.Provider.GetService<INotificationService>();
                if (ok == model.Outcomes.Count) n?.Success(ThIDE.Resources.Tr.Get("Batch_Title"), model.Status);
                else n?.Warning(ThIDE.Resources.Tr.Get("Batch_Title"), model.Status);
            }
        };
        await new BatchBuildWindow(model).ShowDialog(this);
    }

    // ----- tasks --------------------------------------------------------------

    /// <summary>Fills Tools ▸ Tasks with the project's tasks each time it opens.</summary>
    private void OnTasksSubmenuOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || DataContext is not MainWindowViewModel { Tasks: { } runner }) return;
        menu.Items.Clear();
        var tasks = runner.Tasks();
        foreach (var task in tasks)
        {
            var id = task.Id;
            var item = new MenuItem { Header = task.Title };
            Avalonia.Automation.AutomationProperties.SetAutomationId(item, "Menu.Tools.Tasks." + id);
            item.Click += (_, _) => _ = runner.RunAsync(id);
            menu.Items.Add(item);
        }
        if (tasks.Count == 0) menu.Items.Add(new MenuItem { Header = ThIDE.Resources.Tr.Get("Task_None"), IsEnabled = false });
    }

    // ----- fixes ----------------------------------------------------------------

    /// <summary>A file's current text: the editor buffer when open, else the file by its encoding.</summary>
    private static string? CurrentText(IDocumentService? docs, string path)
    {
        var doc = docs?.Documents.FirstOrDefault(d => Therion.Workspace.Editing.FilePaths.Same(d.FilePath, path));
        if (doc is not null) return doc.DocumentText;
        return File.Exists(path) ? Therion.Syntax.EncodingResolver.ReadAllText(path) : null;
    }

    /// <summary>Applies the first fix for one diagnostic (Diagnostics panel ▸ Fix).</summary>
    private async System.Threading.Tasks.Task FixOneAsync(Therion.Core.Diagnostic d)
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        if (d.Span.FilePath is not { Length: > 0 } path || CurrentText(docs, path) is not { } text) return;
        // This diagnostic's own fix only (an unrelated fix on the same line is not "fixing" it).
        var fix = Therion.Workspace.Fixes.CodeFixes.ForDiagnostic(text, d)
                  ?? Therion.Workspace.Fixes.CodeFixes.ForLine(text, d.Span.Start.Line, [d]).FirstOrDefault(f => f.DiagnosticCode == d.Code.Value);
        if (fix is null)
        {
            AppServices.Provider.GetService<INotificationService>()?.Info(ThIDE.Resources.Tr.Get("Diag_Fix"), ThIDE.Resources.Tr.Get("Diag_NoFix"));
            return;
        }
        var batch = new Therion.Workspace.Editing.EditBatch([new(path, fix.Edits)], [], []);
        // A fix that cannot tell where it belongs (an end inserted after a flat block body) is shown first.
        if (!fix.SafeForBatch)
        {
            await PreviewAndApplyAsync(ThIDE.Resources.Tr.Get("Diag_Fix"), ThIDE.Resources.Tr.Get("Diag_FixCheck"), batch);
            return;
        }
        if (AppServices.Provider.GetService<IWorkspaceEditService>() is not { } editor) return;
        var outcome = await editor.ApplyAsync(batch);
        if (!outcome.Success) AppServices.Provider.GetService<INotificationService>()?.Warning(ThIDE.Resources.Tr.Get("Diag_Fix"), outcome.Describe());
    }

    /// <summary>Diagnostics panel ▸ Fix all '<code>': every safe fix for that code, previewed, then applied.</summary>
    private async System.Threading.Tasks.Task FixAllAsync(MainWindowViewModel vm, string code)
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        var notifications = AppServices.Provider.GetService<INotificationService>();
        // The diagnostics' line numbers are from the last check: an open file edited since may have
        // moved them, so such files must be saved (and re-checked) first.
        var dirty = vm.Diagnostics.All.Where(d => d.Code.Value == code && d.Span.FilePath is { } f
                && docs?.Documents.Any(x => x.IsDirty && Therion.Workspace.Editing.FilePaths.Same(x.FilePath, f)) == true)
            .Select(d => Path.GetFileName(d.Span.FilePath)).Distinct().ToList();
        if (dirty.Count > 0)
        {
            notifications?.Warning(string.Format(ThIDE.Resources.Tr.Get("Diag_FixAll"), code), string.Format(ThIDE.Resources.Tr.Get("Diag_FixAllSaveFirst"), string.Join(", ", dirty)));
            return;
        }
        var (batch, fixedCount, skipped) = Therion.Workspace.Fixes.FixAll.Plan(code, vm.Diagnostics.All, p => CurrentText(docs, p));
        if (fixedCount == 0)
        {
            notifications?.Info(string.Format(ThIDE.Resources.Tr.Get("Diag_FixAll"), code), ThIDE.Resources.Tr.Get("Diag_FixAllNone"));
            return;
        }
        await PreviewAndApplyAsync(string.Format(ThIDE.Resources.Tr.Get("Diag_FixAll"), code),
            string.Format(ThIDE.Resources.Tr.Get("Diag_FixAllSummary"), fixedCount, batch.Edits.Count, skipped), batch, reload: true);
    }

    /// <summary>Shows a preview of a batch and applies the ticked edits (one undo step per open file).</summary>
    private async System.Threading.Tasks.Task<bool> PreviewAndApplyAsync(string title, string summary, Therion.Workspace.Editing.EditBatch batch,
        IReadOnlyList<Therion.Workspace.Fixes.EditPreviewRow>? rows = null, bool reload = false)
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        rows ??= Therion.Workspace.Fixes.FixAll.Preview(batch, p => CurrentText(docs, p));
        var preview = new EditPreviewViewModel(title, summary, batch, rows, AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath);
        var window = new EditPreviewWindow(preview);
        await window.ShowDialog(this);
        if (!window.Confirmed || AppServices.Provider.GetService<IWorkspaceEditService>() is not { } editor) return false;
        var outcome = await editor.ApplyAsync(preview.Chosen());
        // Closed files change on disk without a re-parse: reload when the diagnostics must follow. Not
        // through the batch's own reload flag, which also refuses files with unsaved changes — right for
        // a batch that adds an input, wrong for one that only edits text.
        if (outcome.Success && reload && AppServices.Provider.GetService<IWorkspaceSession>() is { ActiveThconfig: { } active } session)
            await session.SetActiveThconfigAsync(active.FullPath);
        var n = AppServices.Provider.GetService<INotificationService>();
        if (outcome.Success) n?.Success(title, string.Format(ThIDE.Resources.Tr.Get("Diag_FixAllDone"), preview.CheckedCount));
        else n?.Warning(title, outcome.Describe());
        return outcome.Success;
    }

    /// <summary>Tools ▸ Suggest equates: pick a close pair and open Link survey file on it.</summary>
    private async System.Threading.Tasks.Task SuggestEquatesAsync()
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        if (docs?.Workspace is not { PerFile.Count: > 0 } ws) return;
        var window = new SuggestEquatesWindow(new SuggestEquatesViewModel(ws, AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath));
        await window.ShowDialog(this);
        if (window.Chosen is not { } pair || pair.Candidate.A.DeclarationSpan.FilePath is not { } fa || pair.Candidate.B.DeclarationSpan.FilePath is not { } fb) return;
        await ShowLinkSurveyAsync(null, 0, m => m.PresetStationsAsync(fa, pair.Candidate.A.Name, fb, pair.Candidate.B.Name));
    }

    /// <summary>Tools ▸ Format all files: every project file through the formatter, previewed first.</summary>
    private async System.Threading.Tasks.Task FormatAllAsync()
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        if (docs?.Workspace is not { } ws) return;
        var files = ws.PerFile.Keys
            .Concat(ws.FileGraphEdges.SelectMany(e => new[] { e.From, e.To }))
            .Where(f => f.EndsWith(".th", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".th2", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".thconfig", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var settings = AppServices.Provider.GetService<IAppSettingsService>()?.Current;
        var unit = settings is { ConvertTabsToSpaces: false } ? "\t" : new string(' ', Math.Max(1, settings?.IndentationSize ?? 2));
        // The editor's Format Document: a re-indent that never reorders or drops anything.
        string Format(string text)
        {
            var doc = new AvaloniaEdit.Document.TextDocument(text);
            ThIDE.Editor.TherionFormatter.Reindent(doc, unit);
            return doc.Text;
        }
        var (batch, rows, skipped) = Therion.Workspace.Fixes.FormatAll.Plan(files, p => CurrentText(docs, p), Therion.Workspace.Fixes.LineEndings.Keep,
            Format, n => string.Format(ThIDE.Resources.Tr.Get("FormatAll_LinesDiffer"), n));
        var title = ThIDE.Resources.Tr.Get("Menu_Tools_FormatAll");
        var summary = string.Format(ThIDE.Resources.Tr.Get("FormatAll_Summary"), rows.Count, skipped.Count);
        if (batch.IsEmpty) { AppServices.Provider.GetService<INotificationService>()?.Info(title, summary); return; }
        await PreviewAndApplyAsync(title, summary, batch, rows, reload: true);
    }

    /// <summary>Tools ▸ Add unreferenced scraps to maps.</summary>
    private async System.Threading.Tasks.Task SyncMapsAsync()
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        var n = AppServices.Provider.GetService<INotificationService>();
        var title = ThIDE.Resources.Tr.Get("Menu_Tools_SyncMaps");
        if (docs?.Workspace is not { } ws) return;
        var model = new SyncMapsViewModel(ws, AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath);
        if (model.Rows.Count == 0 || !model.HasMaps) { n?.Info(title, ThIDE.Resources.Tr.Get(model.Rows.Count == 0 ? "SyncMaps_Nothing" : "SyncMaps_NoMaps")); return; }
        var window = new SyncMapsWindow(model);
        await window.ShowDialog(this);
        if (!window.Confirmed) return;

        var cache = new Dictionary<string, Therion.Syntax.TherionFile?>(Therion.Workspace.Editing.FilePaths.Comparer);
        Therion.Syntax.TherionFile? Ast(string p) => cache.TryGetValue(p, out var a) ? a
            : cache[p] = p.EndsWith(".th", StringComparison.OrdinalIgnoreCase) && CurrentText(docs, p) is { } t ? new Therion.Syntax.ThParser().Parse(p, t).Value : null;
        var scopes = Therion.Semantics.Linking.SurveyScopeResolver.Build(ws, Ast);
        var (batch, problems) = Therion.Workspace.Fixes.MapSync.Plan(ws, scopes, model.Assignments(), p => CurrentText(docs, p), Ast);
        var summary = problems.Count == 0 ? ThIDE.Resources.Tr.Get("SyncMaps_Summary")
            : ThIDE.Resources.Tr.Get("SyncMaps_Summary") + " " + string.Format(ThIDE.Resources.Tr.Get("SyncMaps_Problems"),
                string.Join(", ", problems.Select(p => $"{p.Map} ({ThIDE.Resources.Tr.Get("SyncMaps_" + p.Problem)})")));
        if (batch.IsEmpty) { n?.Warning(title, summary); return; }
        await PreviewAndApplyAsync(title, summary, batch, reload: true);
    }

    /// <summary>An arrived (or dropped) file: link a survey, or import Survex/Compass/GPX first.</summary>
    private readonly System.Threading.SemaphoreSlim _importOneAtATime = new(1, 1);

    private async System.Threading.Tasks.Task ImportArrivedAsync(MainWindowViewModel vm, string path)
    {
        // Several dropped files: one dialog after another, not all at once.
        await _importOneAtATime.WaitAsync();
        try { await ImportOneAsync(vm, path); }
        catch (Exception ex) { AppServices.Provider.GetService<INotificationService>()?.Error(ThIDE.Resources.Tr.Get("Notif_ImportFailed"), ex.Message); }
        finally { _importOneAtATime.Release(); }
    }

    private async System.Threading.Tasks.Task ImportOneAsync(MainWindowViewModel vm, string path)
    {
        switch (Therion.Workspace.Incoming.IncomingFiles.Route(path))
        {
            case Therion.Workspace.Incoming.IncomingRoute.LinkSurvey:
                await ShowLinkSurveyAsync(null, 0, m => m.LoadAsync(path));
                break;
            case Therion.Workspace.Incoming.IncomingRoute.ImportSurvey:
                if (await vm.ImportSurveyFromPathAsync(path) is { } th)
                    await ShowLinkSurveyAsync(null, 0, m => m.LoadAsync(th));
                break;
            case Therion.Workspace.Incoming.IncomingRoute.ImportGpx:
                await vm.ImportGpxFromPathAsync(path);
                break;
        }
    }

    // ----- macros -----------------------------------------------------------

    /// <summary>Fills Tools ▸ Macros each time it opens.</summary>
    private void OnMacrosSubmenuOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || AppServices.Provider.GetService<IMacroService>() is not { } macros) return;
        menu.Items.Clear();
        foreach (var (macro, fromProject) in macros.Macros())
        {
            var name = macro.Name;
            var item = new MenuItem { Header = fromProject ? $"{macro.Title} ({ThIDE.Resources.Tr.Get("Macro_Project")})" : macro.Title };
            ToolTip.SetTip(item, macro.Description);
            Avalonia.Automation.AutomationProperties.SetAutomationId(item, "Menu.Tools.Macros." + name);
            item.Click += (_, _) => _ = macros.RunAsync(name);
            menu.Items.Add(item);
        }
    }

    /// <summary>Asks for each macro variable in turn (null when cancelled).</summary>
    private async System.Threading.Tasks.Task<Dictionary<string, string>?> AskMacroVariablesAsync(Therion.Mcp.Dispatch.MacroDefinition macro)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in macro.Vars)
        {
            string? value;
            if (v.Type is "file" or "folder")
            {
                var picker = (DataContext as MainWindowViewModel)?.StoragePicker;
                value = picker is null ? null : v.Type == "file" ? await picker.PickOpenFileAsync(v.Prompt ?? v.Id) : await picker.PickOpenFolderAsync(v.Prompt ?? v.Id);
                // The tools take paths inside the project: pass them relative to it.
                if (value is not null && AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath is { } root)
                {
                    var rel = Path.GetRelativePath(root, value).Replace('\\', '/');
                    if (rel == ".." || rel.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                    {
                        // The tools would refuse it: say why and stop here rather than run a failing preview.
                        AppServices.Provider.GetService<INotificationService>()?.Warning(macro.Title, ThIDE.Resources.Tr.Get("Macro_OutsideProject"));
                        return null;
                    }
                    value = rel;
                }
            }
            else
            {
                var prompt = v.Choices is { Count: > 0 } c ? $"{v.Prompt ?? v.Id} ({string.Join(", ", c)})" : v.Prompt ?? v.Id;
                value = await new InputDialog(macro.Title, prompt, v.Default ?? "").ShowAsync(this);
            }
            if (value is null) return null;
            values[v.Id] = value;
        }
        return values;
    }

    /// <summary>Shows a macro's preview summary and asks whether to apply it.</summary>
    private async System.Threading.Tasks.Task<bool> ConfirmMacroAsync(Therion.Mcp.Dispatch.MacroDefinition macro, Therion.Mcp.Dispatch.MacroRun preview)
    {
        var lines = preview.Steps.Select(s => $"• {s.StepId} ({s.Tool}): {s.Status} — {s.Summary}");
        var message = string.Format(ThIDE.Resources.Tr.Get("Macro_ApplyQuestion"), string.Join("\n", lines));
        return await new ConfirmDialog(macro.Title, message, ThIDE.Resources.Tr.Get("Macro_Apply")).ShowAsync(this);
    }

    // ----- automation panel -----------------------------------------------

    /// <summary>Shows the Automation panel when a run starts, and opens the file:line a row names.</summary>
    private void AttachAutomationPanel()
    {
        if (AppServices.Provider.GetService<AutomationOutputViewModel>() is not { } automation) return;
        automation.SessionStarted += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            (DataContext as MainWindowViewModel)?.Factory.ShowToolById("Automation"));
        automation.NavigateRequested += (_, row) =>
        {
            if (AppServices.Provider.GetService<IDocumentService>() is not { } docs || row.File is null) return;
            var root = AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath;
            var path = Path.IsPathRooted(row.File) ? row.File : Path.Combine(root ?? "", row.File);
            if (!File.Exists(path)) return;
            var at = new Therion.Core.SourceLocation(row.Line, 1);
            _ = docs.NavigateToSpanAsync(new Therion.Core.SourceSpan(path, at, at, 0, 0));
        };
    }

    // ----- snippets -------------------------------------------------------

    /// <summary>
    /// Keeps the editor's snippet catalog on the current project (its config folder holds a
    /// snippets.json of its own) and reports a broken snippet file once, with the details in the log.
    /// </summary>
    private void AttachSnippetCatalog()
    {
        var session = AppServices.Provider.GetService<IWorkspaceSession>();
        var settings = AppServices.Provider.GetService<IAppSettingsService>();
        var notifications = AppServices.Provider.GetService<INotificationService>();
        var log = AppServices.Provider.GetService<ILogService>();
        void Sync() => ThIDE.Editor.TherionSnippets.SetProject(session?.RootPath,
            settings?.Current.ProjectConfigLocation ?? Therion.Workspace.ProjectConfigLocation.ProjectFolder);
        Sync();
        if (session is not null) session.RootChanged += (_, _) => Sync();
        if (settings is not null) settings.Changed += (_, _) => Sync();
        ThIDE.Editor.TherionSnippets.LoadErrors += (_, errors) =>
        {
            foreach (var e in errors) log?.Warning($"Snippets: {e}");
            notifications?.Warning(ThIDE.Resources.Tr.Get("Notif_SnippetsInvalid"),
                string.Join("\n", errors.Take(3).Select(e => e.ToString())));
        };
    }

    // ----- link survey file -----------------------------------------------

    /// <summary>
    /// Opens "Link survey file". With <paramref name="destinationFile"/> (the editor's Generate code
    /// entry) the lines go into that file, in the survey around <paramref name="caret"/>.
    /// </summary>
    private async System.Threading.Tasks.Task ShowLinkSurveyAsync(string? destinationFile, int caret,
        Func<LinkSurveyViewModel, System.Threading.Tasks.Task>? preset = null)
    {
        var docs = AppServices.Provider.GetService<IDocumentService>();
        var notifications = AppServices.Provider.GetService<INotificationService>();
        if (docs?.Workspace is not { PerFile.Count: > 0 } ws)
        {
            notifications?.Warning(ThIDE.Resources.Tr.Get("LinkSurvey_Title"), ThIDE.Resources.Tr.Get("LinkSurvey_NoWorkspace"));
            return;
        }

        // Snapshot the open buffers now (UI thread); the dialog reads files off it.
        var buffers = new Dictionary<string, string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var d in docs.Documents)
            if (!string.IsNullOrEmpty(d.FilePath) && Path.IsPathRooted(d.FilePath))
                buffers.TryAdd(Path.GetFullPath(d.FilePath), d.DocumentText);

        if (destinationFile is not null && !destinationFile.EndsWith(".th", StringComparison.OrdinalIgnoreCase)) destinationFile = null;
        string? current = destinationFile;
        int currentCaret = caret;
        if (current is null && docs.Active is { } active && active.FilePath.EndsWith(".th", StringComparison.OrdinalIgnoreCase))
        {
            current = active.FilePath;
            currentCaret = active.CurrentCaret.StartOffset;
        }

        var env = new LinkSurveyEnvironment(
            ws,
            AppServices.Provider.GetService<IWorkspaceSession>()?.RootPath,
            path =>
            {
                var full = Path.GetFullPath(path);
                if (buffers.TryGetValue(full, out var text)) return text;
                return File.Exists(full) ? Therion.Syntax.EncodingResolver.ReadAllText(full) : null;
            },
            dir => Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.th2", SearchOption.AllDirectories).Take(500).ToList()
                : [],
            (a, b) => File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)),
            AppServices.Provider.GetService<IWorkspaceEditService>(),
            current,
            currentCaret,
            DateOnly.FromDateTime(DateTime.Now));

        var model = new LinkSurveyViewModel(env);
        var dialog = new LinkSurveyWindow(model);
        model.Applied += (_, outcome) =>
            notifications?.Success(ThIDE.Resources.Tr.Get("LinkSurvey_Title"),
                string.Format(ThIDE.Resources.Tr.Get("LinkSurvey_Done"), Path.GetFileName(model.SourcePath)));
        // The dialog opens straight away; the survey file is chosen in it (Browse, or typed + Enter).
        var load = preset is not null ? preset(model) : model.LoadAsync(null, destinationFile);
        await dialog.ShowDialog(this);
        await load;
    }

    // ----- global search window (#3) -------------------------------------

    private SearchWindow? _searchWindow;

    private void OnSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowSearch();

    // #4: quick-open / command-palette / symbol-search toolbar buttons.
    private void OnGoToFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.ShowQuickOpen();
    private void OnGoToAction(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.ShowCommandPalette();
    private void OnGoToSymbolWorkspace(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.ShowGoToSymbol(workspace: true);
    private void OnGoToSymbolDocument(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as MainWindowViewModel)?.ShowGoToSymbol(workspace: false);

    private void ShowSearch()
    {
        if (_searchWindow is { } w && w.IsVisible) { w.Activate(); return; }

        ViewModels.SearchViewModel? vm = null;
        try { vm = AppServices.Provider.GetService<ViewModels.SearchViewModel>(); }
        catch { /* design-time / no container */ }
        if (vm is null) return;

        _searchWindow = new SearchWindow { DataContext = vm };
        _searchWindow.Closed += (_, _) => _searchWindow = null;
        _searchWindow.Show(this);

        // Prefill from the editor selection and auto-search (#10).
        vm.SeedAndSearch(ActiveEditor?.EditorSelectedText);
    }

    // ----- replace in files window (#9) ----------------------------------

    private ReplaceWindow? _replaceWindow;

    private void ShowReplace()
    {
        ViewModels.ReplaceInFilesViewModel? vm = null;
        try { vm = AppServices.Provider.GetService<ViewModels.ReplaceInFilesViewModel>(); }
        catch { /* design-time / no container */ }
        if (vm is null) return;

        // Seed from the editor selection (auto Find All), else keep the previous query; clears old status.
        var selection = ActiveEditor?.SelectedText;

        if (_replaceWindow is { } w && w.IsVisible)
        {
            w.Activate();
            _ = vm.PrepareForOpenAsync(selection);
            return;
        }

        _replaceWindow = new ReplaceWindow { DataContext = vm };
        _replaceWindow.Closed += (_, _) => _replaceWindow = null;
        _ = vm.PrepareForOpenAsync(selection);
        _replaceWindow.Show(this);
    }

    // ----- symbol rename (#1) --------------------------------------------

    private void TriggerRenameSymbol() => ActiveEditor?.StartRename();

    private RenamePreviewWindow? _renamePreviewWindow;

    private ModalDesyncWatchdog? _modalWatchdog;

    // Watches for the window being left disabled by a modal that has already gone (see the watchdog's
    // remarks): the app repaints but ignores every click, which reads as a permanent freeze.
    private void StartModalDesyncWatchdog()
    {
        try
        {
            _modalWatchdog = new ModalDesyncWatchdog(this,
                AppServices.Provider.GetService<ILogService>(),
                AppServices.Provider.GetService<INotificationService>());
            _modalWatchdog.Start();
        }
        catch { /* design-time / no container */ }
    }

    /// <summary>
    /// Find All References (#7): resolves the token to a symbol and lists every occurrence the index
    /// attributes to it (scope-correct, @-aware, cross-file) — the read-only half of true rename.
    /// </summary>
    private async System.Threading.Tasks.Task HandleFindAllReferencesAsync(
        string raw, Therion.Processing.Abstractions.ReferenceKind kind)
    {
        IDocumentService? docs;
        try { docs = AppServices.Provider.GetService<IDocumentService>(); }
        catch { return; }
        if (docs?.Workspace is not { } workspace) return;

        var references = Therion.Semantics.SymbolReferences.FindAll(workspace, raw, kind);
        if (references.Count == 0)
        {
            await new MessageDialog(ThIDE.Resources.Tr.Get("Refs_Title"),
                string.Format(ThIDE.Resources.Tr.Get("Refs_None"), raw)).ShowAsync(this);
            return;
        }

        // Non-modal, and only one at a time: a second search replaces the list rather than stacking windows.
        _referencesWindow?.Close();
        var win = new ReferencesWindow(raw, references, docs);
        _referencesWindow = win;
        win.Closed += (_, _) => _referencesWindow = null;
        win.Show(this);
    }

    private ReferencesWindow? _referencesWindow;

    private async System.Threading.Tasks.Task HandleRenameSymbolAsync(
        string raw, Therion.Processing.Abstractions.ReferenceKind kind)
    {
        IDocumentService? docs = null;
        IAppSettingsService? settings = null;
        try
        {
            docs = AppServices.Provider.GetService<IDocumentService>();
            settings = AppServices.Provider.GetService<IAppSettingsService>();
        }
        catch { return; }

        var nav = docs?.CurrentNavigation;
        var workspace = docs?.Workspace;
        if (nav is null || workspace is null) return;

        var targetSpan = nav.GoToDefinition(raw, kind);
        if (targetSpan is null || targetSpan.Value.IsEmpty) return;

        var stRef = Therion.Semantics.StationRef.Parse(raw);
        var oldName = kind == Therion.Processing.Abstractions.ReferenceKind.Survey
            ? (stRef.SurveyLastName ?? stRef.Point)
            : stRef.PointWithoutMark;
        if (string.IsNullOrEmpty(oldName)) return;

        try
        {
            // Find every occurrence up-front — both to apply and to choose the workflow. Stations & surveys
            // rename via the true occurrence index (scope-correct, @-aware, cross-file); scrap/map use the
            // text-scan; a standalone file falls back to the active document's own index.
            var changes = CollectSymbolRenameChanges(workspace, docs!.Active, kind, targetSpan.Value)
                ?? CollectActiveFileRenameChanges(docs!.Active, kind, targetSpan.Value)
                ?? CollectRenameChanges(workspace, docs!.Active, nav, raw, kind, oldName, targetSpan.Value);
            if (changes.Count == 0)
            {
                await new MessageDialog("Rename Symbol",
                    $"No occurrences of '{oldName}' were found in the workspace to rename.").ShowAsync(this);
                return;
            }

            // Opt-in expansions surfaced only in the occurrences preview.
            var commentChanges = CollectCommentRenameChanges(workspace, docs!.Active, oldName, changes);
            var equateLinkedChanges = CollectEquateLinkedChanges(workspace, docs!.Active, kind, oldName, targetSpan.Value, changes);
            var sameNameChanges = CollectSameNameChanges(workspace, docs!.Active, kind, oldName, changes);

            // "Complex" = the rename reaches beyond this file, or there are other-survey namesakes / equate
            // links to weigh. Complex renames always open the occurrences preview (with its own name field).
            // A simple, single-file rename is applied straight from the input dialog, which still offers a
            // "See occurrences" button to open the preview on demand.
            var activePath = docs!.Active?.FilePath;
            bool crossFile = changes.Any(c => activePath is null ||
                !string.Equals(c.FilePath, activePath, StringComparison.OrdinalIgnoreCase));
            bool otherSurvey = equateLinkedChanges.Count > 0 || sameNameChanges.Count > 0;
            bool complex = crossFile || otherSurvey || settings?.Current.ShowRenamePreviewBeforeApply == true;

            string newName;
            List<RenameFileChanges> optional;

            if (!complex)
            {
                var dlg = new InputDialog("Rename Symbol",
                    string.Format(ThIDE.Resources.Tr.Get("Rename_NewNameFor"), oldName), oldName,
                    seeOccurrencesText: ThIDE.Resources.Tr.Get("Rename_SeeOccurrences"));
                var entered = await dlg.ShowAsync(this);
                if (string.IsNullOrWhiteSpace(entered)) return;
                newName = entered.Trim();
                if (newName == oldName) return;
                if (InvalidName(newName) is { } bad1) { await ShowInvalidNameAsync(newName, bad1); return; }

                if (!dlg.SeeOccurrencesRequested)
                {
                    await ApplyRenameChangesAsync(changes, newName, docs!);   // simple path: apply directly
                    return;
                }
                // "See occurrences" → open the preview with the entered name pre-filled.
                var res = await ShowRenamePreviewAsync(docs!, changes, equateLinkedChanges, sameNameChanges,
                    commentChanges, oldName, newName, focusName: false);
                if (!res.Apply) return;
                newName = res.NewName; optional = res.Optional;
            }
            else
            {
                // No separate input dialog — the preview carries the (focused) name field itself.
                var res = await ShowRenamePreviewAsync(docs!, changes, equateLinkedChanges, sameNameChanges,
                    commentChanges, oldName, oldName, focusName: true);
                if (!res.Apply) return;
                newName = res.NewName; optional = res.Optional;
            }

            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            if (InvalidName(newName) is { } bad2) { await ShowInvalidNameAsync(newName, bad2); return; }

            await ApplyRenameChangesAsync(MergeRenameChanges(changes, optional), newName, docs!);
        }
        catch (System.Exception ex)
        {
            // Never let the rename fail silently (it runs fire-and-forget from the editor event).
            await new MessageDialog("Rename Symbol failed", ex.ToString()).ShowAsync(this);
        }
    }

    private static char? InvalidName(string name) => Therion.Syntax.TherionIdentifiers.FirstIllegalChar(name);

    private System.Threading.Tasks.Task ShowInvalidNameAsync(string name, char bad) =>
        new MessageDialog("Rename Symbol",
            $"'{name}' is not a valid Therion name — it contains '{bad}'.").ShowAsync(this);

    /// <summary>
    /// Opens the occurrences preview (draining pending input first so the modal hand-off is reliable) and
    /// returns the user's decision, the name they settled on, and the optional occurrences they selected.
    /// </summary>
    private async System.Threading.Tasks.Task<(bool Apply, string NewName, List<RenameFileChanges> Optional)>
        ShowRenamePreviewAsync(
            IDocumentService docs,
            List<RenameFileChanges> changes,
            List<RenameFileChanges> equateLinkedChanges,
            List<RenameFileChanges> sameNameChanges,
            List<RenameFileChanges> commentChanges,
            string oldName, string initialName, bool focusName)
    {
        // Let any pending input (e.g. the input dialog's closing click) drain before opening this modal,
        // otherwise Avalonia routes it to the torn-down dialog ("PlatformImpl is null") and it never shows.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            static () => { }, Avalonia.Threading.DispatcherPriority.Background);

        _renamePreviewWindow?.Close();
        var win = new RenamePreviewWindow(changes, equateLinkedChanges, sameNameChanges, commentChanges,
            oldName, initialName, focusName, span => docs.NavigateToSpanAsync(span));
        _renamePreviewWindow = win;
        win.Closed += (_, _) => _renamePreviewWindow = null;
        // Ensure the preview comes up focused/activated (the just-closed input dialog can otherwise leave
        // activation on the main window, which reads as "nothing happened").
        win.Opened += (_, _) => { win.Activate(); win.Focus(); };

        var apply = await win.ShowDialog<bool>(this);
        return (apply, win.ResultNewName, win.AppliedOptionalChanges());
    }

    /// <summary>
    /// True rename for a station: replaces exactly the token-level occurrences the semantic
    /// occurrence index attributes to the clicked station (scope-correct, <c>@</c>-aware, comment-free,
    /// cross-file). Returns null when the target isn't a resolvable station (caller falls back).
    /// </summary>
    private static List<RenameFileChanges>? CollectSymbolRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, Therion.Core.SourceSpan targetSpan)
    {
        // Resolve the clicked declaration to a symbol identity + its current name token; shared with
        // Find All References, which needs exactly the same resolution.
        if (Therion.Semantics.SymbolReferences.ResolveDeclaration(workspace, kind, targetSpan)
            is not { } resolved)
            return null;   // scrap/map (or unresolved): caller falls back to the text-scan
        var (symbol, name) = resolved;

        // The active document's spans must come from its live (possibly-unsaved) buffer — the apply
        // step writes FileText back and updates the open buffer in place, so disk text here would
        // clobber unsaved edits (and the stale-drift guard would silently drop the file's spans).
        var edits = Therion.Semantics.SymbolRenamePlan.Compute(workspace, symbol, name,
            path => active?.FilePath is { Length: > 0 } ap &&
                    string.Equals(path, ap, System.StringComparison.OrdinalIgnoreCase) &&
                    active.DocumentText is { Length: > 0 } at
                ? at
                : SafeReadAllText(path));
        if (edits.Count == 0) return null;
        return edits
            .Select(e => new RenameFileChanges(e.FilePath, e.FileText, e.Spans.ToList()))
            .ToList();
    }

    /// <summary>
    /// Single-file rename via the <b>active document's own</b> occurrence index — the fallback for when
    /// the edited file isn't in the loaded workspace model (a standalone <c>.th</c>, or one opened
    /// without its project, so <see cref="Therion.Semantics.WorkspaceSemanticModel.PerFile"/> doesn't
    /// cover it). Uses the live editor text so spans stay aligned even when the buffer is unsaved.
    /// Returns null when the active file has no matching station/survey declared at the target.
    /// </summary>
    private static List<RenameFileChanges>? CollectActiveFileRenameChanges(
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, Therion.Core.SourceSpan targetSpan)
    {
        if (active?.Semantics is not { } model) return null;
        var path = active.FilePath;
        var text = active.DocumentText;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) return null;
        // The occurrence index only covers this file, so the declaration must live here.
        if (!string.Equals(targetSpan.FilePath, path, System.StringComparison.OrdinalIgnoreCase)) return null;

        // Resolve the clicked declaration to a symbol identity within this file (mirrors the workspace
        // path). Any ⇒ try station then survey (a plain token arrives as Any — see CollectSymbolRenameChanges).
        bool Match(Therion.Core.SourceSpan d) =>
            d.StartOffset == targetSpan.StartOffset &&
            string.Equals(d.FilePath, path, System.StringComparison.OrdinalIgnoreCase);

        Therion.Semantics.SymbolId symbol;
        string name;
        if (WantsStation(kind) && model.Stations.Values.FirstOrDefault(s => Match(s.DeclarationSpan)) is { } st)
        {
            symbol = new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, st.Name);
            name = st.Name.Last;
        }
        else if (WantsSurvey(kind) && model.Surveys.Values.FirstOrDefault(s => Match(s.DeclarationSpan)) is { } sv)
        {
            symbol = new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, sv.Name);
            name = sv.Name.Last;
        }
        else return null;   // scrap/map (or unresolved): caller falls back to the text-scan

        if (Therion.Semantics.SymbolRenamePlan.ComputeForFile(model.Occurrences, symbol, name, path, text)
            is not { } edit) return null;
        return new List<RenameFileChanges> { new(edit.FilePath, edit.FileText, edit.Spans.ToList()) };
    }

    // A plain station/survey token reaches rename as ReferenceKind.Any; both must be tried for it.
    private static bool WantsStation(Therion.Processing.Abstractions.ReferenceKind k) =>
        k is Therion.Processing.Abstractions.ReferenceKind.Station or Therion.Processing.Abstractions.ReferenceKind.Any;
    private static bool WantsSurvey(Therion.Processing.Abstractions.ReferenceKind k) =>
        k is Therion.Processing.Abstractions.ReferenceKind.Survey or Therion.Processing.Abstractions.ReferenceKind.Any;

    /// <summary>
    /// The blunt opt-in pass: rename <b>every other</b> station/survey that shares this name, regardless of
    /// which survey or file it lives in (≈ "replace all string occurrences"). Built from the occurrence
    /// index — for each same-last-named symbol it computes that symbol's own scope-correct edits, then
    /// merges them, dropping any span already covered by the true rename. Covers workspace files and the
    /// active document (which may be outside the workspace). Empty when no other same-named symbol exists.
    /// </summary>
    private static List<RenameFileChanges> CollectSameNameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, string oldName, List<RenameFileChanges> already)
    {
        // Every station (and survey, when applicable) sharing the last name — workspace-wide plus the
        // active document's own (it may be outside the workspace). The target's own occurrences drop out
        // via the covered-span filter in BuildSymbolChanges.
        var symbols = new HashSet<Therion.Semantics.SymbolId>();
        if (WantsStation(kind) && workspace.StationsByLastName.TryGetValue(oldName, out var sts))
            foreach (var s in sts) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, s.Name));
        if (WantsSurvey(kind) && workspace.SurveysByLastName.TryGetValue(oldName, out var svs))
            foreach (var s in svs) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, s.Name));
        if (active?.Semantics is { } m)
        {
            if (WantsStation(kind))
                foreach (var s in m.Stations.Values)
                    if (s.Name.Last == oldName) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, s.Name));
            if (WantsSurvey(kind))
                foreach (var s in m.Surveys.Values)
                    if (s.Name.Last == oldName) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Survey, s.Name));
        }
        return BuildSymbolChanges(workspace, active, symbols, oldName, already);
    }

    /// <summary>
    /// Opt-in pass: rename the stations that an <c>equate</c> declares to be the same physical point as the
    /// target and that share its name in a different survey (e.g. <c>b.1</c> for <c>a.1</c> given
    /// <c>equate 1@a 1@b</c>) — following equate links across surveys/files so the connection isn't broken.
    /// Narrower than <see cref="CollectSameNameChanges"/> (only equate-linked stations, not every namesake).
    /// Works no matter where the rename was launched (equate line or an ordinary station occurrence).
    /// </summary>
    private static List<RenameFileChanges> CollectEquateLinkedChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ReferenceKind kind, string oldName,
        Therion.Core.SourceSpan targetSpan, List<RenameFileChanges> already)
    {
        if (!WantsStation(kind)) return new();

        // The target station's qualified name (workspace first, then the active file's own model).
        Therion.Semantics.QualifiedName? targetQn = workspace.FindStationByDeclaration(targetSpan)?.Name;
        if (targetQn is null && active?.Semantics is { } m)
            foreach (var s in m.Stations.Values)
                if (s.DeclarationSpan.StartOffset == targetSpan.StartOffset &&
                    string.Equals(s.DeclarationSpan.FilePath, targetSpan.FilePath, System.StringComparison.OrdinalIgnoreCase))
                { targetQn = s.Name; break; }
        if (targetQn is null) return new();

        var linked = workspace.EquatedSameNameStations(targetQn.Value);
        if (linked.IsEmpty) return new();

        var symbols = new HashSet<Therion.Semantics.SymbolId>();
        foreach (var qn in linked) symbols.Add(new Therion.Semantics.SymbolId(Therion.Semantics.SymbolKind.Station, qn));
        return BuildSymbolChanges(workspace, active, symbols, oldName, already);
    }

    /// <summary>
    /// Computes the merged, per-file rename edits for an explicit set of <paramref name="symbols"/> from the
    /// occurrence index (workspace + active document), dropping any span already covered by
    /// <paramref name="already"/>. The active document is read from its live buffer so spans stay aligned
    /// even when it is unsaved / not part of the workspace.
    /// </summary>
    private static List<RenameFileChanges> BuildSymbolChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        IReadOnlyCollection<Therion.Semantics.SymbolId> symbols, string oldName, List<RenameFileChanges> already)
    {
        if (symbols.Count == 0) return new();

        var covered = already.ToDictionary(c => c.FilePath,
            c => new HashSet<int>(c.Hits.Select(h => h.Start)), StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, RenameFileChanges>(StringComparer.OrdinalIgnoreCase);

        string? Read(string p) =>
            active?.FilePath is { Length: > 0 } ap &&
            string.Equals(p, ap, System.StringComparison.OrdinalIgnoreCase) &&
            active.DocumentText is { Length: > 0 } at
                ? at
                : (System.IO.File.Exists(p) ? SafeReadAllText(p) : null);

        void AddEdit(string filePath, string fileText, IEnumerable<(int Start, int Length)> spans)
        {
            covered.TryGetValue(filePath, out var cov);
            var add = spans.Where(s => cov is null || !cov.Contains(s.Start)).ToList();
            if (add.Count == 0) return;
            byPath[filePath] = byPath.TryGetValue(filePath, out var ex)
                ? ex with { Hits = ex.Hits.Concat(add).Distinct().OrderBy(h => h.Start).ToList() }
                : new RenameFileChanges(filePath, fileText, add.Distinct().OrderBy(h => h.Start).ToList());
        }

        foreach (var sym in symbols)
        {
            foreach (var e in Therion.Semantics.SymbolRenamePlan.Compute(workspace, sym, oldName, Read))
                AddEdit(e.FilePath, e.FileText, e.Spans);
            // Active document (may be outside the workspace occurrence index).
            if (active?.Semantics is { } m && active.FilePath is { Length: > 0 } ap && Read(ap) is { } t &&
                Therion.Semantics.SymbolRenamePlan.ComputeForFile(m.Occurrences, sym, oldName, ap, t) is { } le)
                AddEdit(le.FilePath, le.FileText, le.Spans);
        }
        return byPath.Values.ToList();
    }

    private static string? SafeReadAllText(string path)
    {
        try { return Therion.Syntax.EncodingResolver.ReadAllText(path); } catch { return null; }
    }

    /// <summary>
    /// Whole-word occurrences of <paramref name="oldName"/> that live inside <c>#</c> comments (which
    /// the symbol rename skips), across the workspace's .th files — for the opt-in "rename in comments
    /// too" pass. Excludes any span already covered by the symbol rename.
    /// </summary>
    private static List<RenameFileChanges> CollectCommentRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        string oldName, List<RenameFileChanges> symbolChanges)
    {
        var known = symbolChanges.ToDictionary(c => c.FilePath,
            c => new HashSet<int>(c.Hits.Select(h => h.Start)), StringComparer.OrdinalIgnoreCase);
        var result = new List<RenameFileChanges>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string path, string text)
        {
            if (!seenPaths.Add(path)) return;
            known.TryGetValue(path, out var seen);
            var hits = Therion.Semantics.CommentOccurrences.Find(text, oldName, seen).ToList();
            if (hits.Count > 0) result.Add(new RenameFileChanges(path, text, hits));
        }

        // Active document first, from its live buffer (may be standalone / unsaved).
        if (active?.FilePath is { Length: > 0 } activePath && active.DocumentText is { Length: > 0 } activeText)
            Scan(activePath, activeText);

        foreach (var path in workspace.PerFile.Keys)
        {
            if (seenPaths.Contains(path)) continue;
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            Scan(path, text);
        }
        return result;
    }

    /// <summary>Merges two change sets by file (dedup + order hits), so both passes apply cleanly.</summary>
    private static List<RenameFileChanges> MergeRenameChanges(
        List<RenameFileChanges> a, IReadOnlyList<RenameFileChanges> b)
    {
        var byPath = a.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);
        foreach (var c in b)
        {
            if (byPath.TryGetValue(c.FilePath, out var existing))
                byPath[c.FilePath] = existing with
                {
                    Hits = existing.Hits.Concat(c.Hits).Distinct().OrderBy(h => h.Start).ToList(),
                };
            else byPath[c.FilePath] = c;
        }
        return byPath.Values.ToList();
    }

    /// <summary>
    /// Scans every workspace file (plus the active document, so a standalone file not in the workspace
    /// is still covered) for ref tokens resolving to the same declaration span. The last-resort path for
    /// scrap/map renames, which don't yet have an occurrence index.
    /// </summary>
    private static List<RenameFileChanges> CollectRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        ViewModels.Docking.FileDocumentViewModel? active,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        string raw,
        Therion.Processing.Abstractions.ReferenceKind kind,
        string oldName,
        Therion.Core.SourceSpan targetSpan)
    {
        var result = new List<RenameFileChanges>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The active document may not be part of the loaded workspace; scan its live (possibly-unsaved)
        // text first so its spans align with what the user sees.
        if (active?.FilePath is { Length: > 0 } activePath && active.DocumentText is { Length: > 0 } activeText)
        {
            var activeHits = FindRenameHitsInText(activeText, oldName, nav, kind, targetSpan);
            if (activeHits.Count > 0) result.Add(new RenameFileChanges(activePath, activeText, activeHits));
            seenPaths.Add(activePath);
        }

        foreach (var filePath in workspace.PerFile.Keys)
        {
            if (!seenPaths.Add(filePath)) continue;   // already handled as the active doc
            string text;
            try { text = Therion.Syntax.EncodingResolver.ReadAllText(filePath); }
            catch { continue; }

            var hits = FindRenameHitsInText(text, oldName, nav, kind, targetSpan);
            if (hits.Count > 0) result.Add(new RenameFileChanges(filePath, text, hits));
        }
        return result;
    }

    private static List<(int Start, int Length)> FindRenameHitsInText(
        string text, string oldName,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        Therion.Processing.Abstractions.ReferenceKind kind,
        Therion.Core.SourceSpan targetSpan)
    {
        var hits = new List<(int, int)>();
        int idx = 0;
        while ((idx = text.IndexOf(oldName, idx, System.StringComparison.Ordinal)) >= 0)
        {
            bool beforeOk = idx == 0 || !IsRefChar(text[idx - 1]);
            bool afterOk  = idx + oldName.Length >= text.Length || !IsRefChar(text[idx + oldName.Length]);
            if (beforeOk && afterOk)
            {
                int mid = idx + oldName.Length / 2;
                var tok = ThIDE.Editor.TherionTextEditor.ExtractRefTokenStatic(text, mid);
                if (tok is { } t)
                {
                    var resolved = nav.GoToDefinition(t.Raw, kind);
                    if (resolved is { } rs &&
                        string.Equals(rs.FilePath, targetSpan.FilePath, System.StringComparison.OrdinalIgnoreCase) &&
                        rs.StartOffset == targetSpan.StartOffset)
                    {
                        hits.Add((idx, oldName.Length));
                    }
                }
            }
            idx += oldName.Length;
        }
        return hits;
    }

    private static bool IsRefChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@' or ':';

    private static async System.Threading.Tasks.Task ApplyRenameChangesAsync(
        List<RenameFileChanges> changes,
        string newName,
        IDocumentService docs)
    {
        // The rename is an app-initiated write, so suppress the file-watcher for each file (like a
        // normal Save) and update any open buffer in place — instead of a "reloaded from disk"
        // banner/toast, which was misleading for a change the app made itself (#1).
        Services.IWorkspaceSession? session = null;
        try { session = AppServices.Provider.GetService<Services.IWorkspaceSession>(); } catch { /* design-time */ }

        // Encode every file before writing any: one file whose encoding cannot hold the new name stops
        // the whole rename, instead of leaving references to a survey that was not renamed.
        var planned = new List<(RenameFileChanges Changes, string Text, byte[] Bytes, Therion.Workspace.Editing.TextFileFormat Format, ViewModels.Docking.FileDocumentViewModel? Open)>();
        foreach (var fc in changes)
        {
            var sorted = new System.Collections.Generic.List<(int, int)>(fc.Hits);
            sorted.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            var sb = new System.Text.StringBuilder(fc.FileText);
            foreach (var (start, length) in sorted)
                sb.Remove(start, length).Insert(start, newName);

            var newText = sb.ToString();
            var open = docs.Documents.FirstOrDefault(d => string.Equals(d.FilePath, fc.FilePath, System.StringComparison.OrdinalIgnoreCase));
            try
            {
                var (bytes, format) = await System.Threading.Tasks.Task.Run(() => Therion.Workspace.Editing.SourceFileIo.EncodeForSave(fc.FilePath, newText, open?.Format));
                planned.Add((fc, newText, bytes, format, open));
            }
            catch (Therion.Workspace.Editing.UnrepresentableCharacterException ex)
            {
                AppServices.Provider.GetService<INotificationService>()?.Error(ThIDE.Resources.Tr.Get("Notif_SaveEncodingTitle"),
                    string.Format(ThIDE.Resources.Tr.Get("Notif_SaveEncoding"), System.IO.Path.GetFileName(fc.FilePath), ex.Character, ex.EncodingName));
                return;
            }
            catch { /* unreadable: skipped, as a failed write is below */ }
        }

        foreach (var (fc, newText, bytes, format, open) in planned)
        {
            session?.SuppressSelfWrite(fc.FilePath);
            try
            {
                await System.Threading.Tasks.Task.Run(() => Therion.Workspace.Editing.SourceFileIo.WriteOver(fc.FilePath, bytes));
                if (open is not null) open.Format = format;
            }
            catch { continue; }

            foreach (var doc in docs.Documents)
            {
                if (!string.Equals(doc.FilePath, fc.FilePath, System.StringComparison.OrdinalIgnoreCase)) continue;

                // The active file's new text already incorporates its unsaved edits, so update it
                // silently. Any OTHER tab with unsaved edits keeps the conflict banner so those
                // edits aren't discarded; a clean tab just reflects the rename silently.
                if (!ReferenceEquals(doc, docs.Active) && doc.IsDirty)
                    doc.NotifyExternalChange(System.DateTime.Now, deleted: false, autoReload: true);
                else
                    doc.SetText(newText, reparse: true);
                break;
            }
        }
    }

    // Find all references (#4): configure Find-in-Files as a whole-word, project-scoped
    // search for the identifier and run it.
    private void ShowFindReferences(string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return;
        ViewModels.SearchViewModel? vm = null;
        try { vm = AppServices.Provider.GetService<ViewModels.SearchViewModel>(); }
        catch { return; }
        if (vm is null) return;

        vm.Query = term;
        vm.WholeWord = true;
        vm.MatchCase = true;
        vm.UseRegex = false;
        vm.Scope = ViewModels.SearchViewModel.ScopeProject;
        ShowSearch();
        vm.SearchCommand.Execute(null);
    }

    // ----- notifications / toast center --------------------------

    private Avalonia.Controls.Notifications.WindowNotificationManager? _notificationManager;

    private void AttachNotifications(MainWindowViewModel vm)
    {
        _notificationManager = new Avalonia.Controls.Notifications.WindowNotificationManager(this)
        {
            Position = Avalonia.Controls.Notifications.NotificationPosition.BottomRight,
            MaxItems = 4,
        };
        vm.Notifications.Posted += (_, n) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowToast(n));
    }

    // Opening the bell flyout clears the unread badge (the flyout opens from the same click).
    private void OnNotificationsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => (DataContext as MainWindowViewModel)?.MarkNotificationsRead();

    private void ShowToast(Services.AppNotification n)
    {
        if (_notificationManager is null) return;
        var type = n.Kind switch
        {
            Services.NotificationKind.Success => Avalonia.Controls.Notifications.NotificationType.Success,
            Services.NotificationKind.Warning => Avalonia.Controls.Notifications.NotificationType.Warning,
            Services.NotificationKind.Error   => Avalonia.Controls.Notifications.NotificationType.Error,
            _                                 => Avalonia.Controls.Notifications.NotificationType.Information,
        };
        // Errors linger; everything else auto-expires. The whole toast is clickable when the
        // notification carries an action (e.g. "Show output").
        var expiration = n.Kind == Services.NotificationKind.Error ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(6);
        _notificationManager.Show(new Avalonia.Controls.Notifications.Notification(
            n.Title, n.Message, type, expiration, onClick: n.Action));
    }

    // ----- "open with" / file association ------------------------

    private void OpenStartupFileArgs(MainWindowViewModel vm)
    {
        var args = App.StartupFileArgs;
        if (args.Count == 0) return;
        IDocumentService? docs = null;
        try { docs = AppServices.Provider.GetService<IDocumentService>(); }
        catch { /* design-time / no container */ }
        if (docs is null) return;
        foreach (var path in args)
            if (OpenableExtensions.Contains(Path.GetExtension(path)))
                _ = docs.OpenFileAsync(path);
    }

    // ----- drag-and-drop open (#17) --------------------------------------

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer?.TryGetFiles() is { Length: > 0 }
            ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer?.TryGetFiles() is not { } files) return;

        IDocumentService? docs = null;
        try { docs = AppServices.Provider.GetService<IDocumentService>(); }
        catch { /* design-time / no container */ }
        if (docs is null) return;

        foreach (var item in files)
        {
            if (item is not IStorageFile file) continue;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;
            var ext = Path.GetExtension(path);
            if (OpenableExtensions.Contains(ext)) _ = docs.OpenFileAsync(path);
            else if (ImageExtensions.Contains(ext)) ScaffoldScrapForImage(path, docs);
            // Survex/Compass/GPX: the importers, as from the incoming folder.
            else if (DataContext is MainWindowViewModel vm
                     && Therion.Workspace.Incoming.IncomingFiles.Route(path) is Therion.Workspace.Incoming.IncomingRoute.ImportSurvey or Therion.Workspace.Incoming.IncomingRoute.ImportGpx)
                _ = ImportArrivedAsync(vm, path);
        }
    }

    // dropping a scan image scaffolds a .th2 scrap wired to it (Therion's scrap
    // `-sketch <image>`), opens it, and copies the `input` line for the survey's .th.
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".xvi" };

    private void ScaffoldScrapForImage(string imagePath, IDocumentService docs)
    {
        try
        {
            var dir = Path.GetDirectoryName(imagePath);
            if (string.IsNullOrEmpty(dir)) return;
            var id = Path.GetFileNameWithoutExtension(imagePath);
            var th2Path = Path.Combine(dir, id + ".th2");
            if (!File.Exists(th2Path))
                File.WriteAllText(th2Path,
                    Therion.Workspace.Import.Th2Scaffold.NewScrap(id, "plan", Path.GetFileName(imagePath)));
            _ = docs.OpenFileAsync(th2Path);

            var inputLine = Therion.Workspace.Import.Th2Scaffold.InputLine(Path.GetFileName(th2Path));
            Services.ClipboardHelper.SetText(inputLine);
            (DataContext as MainWindowViewModel)?.Notifications.Success(
                "Scrap created", $"Wired '{Path.GetFileName(imagePath)}' to {id}.th2 — '{inputLine}' copied to clipboard.");
        }
        catch { /* best-effort drop */ }
    }

    // ----- UTIL calculators (Tools ▸ Calculators) --------------------------

    private void OnUnitConverter(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new UnitConverterWindow().Show(this);

    private void OnCoordinateConverter(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new CoordinateConverterWindow().Show(this);

    private void OnDeclinationCalculator(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new DeclinationWindow().Show(this);

    private void OnPreferencesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = OpenPreferences(null);

    private async System.Threading.Tasks.Task OpenPreferences(string? section)
    {
        IAppSettingsService? settings = null;
        KeyboardShortcutsViewModel? keyboard = null;
        ILanguageService? language = null;
        SettingsViewModel? externalTools = null;
        FileAssociationsViewModel? associations = null;
        try
        {
            settings = AppServices.Provider.GetService<IAppSettingsService>();
            keyboard = AppServices.Provider.GetService<KeyboardShortcutsViewModel>();
            language = AppServices.Provider.GetService<ILanguageService>();
            externalTools = AppServices.Provider.GetService<SettingsViewModel>();
            associations = AppServices.Provider.GetService<FileAssociationsViewModel>();
        }
        catch { /* design-time / no container */ }
        if (settings is null) return;

        var vm = new PreferencesViewModel(settings, keyboard, language, externalTools, associations);
        if (AppServices.Provider.GetService<IWorkspaceTrustService>() is { } trustStore) vm.AttachTrust(trustStore);
        if (!string.IsNullOrEmpty(section)) vm.SelectSectionById(section!);
        await new PreferencesWindow { DataContext = vm }.ShowDialog(this);
    }

    private void AttachLayout(MainWindowViewModel vm)
    {
        _layout = vm.LayoutService;
        if (_layout is null) return;
        var s = _layout.Current;

        // Restore the normal window bounds first, then re-apply a maximized state on top so
        // un-maximizing returns to the saved size. The dock arrangement itself is restored
        // separately by the DockFactory.
        if (s.WindowWidth > 200) Width = s.WindowWidth;
        if (s.WindowHeight > 200) Height = s.WindowHeight;
        if (s.WindowLeft is double l && s.WindowTop is double t)
        {
            try { Position = new PixelPoint((int)l, (int)t); } catch { }
        }
        if ((WindowState)s.WindowState == WindowState.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveLayout()
    {
        if (_layout is null) return;
        try
        {
            // Fold in the live dock pane sizes + active tabs before saving (#17).
            var persisted = _layout.Current;
            var current = persisted;
            if ((DataContext as MainWindowViewModel)?.Factory is { } factory)
                current = factory.CaptureLayoutState(current);
            // Only capture bounds while in the Normal state — when maximized/fullscreen the
            // reported size is the screen frame, so keeping the last Normal bounds lets a later
            // un-maximize restore the user's chosen size. The state itself is always recorded.
            var next = WindowState == WindowState.Normal
                ? current with
                {
                    WindowWidth = Width,
                    WindowHeight = Height,
                    WindowLeft = Position.X,
                    WindowTop = Position.Y,
                    WindowState = (int)WindowState.Normal,
                }
                : current with { WindowState = (int)WindowState };
            // Dedup against the PERSISTED state, not the freshly-captured one — comparing to
            // `current` swallowed every dock-only change (pane sizes, floats, profile): with an
            // unmoved/maximized window `next` always equaled it, so nothing was ever written.
            // The capture caches (floats/profile return the same instance when unchanged) keep
            // this value comparison cheap and stable across ticks.
            if (next == persisted) return; // value-equal record — nothing to write
            _layout.Save(next);
        }
        catch { /* best-effort */ }
    }

    // ----- continuous persistence (task 2) -------------------------------
    // Save the dock arrangement + window bounds on a timer (and on focus loss / close) so
    // the layout survives even a hard process stop from the debugger.
    private Avalonia.Threading.DispatcherTimer? _autosaveTimer;

    private void StartAutosave()
    {
        if (_autosaveTimer is not null) return;
        _autosaveTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _autosaveTimer.Tick += (_, _) => { PersistLayout(); PersistRecoveryBuffers(); };
        _autosaveTimer.Start();
    }

    /// <summary>writes the currently-dirty editor buffers to the crash-recovery folder.</summary>
    private void PersistRecoveryBuffers()
    {
        if (_crashRecovery is null) return;
        try
        {
            var docs = AppServices.Provider.GetService<IDocumentService>();
            if (docs is null) return;
            var dirty = new List<(string, string)>();
            foreach (var d in docs.Documents)
                if (d.IsDirty && !string.IsNullOrEmpty(d.FilePath))
                    dirty.Add((d.FilePath, d.DocumentText));
            _crashRecovery.SaveBuffers(dirty);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Persists the dock arrangement + window bounds (both deduplicated).</summary>
    private void PersistLayout()
    {
        (DataContext as MainWindowViewModel)?.Factory.SaveLayout();
        SaveLayout();
    }

    /// <summary>Persists layout + the open-document session.</summary>
    private void PersistAll()
    {
        (DataContext as MainWindowViewModel)?.PersistSession();
        PersistLayout();
    }

    private void AttachKeyboardShortcuts(MainWindowViewModel vm)
    {
        // Independent of the keyboard service — connect the MCP bridge first, so the R3 command tools
        // still work if the shortcut service is unavailable and we return early below.
        ConnectMcpBridge(vm);

        try { _shortcuts = AppServices.Provider.GetService<IKeyboardShortcutService>(); }
        catch { _shortcuts = null; }
        if (_shortcuts is null) return;

        // Feed the reactive tooltip source so {l:Tip} toolbar tooltips show — and live-update —
        // the current gesture for each action, and the same for {l:Gesture} menu InputGestures.
        ThIDE.Resources.TipProxy.Instance.Attach(_shortcuts);
        ThIDE.Resources.GestureProxy.Instance.Attach(_shortcuts);

        void Rebuild() => RebuildKeyBindings(vm, _shortcuts);
        _shortcuts.GesturesChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
        Rebuild();

        vm.ShowFindInFilesRequested   += (_, _) => ShowSearch();
        vm.ShowReplaceInFilesRequested += (_, _) => ShowReplace();
        vm.RenameSymbolRequested       += (_, _) => TriggerRenameSymbol();
    }

    /// <summary>
    /// The shell-scoped id→command map. Built once (the command instances are stable) and shared by the
    /// keyboard bindings and the MCP ring-R3 <c>run_command</c> tool (T-03.5), so both reach commands by
    /// the same ids. Caret-scoped editor actions are matched by the focused editor, not listed here;
    /// RenameSymbol appears in both — the editor wins when focused, this covers a focused tool panel.
    /// </summary>
    private Dictionary<string, ICommand?> BuildShellCommandMap(MainWindowViewModel vm)
    {
        _toggleFullScreenCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleFullScreen);
        return new Dictionary<string, ICommand?>(StringComparer.Ordinal)
        {
            [ShellCommandIds.Build]                    = vm.Build.BuildCommand,
            [ShellCommandIds.Rebuild]                  = vm.Build.RebuildCommand,
            [ShellCommandIds.CancelBuild]              = vm.Build.CancelCommand,
            [ShellCommandIds.OpenInLoch]               = vm.Build.OpenInLochCommand,
            [ShellCommandIds.OpenInAven]               = vm.Build.OpenInAvenCommand,
            [ShellCommandIds.ToggleWorkspaceExplorer]  = vm.ToggleWorkspaceExplorerCommand,
            [ShellCommandIds.ToggleDiagnostics]        = vm.ToggleDiagnosticsCommand,
            [ShellCommandIds.Save]                     = vm.SaveCommand,
            [ShellCommandIds.GoBack]                   = vm.GoBackCommand,
            [ShellCommandIds.GoForward]                = vm.GoForwardCommand,
            [ShellCommandIds.FindInFiles]              = vm.ShowFindInFilesCommand,
            [ShellCommandIds.ReplaceInFiles]           = vm.ShowReplaceInFilesCommand,
            [ShellCommandIds.RenameSymbol]             = vm.RenameSymbolCommand,
            [ShellCommandIds.QuickOpen]                = vm.QuickOpenCommand,
            [ShellCommandIds.CommandPalette]           = vm.CommandPaletteCommand,
            [ShellCommandIds.ReopenClosedTab]          = vm.ReopenClosedTabCommand,
            [ShellCommandIds.ToggleFullScreen]         = _toggleFullScreenCommand,
            [ShellCommandIds.NextProblem]              = vm.NextProblemCommand,
            [ShellCommandIds.PreviousProblem]          = vm.PreviousProblemCommand,
            [ShellCommandIds.NewFile]                  = vm.NewFileCommand,
            [ShellCommandIds.OpenFile]                 = vm.OpenFileCommand,
            [ShellCommandIds.OpenFolder]               = vm.OpenFolderCommand,
            [ShellCommandIds.OpenThconfig]             = vm.OpenThconfigCommand,
            [ShellCommandIds.ToggleObjectBrowser]      = vm.ToggleObjectBrowserCommand,
            [ShellCommandIds.ToggleOutline]            = vm.ToggleOutlineCommand,
            [ShellCommandIds.ToggleProject]            = vm.ToggleProjectCommand,
            [ShellCommandIds.ToggleLog]                = vm.ToggleLogCommand,
            [ShellCommandIds.ToggleLivePreview]        = vm.ToggleLivePreviewCommand,
            [ShellCommandIds.ToggleMapViewer]          = vm.ToggleMapViewerCommand,
            [ShellCommandIds.ToggleModel3DViewer]      = vm.ToggleModel3DViewerCommand,
            [ShellCommandIds.ToggleStructuralGeology]  = vm.ToggleStructuralGeologyCommand,
            [ShellCommandIds.ToggleBlenderAnimation]   = vm.ToggleBlenderAnimationCommand,
            [ShellCommandIds.SplitEditor]              = vm.SplitEditorCommand,
            [ShellCommandIds.ResetLayout]              = vm.ResetLayoutCommand,
            [ShellCommandIds.FloatActiveDocument]      = vm.FloatActiveDocumentCommand,
            [ShellCommandIds.QuickExport]              = vm.Build.ShowQuickExportCommand,
            [ShellCommandIds.OpenOutputFolder]         = vm.Build.OpenLastOutputFolderCommand,
            [ShellCommandIds.ToggleWordWrap]           = vm.ToggleWordWrapCommand,
            [ShellCommandIds.NewScrapScaffold]         = vm.NewScrapScaffoldCommand,
            [ShellCommandIds.GenerateReport]           = vm.GenerateReportCommand,
        };
    }

    /// <summary>Connects the in-app MCP bridge to this shell so the guarded R3 tools drive real commands (T-03.5).</summary>
    private void ConnectMcpBridge(MainWindowViewModel vm)
    {
        UiBridge? bridge;
        try { bridge = AppServices.Provider.GetService<Therion.Mcp.IUiBridge>() as UiBridge; }
        catch { bridge = null; }
        if (bridge is null) return;

        var layoutPresets = new Dictionary<string, ICommand?>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"]       = vm.ResetLayoutCommand,
            ["split2"]        = vm.ApplySplitLayout2Command,
            ["split3"]        = vm.ApplySplitLayout3Command,
            ["multi-monitor"] = vm.ApplyMultiMonitorLayoutCommand,
        };
        bridge.ConnectShell(BuildShellCommandMap(vm), vm.SaveAllDirtyAsync, layoutPresets);
    }

    private void RebuildKeyBindings(MainWindowViewModel vm, IKeyboardShortcutService shortcuts)
    {
        var commandMap = BuildShellCommandMap(vm);

        KeyBindings.Clear();
        foreach (var (id, cmd) in commandMap)
        {
            if (cmd is null) continue;
            if (!shortcuts.Gestures.TryGetValue(id, out var gestureText) || string.IsNullOrWhiteSpace(gestureText))
                continue;
            KeyGesture? gesture;
            try { gesture = KeyGesture.Parse(gestureText); }
            catch { continue; }
            if (gesture is null) continue;
            KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = cmd });
        }
    }
}
