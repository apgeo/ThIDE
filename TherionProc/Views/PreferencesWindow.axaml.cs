using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        (DataContext as PreferencesViewModel)?.Apply();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
