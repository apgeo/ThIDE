using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TherionProc.ViewModels.QuickPick;

namespace TherionProc.Views.QuickPick;

public partial class QuickPickOverlay : UserControl
{
    public QuickPickOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        var box = this.FindControl<TextBox>("SearchBox");
        if (box is not null) box.KeyDown += OnSearchKeyDown;

        var list = this.FindControl<ListBox>("ResultList");
        if (list is not null)
            list.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel);

        if (this.FindControl<Grid>("Backdrop") is { } backdrop)
            backdrop.PointerPressed += OnBackdropPressed;
        if (this.FindControl<Border>("Box") is { } boxBorder)
            boxBorder.PointerPressed += (_, e) => e.Handled = true; // clicks inside don't dismiss
    }

    private QuickPickViewModel? Vm => DataContext as QuickPickViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Focus the search box (and select all) whenever a palette is shown.
        if (Vm is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.FindControl<TextBox>("SearchBox");
            box?.Focus();
            box?.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null) return;
        switch (e.Key)
        {
            case Key.Down: Vm.MoveDown(); e.Handled = true; break;
            case Key.Up:   Vm.MoveUp();   e.Handled = true; break;
            case Key.Enter: Vm.Accept();  e.Handled = true; break;
            case Key.Escape: Vm.Close();  e.Handled = true; break;
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A click on a row accepts it (selection has already updated via the tunnel order).
        if (e.InitialPressMouseButton == MouseButton.Left &&
            (e.Source as Control)?.DataContext is QuickPickItem)
            Vm?.Accept();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e) => Vm?.Close();
}
