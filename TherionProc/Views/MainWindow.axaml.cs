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
            }
        };

        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.PersistSession();
            SaveLayout();
        };

        // Drag-and-drop file open (#17).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

        // Restore window bounds. The dock arrangement itself is rebuilt fresh each
        // launch (full Dock layout serialization is a follow-up).
        if (s.WindowWidth > 200) Width = s.WindowWidth;
        if (s.WindowHeight > 200) Height = s.WindowHeight;
        if (s.WindowLeft is double l && s.WindowTop is double t)
        {
            try { Position = new PixelPoint((int)l, (int)t); } catch { }
        }
    }

    private void SaveLayout()
    {
        if (_layout is null) return;
        try
        {
            var current = _layout.Current;
            var next = current with
            {
                WindowWidth = Width,
                WindowHeight = Height,
                WindowLeft = Position.X,
                WindowTop = Position.Y,
            };
            _layout.Save(next);
        }
        catch { /* best-effort */ }
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
