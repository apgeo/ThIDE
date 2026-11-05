// The status-bar path crumb gives way from the root side: when the row is too narrow the leading folders
// are dropped behind a "…" and the file name stays whole. The old StackPanel clipped on the right and cut
// "demo.th" to "de", right up against "Length:".

using Avalonia;
using Avalonia.Controls;
using ThIDE.Views;

namespace ThIDE.Tests;

public sealed class StatusPathCrumbTrimTests
{
    [Fact]
    public void Everything_is_shown_when_it_fits() =>
        Assert.Equal(0, TailFirstPanel.FirstShown(new double[] { 30, 40, 50 }, 120, 10));

    [Fact]
    public void Leading_crumbs_give_way_and_leave_room_for_the_ellipsis()
    {
        // 30+40+50 = 120 > 119: drop the first; 10 (…) + 40 + 50 = 100 fits.
        Assert.Equal(1, TailFirstPanel.FirstShown(new double[] { 30, 40, 50 }, 119, 10));
        // 10 + 40 + 50 = 100 > 99: only the last fits beside the ellipsis.
        Assert.Equal(2, TailFirstPanel.FirstShown(new double[] { 30, 40, 50 }, 99, 10));
    }

    [Fact]
    public void The_last_crumb_is_kept_even_when_it_alone_is_too_wide()
    {
        Assert.Equal(2, TailFirstPanel.FirstShown(new double[] { 30, 40, 50 }, 20, 10));
        Assert.Equal(0, TailFirstPanel.FirstShown(new double[] { 80 }, 20, 10));
        Assert.Equal(0, TailFirstPanel.FirstShown(Array.Empty<double>(), 20, 10));
    }

    [Fact]
    public void A_narrow_panel_keeps_the_file_name_whole_at_its_right_edge() => UiThread.Run(() =>
    {
        var crumbs = new[] { 60.0, 60, 60, 45 }.Select(w => new Border { Width = w, Height = 14 }).ToArray();
        var ellipsis = new Border { Width = 10, Height = 14 };
        var panel = new TailFirstPanel(ellipsis);
        foreach (var c in crumbs) panel.Children.Add(c);

        panel.Measure(new Size(150, 20));
        panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, 20));

        Assert.True(panel.DesiredSize.Width <= 150);
        var file = crumbs[^1].Bounds;
        Assert.Equal(45, file.Width);                                  // the file name is whole
        Assert.Equal(panel.DesiredSize.Width, file.Right, 3);          // ... and ends the row
        Assert.True(crumbs[0].Bounds.Right <= 0);                      // the root-side folder is out of view
        Assert.Equal((0.0, 10.0), (ellipsis.Bounds.X, ellipsis.Bounds.Width));   // the "…" leads the row
        Assert.Equal(10, crumbs[2].Bounds.X);                          // the nearest folder follows it
    });

    [Fact]
    public void A_wide_panel_lays_the_crumbs_out_from_the_left_as_before() => UiThread.Run(() =>
    {
        var crumbs = new[] { 60.0, 45 }.Select(w => new Border { Width = w, Height = 14 }).ToArray();
        var ellipsis = new Border { Width = 10, Height = 14 };
        var panel = new TailFirstPanel(ellipsis);
        foreach (var c in crumbs) panel.Children.Add(c);

        panel.Measure(new Size(500, 20));
        panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, 20));

        Assert.Equal(105, panel.DesiredSize.Width);
        Assert.Equal(0, crumbs[0].Bounds.X);
        Assert.Equal(60, crumbs[1].Bounds.X);
        Assert.True(ellipsis.Bounds.Right <= 0);                       // no "…" when nothing is dropped
    });
}
