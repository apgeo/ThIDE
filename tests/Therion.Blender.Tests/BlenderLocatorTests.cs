// Locator tests (BA-B10 batch 1): version parsing, and the discovery logic (override first,
// newest ≥4.2 wins, too-old vs not-found) exercised with a fake probe + injected candidates
// so it never touches a real Blender or the filesystem.

using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class BlenderLocatorTests
{
    private sealed class FakeProbe(Dictionary<string, BlenderVersion> versions) : IBlenderProbe
    {
        public List<string> Probed { get; } = [];
        public BlenderVersion? Probe(string path)
        {
            Probed.Add(path);
            return versions.TryGetValue(path, out var v) ? v : null;
        }
    }

    private static BlenderLocator Locator(Dictionary<string, BlenderVersion> installs, params string[] candidates)
        => new(new FakeProbe(installs), () => candidates);

    // ---- version parsing ----

    [Theory]
    [InlineData("Blender 4.5.1", 4, 5, 1)]
    [InlineData("Blender 4.2.0 LTS\n\tbuild date: ...", 4, 2, 0)]
    [InlineData("Blender 5.0", 5, 0, 0)]     // missing patch → 0
    [InlineData("4.3.2", 4, 3, 2)]           // bare
    public void Version_Parses(string text, int major, int minor, int patch)
    {
        Assert.True(BlenderVersion.TryParse(text, out var v));
        Assert.Equal(new BlenderVersion(major, minor, patch), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no version here")]
    public void Version_RejectsGarbage(string? text)
    {
        Assert.False(BlenderVersion.TryParse(text, out _));
    }

    [Fact]
    public void Version_ComparesByComponent()
    {
        Assert.True(new BlenderVersion(4, 5, 0) > new BlenderVersion(4, 2, 9));
        Assert.True(new BlenderVersion(4, 2, 0) >= BlenderLocator.MinimumVersion);
        Assert.True(new BlenderVersion(4, 1, 9) < BlenderLocator.MinimumVersion);
    }

    // ---- discovery ----

    [Fact]
    public void Found_NewestUsableWins()
    {
        var locator = Locator(
            new Dictionary<string, BlenderVersion>
            {
                ["/opt/blender42"] = new(4, 2, 0),
                ["/opt/blender50"] = new(5, 0, 1),
                ["/opt/blender45"] = new(4, 5, 3),
            },
            "/opt/blender42", "/opt/blender45", "/opt/blender50");

        var result = locator.Locate();
        Assert.Equal(BlenderLocateStatus.Found, result.Status);
        Assert.Equal("/opt/blender50", result.Installation!.Path);
        Assert.Equal(new BlenderVersion(5, 0, 1), result.Installation.Version);
    }

    [Fact]
    public void Override_IsTriedFirst_AndUsedWhenUsable()
    {
        var locator = Locator(
            new Dictionary<string, BlenderVersion> { ["/custom/blender"] = new(4, 4, 0), ["/opt/blender"] = new(4, 3, 0) },
            "/opt/blender");
        var result = locator.Locate(overridePath: "/custom/blender");
        Assert.Equal("/custom/blender", result.Installation!.Path);
    }

    [Fact]
    public void OnlyOldInstalls_ReportTooOld_WithDetail()
    {
        var locator = Locator(
            new Dictionary<string, BlenderVersion> { ["/opt/b36"] = new(3, 6, 0), ["/opt/b40"] = new(4, 0, 2) },
            "/opt/b36", "/opt/b40");
        var result = locator.Locate();
        Assert.Equal(BlenderLocateStatus.TooOld, result.Status);
        Assert.Equal("/opt/b40", result.Installation!.Path); // newest of the too-old ones
        Assert.Contains("4.2", result.Detail);
        Assert.False(result.IsUsable);
    }

    [Fact]
    public void NothingProbable_IsNotFound()
    {
        var locator = Locator(new Dictionary<string, BlenderVersion>(), "/nope/blender", "/also/missing");
        var result = locator.Locate();
        Assert.Equal(BlenderLocateStatus.NotFound, result.Status);
        Assert.Null(result.Installation);
    }

    [Fact]
    public void Probe_IsCachedPerPath()
    {
        var probe = new FakeProbe(new Dictionary<string, BlenderVersion> { ["/opt/b"] = new(4, 5, 0) });
        var locator = new BlenderLocator(probe, () => new[] { "/opt/b" });
        locator.Locate();
        locator.Locate();
        Assert.Single(probe.Probed); // second Locate hits the cache
    }
}
