// code-behind for the popped-out Stereonet panel. Wraps the same StructuralGeologyViewModel as the
// wizard tab, so the net data / options / selection are already live-synced via bindings; the only
// glue is the click-to-select bridge and the PNG export (both handled by StereonetControl itself).

using System;
using Avalonia.Controls;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Views.Docking;

public partial class StructuralStereonetToolView : UserControl
{
    private bool _wired;

    public StructuralStereonetToolView() => InitializeComponent();

    private StructuralGeologyViewModel? Vm => (DataContext as StructuralStereonetToolViewModel)?.Structural;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_wired || Vm is null || NetControl is null) return;
        _wired = true;
        NetControl.PlaneActivated += (_, name) => Vm?.SelectPlaneByName(name);
    }

    private async void OnExportStereonet(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NetControl is not null) await NetControl.ExportPngAsync();
    }
}
