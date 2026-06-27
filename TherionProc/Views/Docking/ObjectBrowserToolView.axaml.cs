using Avalonia.Controls;
using Avalonia.Input;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class ObjectBrowserToolView : UserControl
{
    public ObjectBrowserToolView() => InitializeComponent();

    // TH2-03: double-click a row in any entity grid → jump to its source declaration.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: IBrowserNavRow row } &&
            DataContext is ObjectBrowserToolViewModel vm)
            vm.Browser.NavigateTo(row);
    }
}
