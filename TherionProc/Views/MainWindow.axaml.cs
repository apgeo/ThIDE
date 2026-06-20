using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using TherionProc.Services;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class MainWindow : Window
{
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

        Closing += (_, _) => SaveLayout();
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
