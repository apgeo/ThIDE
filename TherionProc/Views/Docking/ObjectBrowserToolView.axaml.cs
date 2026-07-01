using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class ObjectBrowserToolView : UserControl
{
    public ObjectBrowserToolView() => InitializeComponent();

    // The grid the context menu was opened on (its SelectedItem is the target row).
    private DataGrid? _ctxGrid;

    // double-click a row in any entity grid → jump to its source declaration.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: IBrowserNavRow row } &&
            DataContext is ObjectBrowserToolViewModel vm)
            vm.Browser.NavigateTo(row);
    }

    // remember which grid was right-clicked so the menu handlers know the target row.
    private void OnGridContextRequested(object? sender, ContextRequestedEventArgs e)
        => _ctxGrid = sender as DataGrid;

    private IBrowserNavRow? CtxRow => _ctxGrid?.SelectedItem as IBrowserNavRow;
    private ObjectBrowserViewModel? Browser => (DataContext as ObjectBrowserToolViewModel)?.Browser;

    private void OnCtxNavigate(object? sender, RoutedEventArgs e) { if (CtxRow is { } r) Browser?.NavigateTo(r); }
    private void OnCtxFindRefs(object? sender, RoutedEventArgs e) { if (CtxRow is { } r) Browser?.FindReferences(r); }
    private void OnCtxRename(object? sender, RoutedEventArgs e) { if (CtxRow is { } r) Browser?.RenameSymbol(r); }
    private void OnCtxCopyQName(object? sender, RoutedEventArgs e) { if (CtxRow is { } r) Browser?.CopyQualifiedName(r); }
}
