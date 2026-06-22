using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class WorkspaceExplorerToolView : UserControl
{
    private WorkspaceExplorerViewModel? _explorer;

    public WorkspaceExplorerToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_explorer is not null) _explorer.PropertyChanged -= OnExplorerPropertyChanged;
        _explorer = (DataContext as WorkspaceExplorerToolViewModel)?.Explorer;
        if (_explorer is not null) _explorer.PropertyChanged += OnExplorerPropertyChanged;
    }

    // Scroll the revealed/selected node into view (#8/#9).
    private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceExplorerViewModel.Selected)) return;
        if (_explorer?.Selected is { } node && this.FindControl<TreeView>("Tree") is { } tree)
            try { tree.ScrollIntoView(node); } catch { /* best-effort */ }
    }

    private void OnNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceExplorerToolViewModel vm && vm.Explorer.Selected is { } node)
            vm.Explorer.ActivateCommand.Execute(node);
    }
}
