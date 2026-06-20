using Avalonia.Controls;
using Avalonia.Interactivity;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class WorkspaceExplorerToolView : UserControl
{
    public WorkspaceExplorerToolView() => InitializeComponent();

    private void OnNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceExplorerToolViewModel vm && vm.Explorer.Selected is { } node)
            vm.Explorer.OpenCommand.Execute(node);
    }
}
