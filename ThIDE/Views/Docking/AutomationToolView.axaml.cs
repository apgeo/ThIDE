using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class AutomationToolView : UserControl
{
    public AutomationToolView() => InitializeComponent();

    private AutomationOutputViewModel? Vm => (DataContext as AutomationToolViewModel)?.Automation;

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(vm.SelectedText());
    }

    // Double-click a `file:line` row to open it.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is AutomationRow row) Vm?.Navigate(row);
    }
}
