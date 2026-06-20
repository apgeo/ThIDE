using Avalonia.Controls;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class DiagnosticsToolView : UserControl
{
    public DiagnosticsToolView() => InitializeComponent();

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsToolViewModel vm && vm.Diagnostics.Selected is { } row)
            vm.Diagnostics.NavigateCommand.Execute(row);
    }
}
