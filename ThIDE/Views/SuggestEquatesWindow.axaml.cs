using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class SuggestEquatesWindow : Window
{
    public SuggestEquatesWindow() => InitializeComponent();
    public SuggestEquatesWindow(SuggestEquatesViewModel model) : this() => DataContext = model;

    /// <summary>The pair chosen with Link…, or null.</summary>
    public EquateCandidateRow? Chosen { get; private set; }

    private void OnLink(object? sender, RoutedEventArgs e)
    {
        Chosen = (DataContext as SuggestEquatesViewModel)?.Selected;
        Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
