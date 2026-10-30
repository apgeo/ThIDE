using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class PasteShotsWindow : Window
{
    public PasteShotsWindow() => InitializeComponent();
    public PasteShotsWindow(PasteShotsViewModel model) : this() => DataContext = model;

    /// <summary>The Therion text to insert, or null when the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    private void OnInsert(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PasteShotsViewModel { CanInsert: true } m) Result = m.Output;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
