using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class ProjectToolView : UserControl
{
    public ProjectToolView() => InitializeComponent();

    // Double-click a lead row to jump to its source (replaces the old "Go to source" button).
    private void OnLeadsRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectToolViewModel vm &&
            (e.Source as Visual)?.FindAncestorOfType<DataGridRow>()?.DataContext is LeadRow row)
            vm.Leads.OpenCommand.Execute(row);
    }

    // Double-click a TODO row to jump to its source (replaces the old "Go to source" button).
    private void OnTodosRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectToolViewModel vm &&
            (e.Source as Visual)?.FindAncestorOfType<DataGridRow>()?.DataContext is TodoRow row)
            vm.Todos.OpenCommand.Execute(row);
    }

    // Double-click an entrance / fixed-point row to jump to its declaration.
    private void OnEntrancesRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectToolViewModel vm &&
            (e.Source as Visual)?.FindAncestorOfType<DataGridRow>()?.DataContext is FixedRow row)
            vm.Analytics.OpenFixedPointCommand.Execute(row);
    }
}
