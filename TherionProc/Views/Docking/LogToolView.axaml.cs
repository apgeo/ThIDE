using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class LogToolView : UserControl
{
    public LogToolView() => InitializeComponent();

    private TextBox? Box => this.FindControl<TextBox>("LogBox");

    // Auto-scroll to the newest line as the log grows.
    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(
            () => Box?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Box is not { } box) return;
        SetClipboard(string.IsNullOrEmpty(box.SelectedText) ? box.Text : box.SelectedText);
    }

    private void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (Box is { } box) SetClipboard(box.Text);
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogToolViewModel vm) vm.Log.ClearCommand.Execute(null);
    }

    private void SetClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) _ = clipboard.SetTextAsync(text);
    }
}
