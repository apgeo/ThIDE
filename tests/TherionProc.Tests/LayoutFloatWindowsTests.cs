using System.IO;
using System.Linq;
using TherionProc.Services;

namespace TherionProc.Tests;

// UX-05: the floated-window persistence contract — float windows (bounds + dockable ids) must
// round-trip through layout.json so they can be re-created at next launch.
public class LayoutFloatWindowsTests
{
    [Fact]
    public void Float_windows_round_trip_through_layout_json()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "layout.json");

        var state = LayoutState.Default with
        {
            FloatWindows = new[]
            {
                new FloatWindowState { X = 100, Y = 120, Width = 640, Height = 480,
                    DockableIds = new[] { "Diagnostics" } },
                new FloatWindowState { X = 50, Y = 60, Width = 800, Height = 600,
                    DockableIds = new[] { "C:/proj/a.th", "C:/proj/b.th" } },
            },
        };

        new JsonLayoutService(path).Save(state);
        var reloaded = new JsonLayoutService(path).Current;

        Assert.Equal(2, reloaded.FloatWindows.Count);

        var first = reloaded.FloatWindows[0];
        Assert.Equal(100, first.X);
        Assert.Equal(120, first.Y);
        Assert.Equal(640, first.Width);
        Assert.Equal(480, first.Height);
        Assert.Equal(new[] { "Diagnostics" }, first.DockableIds);

        var second = reloaded.FloatWindows[1];
        Assert.Equal(new[] { "C:/proj/a.th", "C:/proj/b.th" }, second.DockableIds.ToArray());
    }

    [Fact]
    public void Missing_float_windows_defaults_to_empty()
    {
        // A legacy layout.json with no FloatWindows key must deserialize to an empty list.
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "layout.json");
        File.WriteAllText(path, "{ \"WindowWidth\": 1200 }");

        var state = new JsonLayoutService(path).Current;
        Assert.NotNull(state.FloatWindows);
        Assert.Empty(state.FloatWindows);
        Assert.Equal(1200, state.WindowWidth);
    }
}
