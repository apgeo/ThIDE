using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

/// <summary>
/// Reusable panel window-control buttons (full screen / float on another monitor / move to the
/// central document well). Drop <c>&lt;docking:PanelWindowControls/&gt;</c> into any tool view whose
/// DataContext is a <see cref="ToolViewModelBase"/>; the buttons forward to the shared request methods
/// on that base, which the shell drives through the DockFactory (so they work docked or floating).
/// Add or change a control button in one place and every hosting panel gets it.
/// </summary>
public partial class PanelWindowControls : UserControl
{
    /// <summary>Stack the three buttons horizontally (default) or vertically — set to
    /// <see cref="Orientation.Vertical"/> on panels where horizontal toolbar space is tight.</summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<PanelWindowControls, Orientation>(nameof(Orientation));

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public PanelWindowControls() => InitializeComponent();

    private ToolViewModelBase? Tool => DataContext as ToolViewModelBase;

    private void OnFullScreen(object? sender, RoutedEventArgs e) => Tool?.RequestFullScreen();
    private void OnFloatOtherScreen(object? sender, RoutedEventArgs e) => Tool?.RequestFloatOtherScreen();
    private void OnMoveToCenter(object? sender, RoutedEventArgs e) => Tool?.RequestMoveToCenter();
}
