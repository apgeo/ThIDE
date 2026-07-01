using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class MapViewerToolView : UserControl
{
    public MapViewerToolView() => InitializeComponent();

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not MapViewerToolViewModel vm) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = TherionProc.Resources.Tr.Get("Pick_OpenMap"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(TherionProc.Resources.Tr.Get("Pick_MapsFilter"))
                {
                    Patterns = new[] { "*.pdf", "*.svg", "*.png", "*.jpg", "*.jpeg", "*.bmp" },
                },
            },
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.Map.Load(path);
    }

    // Ctrl+wheel zooms the map (like the relational map).
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0 || DataContext is not MapViewerToolViewModel vm) return;
        if (e.Delta.Y > 0) vm.Map.ZoomInCommand.Execute(null);
        else if (e.Delta.Y < 0) vm.Map.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }
}
