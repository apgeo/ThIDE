using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using Therion.Processing.Abstractions;
using TherionProc.Editor;
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
                var editor = this.FindControl<TherionTextEditor>("Editor");
                if (editor is not null)
                {
                    vm.NavigateToSpanRequested += (_, span) => editor.ScrollTo(span);
                }

                var diagGrid = this.FindControl<DataGrid>("DiagnosticsGrid");
                if (diagGrid is not null)
                {
                    diagGrid.DoubleTapped += (_, _) =>
                    {
                        if (vm.Diagnostics.Selected is { } row)
                            vm.Diagnostics.NavigateCommand.Execute(row);
                    };
                }

                var outGrid = this.FindControl<DataGrid>("CompilerOutputGrid");
                if (outGrid is not null)
                {
                    outGrid.DoubleTapped += (_, _) =>
                    {
                        if (vm.Build.SelectedOutput is { } row)
                            vm.Build.NavigateOutputCommand.Execute(row);
                    };
                }

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

        // Restore window bounds (Plan §7.2 / M6 #1).
        if (s.WindowWidth > 200) Width = s.WindowWidth;
        if (s.WindowHeight > 200) Height = s.WindowHeight;
        if (s.WindowLeft is double l && s.WindowTop is double t)
        {
            try { Position = new PixelPoint((int)l, (int)t); } catch { }
        }

        var shell = this.FindControl<Grid>("ShellGrid");
        if (shell is not null && shell.ColumnDefinitions.Count >= 1 && s.LeftPaneWidth > 0)
            shell.ColumnDefinitions[0].Width = new GridLength(s.LeftPaneWidth);

        var body = this.FindControl<Grid>("BodyGrid");
        if (body is not null && body.RowDefinitions.Count >= 3 && s.BottomPaneHeight > 0)
            body.RowDefinitions[2].Height = new GridLength(s.BottomPaneHeight);
    }

    private void SaveLayout()
    {
        if (_layout is null) return;
        try
        {
            var shell = this.FindControl<Grid>("ShellGrid");
            var body = this.FindControl<Grid>("BodyGrid");
            var current = _layout.Current;
            var next = current with
            {
                LeftPaneWidth = shell is not null && shell.ColumnDefinitions.Count > 0 && shell.ColumnDefinitions[0].Width.IsAbsolute
                    ? shell.ColumnDefinitions[0].Width.Value
                    : current.LeftPaneWidth,
                BottomPaneHeight = body is not null && body.RowDefinitions.Count >= 3 && body.RowDefinitions[2].Height.IsAbsolute
                    ? body.RowDefinitions[2].Height.Value
                    : current.BottomPaneHeight,
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
        // Mapping from logical command ID (Plan §9bis.5a) to the actual ICommand on the VM.
        var commandMap = new Dictionary<string, ICommand?>(StringComparer.Ordinal)
        {
            [ShellCommandIds.Build]                    = vm.Build.BuildCommand,
            [ShellCommandIds.Rebuild]                  = vm.Build.RebuildCommand,
            [ShellCommandIds.CancelBuild]              = vm.Build.CancelCommand,
            [ShellCommandIds.OpenInLoch]               = vm.Build.OpenInLochCommand,
            [ShellCommandIds.OpenInAven]               = vm.Build.OpenInAvenCommand,
            [ShellCommandIds.ToggleWorkspaceExplorer]  = vm.ToggleWorkspaceExplorerCommand,
            [ShellCommandIds.ToggleDiagnostics]        = vm.ToggleDiagnosticsCommand,
            // GoToDefinition / FindReferences are editor-scoped (handled inside TherionTextEditor).
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
