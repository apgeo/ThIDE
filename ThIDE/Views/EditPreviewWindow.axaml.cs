using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class EditPreviewWindow : Window
{
    public EditPreviewWindow() => InitializeComponent();

    public EditPreviewWindow(EditPreviewViewModel model) : this() => DataContext = model;

    /// <summary>True when the user chose Apply.</summary>
    public bool Confirmed { get; private set; }

    private void OnApply(object? sender, RoutedEventArgs e) { Confirmed = true; Close(); }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
