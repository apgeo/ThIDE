using Avalonia.Controls;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class QuickExportWindow : Window
{
    public QuickExportWindow()
    {
        InitializeComponent();
        DataContext = new QuickExportViewModel();
    }

    /// <summary>Set to true when the user confirmed (so the caller runs the export).</summary>
    public bool Confirmed { get; private set; }

    public QuickExportViewModel Model => (QuickExportViewModel)DataContext!;

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
