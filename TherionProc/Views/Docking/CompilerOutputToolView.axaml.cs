using Avalonia.Controls;
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
}
