using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class BatchBuildWindow : Window
{
    public BatchBuildWindow()
    {
        InitializeComponent();
        // The check boxes are templated rows, so focus the first one once they exist.
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            this.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(c => c.IsEffectivelyEnabled)?.Focus());
    }

    public BatchBuildWindow(BatchBuildViewModel model) : this() => DataContext = model;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
