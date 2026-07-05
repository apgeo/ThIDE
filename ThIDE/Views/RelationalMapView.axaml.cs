// Code-behind for the Relational Map: wires the edge layer to the view model and implements
// node dragging (mouse) + double-click-to-open-source. Edges are redrawn live while dragging.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class RelationalMapView : UserControl
{
    private RelationalMapViewModel? _vm;
    private RelationalNode? _drag;
    private Point _dragOffset;

    public RelationalMapView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;

        if (this.FindControl<ItemsControl>("NodesHost") is { } host)
        {
            host.PointerPressed += OnNodePointerPressed;
            host.PointerMoved += OnNodePointerMoved;
            host.PointerReleased += OnNodePointerReleased;
            host.DoubleTapped += OnNodeDoubleTapped;
        }

        // Ctrl+scroll to zoom (#2): tunnel so it runs before the ScrollViewer scrolls.
        if (this.FindControl<ScrollViewer>("DiagramScroll") is { } scroll)
            scroll.AddHandler(PointerWheelChangedEvent, OnWheelZoom, RoutingStrategies.Tunnel);
    }

    private void OnWheelZoom(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if (e.Delta.Y > 0) _vm.ZoomInCommand.Execute(null);
        else if (e.Delta.Y < 0) _vm.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }

    // Fit the whole diagram into the viewport (#2).
    private void OnFitClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || this.FindControl<ScrollViewer>("DiagramScroll") is not { } sv) return;
        double vpW = sv.Viewport.Width > 0 ? sv.Viewport.Width : sv.Bounds.Width;
        double vpH = sv.Viewport.Height > 0 ? sv.Viewport.Height : sv.Bounds.Height;
        if (vpW <= 0 || vpH <= 0 || _vm.CanvasWidth <= 0 || _vm.CanvasHeight <= 0) return;
        double z = Math.Min(vpW / _vm.CanvasWidth, vpH / _vm.CanvasHeight);
        _vm.Zoom = Math.Clamp(z * 0.97, 0.15, 4.0);
        // Changing the zoom re-lays-out the scaled diagram, but the ScrollViewer keeps its previous
        // offset — which now points past the shrunken content, leaving the pane blank (#3). Snap back
        // to the top-left once the new (smaller) extent has been measured so the diagram is in view.
        Dispatcher.UIThread.Post(() =>
        {
            sv.Offset = new Vector(0, 0);
            InvalidateEdges();
        }, DispatcherPriority.Loaded);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.GraphChanged -= OnGraphChanged;
        _vm = DataContext as RelationalMapViewModel;
        if (_vm is not null && this.FindControl<RelationalEdgesControl>("EdgeLayer") is { } layer)
        {
            layer.Configure(_vm, span => _vm.Navigate(span));
            _vm.GraphChanged += OnGraphChanged;
        }
    }

    private void OnGraphChanged(object? sender, EventArgs e) => InvalidateEdges();

    private void InvalidateEdges() =>
        this.FindControl<RelationalEdgesControl>("EdgeLayer")?.InvalidateVisual();

    // ---- node dragging ------------------------------------------------------

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (FindNode(e.Source as Visual) is not { } node) return;
        var root = this.FindControl<Panel>("DiagramRoot");
        if (root is null) return;
        var p = e.GetPosition(root);
        _drag = node;
        _dragOffset = new Point(p.X - node.X, p.Y - node.Y);
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is null) return;
        var root = this.FindControl<Panel>("DiagramRoot");
        if (root is null) return;
        var p = e.GetPosition(root);
        _drag.X = Math.Max(0, p.X - _dragOffset.X);
        _drag.Y = Math.Max(0, p.Y - _dragOffset.Y);
        InvalidateEdges();
    }

    private void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is null) return;
        _drag = null;
        e.Pointer.Capture(null);
        InvalidateEdges();
    }

    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FindNode(e.Source as Visual) is { } node)
            _vm?.NavigateNodeCommand.Execute(node);
    }

    // Walk up the visual tree to the item whose DataContext is a RelationalNode.
    private static RelationalNode? FindNode(Visual? source)
    {
        var v = source;
        while (v is not null)
        {
            if (v is Control { DataContext: RelationalNode n }) return n;
            v = v.GetVisualParent();
        }
        return null;
    }
}
