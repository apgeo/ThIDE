using System;
using System.Collections.Generic;
using System.IO;
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

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.AttachStoragePicker(new AvaloniaStoragePicker(this));
                AttachLayout(vm);
                AttachKeyboardShortcuts(vm);
                vm.RecentFilesChanged += (_, _) => BuildRecentMenu(vm);
                BuildRecentMenu(vm);
                // The layout rendered without crashing — clear the crash sentinel so the next
                // launch trusts it (deferred so the dock has finished materializing).
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => vm.Factory.ConfirmLayoutLoaded(),
                    Avalonia.Threading.DispatcherPriority.Background);
                StartAutosave();
            }
            try
            {
                if (AppServices.Provider.GetService<IDocumentService>() is { } docs)
                {
                    docs.FindReferencesRequested += (_, term) => ShowFindReferences(term);
                    docs.RenameSymbolRequested += (_, args) => _ = HandleRenameSymbolAsync(args.Raw, args.Kind);
                }
            }
            catch { /* design-time / no container */ }
        };

        // Persist when focus leaves the app — covers a debugger stop that never fires Closing.
        Deactivated += (_, _) => PersistAll();
        Closing += (_, _) => { _autosaveTimer?.Stop(); PersistAll(); };

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

        var recent = vm.RecentFiles;
        for (int g = 0; g < RecentGroups.Length; g++)
        {
            var (_, exts) = RecentGroups[g];
            var groupFiles = new List<string>();
            foreach (var path in recent)
            {
                var ext = Path.GetExtension(path);
                bool inGroup = exts.Length == 0
                    ? !RecentGroupedExts.Contains(ext)         // "Other": anything not in a named group
                    : Array.Exists(exts, x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
                if (inGroup) groupFiles.Add(path);
                if (groupFiles.Count == 8) break;              // cap per type (#8)
            }
            if (groupFiles.Count == 0) continue;

            if (items.Count > 0) items.Add(new Separator());
            foreach (var path in groupFiles)
            {
                items.Add(new MenuItem
                {
                    Header = Path.GetFileName(path),
                    Command = vm.OpenRecentCommand,
                    CommandParameter = path,
                    [ToolTip.TipProperty] = path,
                });
            }
        }

        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
        menu.ItemsSource = items;
    }

    private static readonly HashSet<string> RecentGroupedExts =
        new(StringComparer.OrdinalIgnoreCase) { ".thconfig", ".thc", ".th", ".th2" };

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

    private static TherionProc.Editor.TherionTextEditor? ActiveEditor =>
        TherionProc.Editor.TherionTextEditor.LastFocused;

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
        }
    }

    // ----- global search window (#3) -------------------------------------

    private SearchWindow? _searchWindow;

    private void OnSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowSearch();

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
    }

    // ----- replace in files window (#9) ----------------------------------

    private ReplaceWindow? _replaceWindow;

    private void ShowReplace()
    {
        if (_replaceWindow is { } w && w.IsVisible) { w.Activate(); return; }

        ViewModels.ReplaceInFilesViewModel? vm = null;
        try { vm = AppServices.Provider.GetService<ViewModels.ReplaceInFilesViewModel>(); }
        catch { /* design-time / no container */ }
        if (vm is null) return;

        _replaceWindow = new ReplaceWindow { DataContext = vm };
        _replaceWindow.Closed += (_, _) => _replaceWindow = null;
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

        var changes = CollectRenameChanges(workspace, nav, raw, kind, oldName, targetSpan.Value);
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
            if (OpenableExtensions.Contains(Path.GetExtension(path)))
                _ = docs.OpenFileAsync(path);
        }
    }

    private async void OnPreferencesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IAppSettingsService? settings = null;
        KeyboardShortcutsViewModel? keyboard = null;
        ILanguageService? language = null;
        SettingsViewModel? externalTools = null;
        try
        {
            settings = AppServices.Provider.GetService<IAppSettingsService>();
            keyboard = AppServices.Provider.GetService<KeyboardShortcutsViewModel>();
            language = AppServices.Provider.GetService<ILanguageService>();
            externalTools = AppServices.Provider.GetService<SettingsViewModel>();
        }
        catch { /* design-time / no container */ }
        if (settings is null) return;

        var window = new PreferencesWindow
        {
            DataContext = new PreferencesViewModel(settings, keyboard, language, externalTools),
        };
        await window.ShowDialog(this);
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
            var current = _layout.Current;
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
        _autosaveTimer.Tick += (_, _) => PersistLayout();
        _autosaveTimer.Start();
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
