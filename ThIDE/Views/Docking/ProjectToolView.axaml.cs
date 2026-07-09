using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

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

    // Pick a directory for the audit's orphan scan to ignore. The picker hangs off the TopLevel, so
    // it lives here rather than in the view-model.
    private async void OnAddAuditExclusion(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ProjectToolViewModel vm) return;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = ThIDE.Resources.Tr.Get("Proj_Audit_AddExclusion"),
            AllowMultiple = false,
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            vm.Audit.AddExclusion(path);
    }
}
