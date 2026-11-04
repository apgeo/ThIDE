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
        // The check boxes are templated rows, so focus the first one in the list once they exist
        // (searching the whole window would find the "stop on first failure" box below the list).
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            this.FindControl<ItemsControl>("ConfigList")?.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => c.IsEffectivelyEnabled)?.Focus(),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public BatchBuildWindow(BatchBuildViewModel model) : this() => DataContext = model;

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
