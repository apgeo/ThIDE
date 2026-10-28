using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class BatchBuildWindow : Window
{
    public BatchBuildWindow() => InitializeComponent();

    public BatchBuildWindow(BatchBuildViewModel model) : this() => DataContext = model;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
