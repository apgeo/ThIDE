using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class ScaffoldProjectWindow : Window
{
    public ScaffoldProjectWindow() : this(new ScaffoldProjectViewModel()) { }

    public ScaffoldProjectWindow(ScaffoldProjectViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    /// <summary>Set to true when the user confirmed (so the caller writes the project).</summary>
    public bool Confirmed { get; private set; }

    public ScaffoldProjectViewModel Model => (ScaffoldProjectViewModel)DataContext!;

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
