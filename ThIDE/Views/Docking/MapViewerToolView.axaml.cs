using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class MapViewerToolView : UserControl
{
    private MapViewerViewModel? _map;

    public MapViewerToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // Rewire the ImageLoaded subscription whenever the bound VM changes so a freshly rendered
    // page auto-fits to the window (#4/#6).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_map is not null) _map.ImageLoaded -= OnImageLoaded;
        _map = (DataContext as MapViewerToolViewModel)?.Map;
        if (_map is not null) _map.ImageLoaded += OnImageLoaded;
    }

    // A new image arrived: shrink-to-fit once layout has run (viewport size is known then).
    private void OnImageLoaded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => FitToViewport(shrinkOnly: true), DispatcherPriority.Loaded);

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not MapViewerToolViewModel vm) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ThIDE.Resources.Tr.Get("Pick_OpenMap"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_MapsFilter"))
                {
                    Patterns = new[] { "*.pdf", "*.svg", "*.png", "*.jpg", "*.jpeg", "*.bmp" },
                },
            },
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) vm.Map.Load(path);
    }

    // Window controls (#7): forwarded to the shell, which drives the DockFactory so they work
    // whether the panel is docked or already floating.
    private void OnFullScreen(object? sender, RoutedEventArgs e) =>
        (DataContext as MapViewerToolViewModel)?.RequestFullScreen();

    private void OnFloatOtherScreen(object? sender, RoutedEventArgs e) =>
        (DataContext as MapViewerToolViewModel)?.RequestFloatOtherScreen();

    private void OnMoveToCenter(object? sender, RoutedEventArgs e) =>
        (DataContext as MapViewerToolViewModel)?.RequestMoveToCenter();

    // "Fit" button (#4): shrink the page so it fits the visible viewport, keeping aspect ratio,
    // but never enlarge past 100%. Same routine drives the auto-fit on open.
    private void OnFit(object? sender, RoutedEventArgs e) => FitToViewport(shrinkOnly: true);

    // Scale so the whole image fits, minus the 12px margin around it on each side. Uses the
    // bitmap's DIP size (what the Image shows at Stretch="None"), which the LayoutTransform then
    // scales by Zoom. With shrinkOnly, the result is capped at 1.0 so a small map is not blown up.
    private void FitToViewport(bool shrinkOnly)
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
        if (shrinkOnly) fit = System.Math.Min(1.0, fit);
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
