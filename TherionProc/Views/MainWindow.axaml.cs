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
                    docs.FindReferencesRequested += (_, term) => ShowFindReferences(term);
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
        try { settings = AppServices.Provider.GetService<IAppSettingsService>(); }
        catch { /* design-time / no container */ }
        if (settings is null) return;

        var window = new PreferencesWindow { DataContext = new PreferencesViewModel(settings) };
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
