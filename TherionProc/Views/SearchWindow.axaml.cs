using Avalonia.Controls;
using Avalonia.Input;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class SearchWindow : Window
{
    public SearchWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            (DataContext as SearchViewModel)?.PrepareDefaults();
            this.FindControl<TextBox>("QueryBox")?.Focus();
        };
    }

    private void OnResultActivated(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchViewModel vm && vm.Selected is { } hit)
            vm.ActivateCommand.Execute(hit);
    }
}
