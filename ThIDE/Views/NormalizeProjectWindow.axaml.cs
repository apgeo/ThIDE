using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class NormalizeProjectWindow : Window
{
    public NormalizeProjectWindow() => InitializeComponent();
    public NormalizeProjectWindow(NormalizeProjectViewModel model) : this() => DataContext = model;

    /// <summary>True when the user went on to the preview.</summary>
    public bool Confirmed { get; private set; }

    private void OnNext(object? sender, RoutedEventArgs e) { Confirmed = true; Close(); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
