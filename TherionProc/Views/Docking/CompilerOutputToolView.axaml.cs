using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class CompilerOutputToolView : UserControl
{
    public CompilerOutputToolView() => InitializeComponent();

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CompilerOutputToolViewModel vm && vm.Build.SelectedOutput is { } row)
            vm.Build.NavigateOutputCommand.Execute(row);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) =>
        this.FindControl<DataGrid>("Grid")?.SelectAll();

    private void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CompilerOutputToolViewModel vm) return;
        var text = vm.Build.OutputAsText();
        if (string.IsNullOrEmpty(text)) return;
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        _ = topLevel?.Clipboard?.SetTextAsync(text);
    }
}
