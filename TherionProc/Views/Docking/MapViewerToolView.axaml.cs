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

    // Fit the current page to the visible viewport: scale so the whole image fits, minus the
    // 12px margin around it on each side. Uses the bitmap's DIP size (what the Image shows at
    // Stretch="None"), which the LayoutTransform then scales by Zoom.
    private void OnFitPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapViewerToolViewModel vm || vm.Map.Image is not { } bmp) return;
        var scroll = this.FindControl<ScrollViewer>("Scroll");
        if (scroll is null) return;
        const double margin = 12 * 2;
        double availW = scroll.Viewport.Width - margin;
        double availH = scroll.Viewport.Height - margin;
        var size = bmp.Size;
        if (availW <= 0 || availH <= 0 || size.Width <= 0 || size.Height <= 0) return;
        double fit = System.Math.Min(availW / size.Width, availH / size.Height);
        vm.Map.Zoom = System.Math.Clamp(System.Math.Round(fit, 3), 0.1, 8.0);
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
