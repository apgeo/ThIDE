using Avalonia.Controls;

namespace ThIDE.Views.Pickers;

public partial class StationSelectorView : UserControl
{
    public StationSelectorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ThIDE.ViewModels.Pickers.StationSelectorViewModel vm)
                vm.MaximizeRequested += OnMaximize;
        };
    }

    private void OnMaximize(object? sender, System.EventArgs e)
    {
        if (sender is ThIDE.ViewModels.Pickers.StationSelectorViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            PreviewWindow.ShowPreview(owner, vm.Preview, ThIDE.Resources.Tr.Get("Tool_LivePreview"));
    }
}
