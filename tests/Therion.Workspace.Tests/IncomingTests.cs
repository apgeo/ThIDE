using Therion.Workspace.Incoming;

namespace Therion.Workspace.Tests;

public class IncomingTests
{
    [Theory]
    [InlineData("trip.th", IncomingRoute.LinkSurvey)]
    [InlineData("plan.TH2", IncomingRoute.SketchOnly)]
    [InlineData("cave.svx", IncomingRoute.ImportSurvey)]
    [InlineData("cave.dat", IncomingRoute.ImportSurvey)]
    [InlineData("waypoints.gpx", IncomingRoute.ImportGpx)]
    [InlineData("photo.jpg", IncomingRoute.Ignore)]
    public void Routes_by_extension(string file, IncomingRoute route) => Assert.Equal(route, IncomingFiles.Route(file));

    [Fact]
    public void Folder_only_when_turned_on()
    {
        Assert.Null(IncomingFiles.FolderFor("/c", null));
        Assert.Null(IncomingFiles.FolderFor("/c", new ProjectSettingsFile()));
        Assert.Equal(Path.GetFullPath("/c/drop"), IncomingFiles.FolderFor("/c", new ProjectSettingsFile { Incoming = new() { Enabled = true, Folder = "drop" } }));
    }

    [Fact]
    public void A_file_is_stable_once_it_stops_changing_for_the_interval()
    {
        var t = new DateTime(2026, 10, 28, 12, 0, 0);
        (long, DateTime)? state = (100, t);
        var tracker = new StableFileTracker(_ => state, () => t);

        Assert.False(tracker.IsStable("f"));          // first look
        t = t.AddSeconds(1.5);
        state = (200, t);
        Assert.False(tracker.IsStable("f"));          // still growing
        t = t.AddSeconds(0.5);
        Assert.False(tracker.IsStable("f"));          // unchanged, but not long enough
        t = t.AddSeconds(0.6);
        Assert.True(tracker.IsStable("f"));
        state = null;
        Assert.False(tracker.IsStable("f"));          // gone
    }

    [Fact]
    public void Watcher_filter_and_unwatch()
    {
        var dir = Directory.CreateTempSubdirectory("thide-watch-").FullName;
        try
        {
            using var w = new DebouncedFileWatcher(TimeSpan.FromMilliseconds(50)) { Filter = p => p.EndsWith(".th") };
            var seen = new List<string>();
            w.FileChanged += p => { lock (seen) seen.Add(Path.GetFileName(p)); };
            w.Watch(dir);
            File.WriteAllText(Path.Combine(dir, "a.th"), "x");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "x");
            for (int i = 0; i < 40 && seen.Count == 0; i++) Thread.Sleep(50);
            Thread.Sleep(200);
            lock (seen) Assert.Equal(["a.th"], seen.Distinct());

            w.Unwatch(dir);
            Assert.Empty(w.Watched);
        }
        finally { Directory.Delete(dir, true); }
    }
}
