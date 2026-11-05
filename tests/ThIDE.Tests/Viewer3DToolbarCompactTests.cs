// The docked 3D viewer (~250-300 px wide at 1366x768) moves the bulk of its toolbar into the "⋯" flyout, so
// the bar keeps to two rows and the view keeps its height. A wide panel (floated, full screen) keeps them all.

using ThIDE.Views.Docking;

namespace ThIDE.Tests;

public sealed class Viewer3DToolbarCompactTests
{
    [Theory]
    [InlineData(250, true)]
    [InlineData(297, true)]
    [InlineData(719, true)]
    [InlineData(720, false)]
    [InlineData(1366, false)]
    [InlineData(0, false)]   // not laid out yet
    public void Narrow_panels_use_the_compact_toolbar(double width, bool compact) =>
        Assert.Equal(compact, Model3DViewerToolView.IsCompactToolbar(width));
}
