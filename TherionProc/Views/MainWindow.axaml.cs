using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using TherionProc.Services;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> OpenableExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".th", ".th2", ".thconfig", ".thc", "" };

    private IKeyboardShortcutService? _shortcuts;
    private ILayoutService? _layout;
    private IGlobalHotkeyService? _globalHotkey;
    private ICrashRecoveryService? _crashRecovery;

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
                vm.Build.QuickExportRequested += (_, _) => _ = ShowQuickExportAsync(vm);
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
                OpenStartupFileArgs(vm);                 // "open with" / file association
                // mirror the active editor's selection stats into the status bar.
                TherionProc.Editor.TherionTextEditor.SelectionStatsChanged += (_, s) =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.SetSelectionStats(s.Chars, s.Lines));
            }
            try
            {
                if (AppServices.Provider.GetService<IDocumentService>() is { } docs)
                {
                    docs.FindReferencesRequested += (_, term) => ShowFindReferences(term);
                    docs.RenameSymbolRequested += (_, args) => _ = HandleRenameSymbolAsync(args.Raw, args.Kind);
                }
                _crashRecovery = AppServices.Provider.GetService<ICrashRecoveryService>();
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

    // a recent/pinned file entry — opens on click; right-click pins or unpins it.
    private static MenuItem RecentItem(MainWindowViewModel vm, string path, bool isPinned)
    {
        var item = new MenuItem
        {
            Header = (isPinned ? "📌 " : string.Empty) + Path.GetFileName(path),
            Command = vm.OpenRecentCommand,
            CommandParameter = path,
            [ToolTip.TipProperty] = path,
        };
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
            items.Add(new MenuItem
            {
                Header = RecentDirectoryLabel(dir),
                Command = vm.OpenRecentDirectoryCommand,
                CommandParameter = dir,
                [ToolTip.TipProperty] = dir,
            });
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
    private static TherionProc.Editor.TherionTextEditor? ActiveEditor
    {
        get
        {
            try
            {
                var path = AppServices.Provider.GetService<IDocumentService>()?.Active?.FilePath;
                if (TherionProc.Editor.TherionTextEditor.ForPath(path) is { } ed) return ed;
            }
            catch { /* design-time / no container */ }
            return TherionProc.Editor.TherionTextEditor.LastFocused;
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
        var ok = new Button { Content = TherionProc.Resources.Tr.Get("Ctx_Open"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = TherionProc.Resources.Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        var dialog = new Window
        {
            Title = TherionProc.Resources.Tr.Get("Dlg_LoadFolderTitle"),
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
                    new TextBlock { Text = TherionProc.Resources.Tr.Get("Dlg_LoadFolderMsg") + "\n\n" + path, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancel, ok } },
                },
            },
        };
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.Alt:
                (DataContext as MainWindowViewModel)?.GoBackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.Alt:
                (DataContext as MainWindowViewModel)?.GoForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S when e.KeyModifiers == KeyModifiers.Control:
                (DataContext as MainWindowViewModel)?.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                ShowSearch();
                e.Handled = true;
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.Control:
                (DataContext as MainWindowViewModel)?.ShowQuickOpen();
                e.Handled = true;
                break;
            case Key.P when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                (DataContext as MainWindowViewModel)?.ShowCommandPalette();
                e.Handled = true;
                break;
            // reopen the most-recently-closed tab (VSCode Ctrl+Shift+T).
            case Key.T when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                (DataContext as MainWindowViewModel)?.ReopenClosedTabCommand.Execute(null);
                e.Handled = true;
                break;
            // F8 / Shift+F8 jump to the next / previous diagnostic.
            case Key.F8:
                (DataContext as MainWindowViewModel)?.Diagnostics.GoToAdjacent((e.KeyModifiers & KeyModifiers.Shift) == 0);
                e.Handled = true;
                break;
        }
    }

    // ----- quick export dialog --------------------------------
    private async System.Threading.Tasks.Task ShowQuickExportAsync(MainWindowViewModel vm)
    {
        var dialog = new QuickExportWindow();
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;
        var m = dialog.Model;
        await vm.Build.RunQuickExportAsync(m.ComposeBlock(), m.OutputFileName);
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

        var newName = await new InputDialog("Rename Symbol",
            $"New name for '{oldName}':", oldName).ShowAsync(this);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == oldName) return;
        newName = newName.Trim();
        if (Therion.Syntax.TherionIdentifiers.FirstIllegalChar(newName) is { } badChar)
        {
            await new MessageDialog("Rename Symbol",
                $"'{newName}' is not a valid Therion name — it contains '{badChar}'.").ShowAsync(this);
            return;
        }

        // Stations rename via the true symbol occurrence index (scope-correct, @-aware, comment-free);
        // survey/scrap/map still use the legacy text-scan until they get an occurrence index.
        var changes = kind == Therion.Processing.Abstractions.ReferenceKind.Station
            ? (CollectStationRenameChanges(workspace, targetSpan.Value)
               ?? CollectRenameChanges(workspace, nav, raw, kind, oldName, targetSpan.Value))
            : CollectRenameChanges(workspace, nav, raw, kind, oldName, targetSpan.Value);
        if (changes.Count == 0) return;

        if (settings?.Current.ShowRenamePreviewBeforeApply == true)
        {
            _renamePreviewWindow?.Close();
            _renamePreviewWindow = new RenamePreviewWindow(changes, oldName, newName);
            _renamePreviewWindow.Closed += (_, _) => _renamePreviewWindow = null;
            var apply = await _renamePreviewWindow.ShowDialog<bool>(this);
            if (!apply) return;
        }

        await ApplyRenameChangesAsync(changes, newName, docs!);
    }

    /// <summary>
    /// True rename for a station: replaces exactly the token-level occurrences the semantic
    /// occurrence index attributes to the clicked station (scope-correct, <c>@</c>-aware, comment-free,
    /// cross-file). Returns null when the target isn't a resolvable station (caller falls back).
    /// </summary>
    private static List<RenameFileChanges>? CollectStationRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace, Therion.Core.SourceSpan targetSpan)
    {
        var edits = Therion.Semantics.StationRenamePlan.Compute(workspace, targetSpan,
            path => { try { return System.IO.File.ReadAllText(path); } catch { return null; } });
        if (edits.Count == 0) return null;
        return edits
            .Select(e => new RenameFileChanges(e.FilePath, e.FileText, e.Spans.ToList()))
            .ToList();
    }

    /// <summary>Scans every workspace file for ref tokens resolving to the same declaration span.</summary>
    private static List<RenameFileChanges> CollectRenameChanges(
        Therion.Semantics.WorkspaceSemanticModel workspace,
        Therion.Processing.Abstractions.ISymbolNavigationService nav,
        string raw,
        Therion.Processing.Abstractions.ReferenceKind kind,
        string oldName,
        Therion.Core.SourceSpan targetSpan)
    {
        var result = new List<RenameFileChanges>();
        foreach (var filePath in workspace.PerFile.Keys)
        {
            string text;
            try { text = System.IO.File.ReadAllText(filePath); }
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
                var tok = TherionProc.Editor.TherionTextEditor.ExtractRefTokenStatic(text, mid);
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
        foreach (var fc in changes)
        {
            var sorted = new System.Collections.Generic.List<(int, int)>(fc.Hits);
            sorted.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            var sb = new System.Text.StringBuilder(fc.FileText);
            foreach (var (start, length) in sorted)
                sb.Remove(start, length).Insert(start, newName);

            var newText = sb.ToString();
            try { await System.IO.File.WriteAllTextAsync(fc.FilePath, newText); }
            catch { continue; }

            foreach (var doc in docs.Documents)
            {
                if (string.Equals(doc.FilePath, fc.FilePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    doc.NotifyExternalChange(System.DateTime.Now, deleted: false, autoReload: true);
                    break;
                }
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
            var current = _layout.Current;
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
            if (next == current) return; // value-equal record — nothing to write
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
        try { _shortcuts = AppServices.Provider.GetService<IKeyboardShortcutService>(); }
        catch { _shortcuts = null; }
        if (_shortcuts is null) return;

        void Rebuild() => RebuildKeyBindings(vm, _shortcuts);
        _shortcuts.GesturesChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
        Rebuild();

        vm.ShowFindInFilesRequested   += (_, _) => ShowSearch();
        vm.ShowReplaceInFilesRequested += (_, _) => ShowReplace();
        vm.RenameSymbolRequested       += (_, _) => TriggerRenameSymbol();
    }

    private void RebuildKeyBindings(MainWindowViewModel vm, IKeyboardShortcutService shortcuts)
    {
        var commandMap = new Dictionary<string, ICommand?>(StringComparer.Ordinal)
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
        };

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
