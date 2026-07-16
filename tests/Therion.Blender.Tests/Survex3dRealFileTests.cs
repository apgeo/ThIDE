// .3d parser tests against the real Therion-exported file committed in tests/Corpus
// (av_cerbul_de_aur — the same cave/export also exists as .lox, which batch-4's
// cross-format suite compares against station by station).

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class Survex3dRealFileTests
{
    [CorpusFact]
    public void AvCerbul_ParsesHeaderAndContent()
    {
        var model = Survex3dReader.ReadFile(TestCorpus.AvCerbul3d());

        Assert.Equal(CaveSourceFormat.Survex3d, model.SourceFormat);
        Assert.Equal(8, model.FormatVersion);
        Assert.False(string.IsNullOrEmpty(model.Title));
        Assert.NotNull(model.Datestamp);

        Assert.True(model.Stations.Count > 10, $"expected a real station list, found {model.Stations.Count}");
        Assert.True(model.Shots.Count > 10, $"expected a real shot list, found {model.Shots.Count}");

        // Station ids are the sequential file order the parser assigns.
        Assert.Equal((uint)(model.Stations.Count - 1), model.Stations[^1].Id);

        // This export is splay-heavy (TopoDroid survey): real centerline legs are the
        // minority, but every one of them must anchor at stations on both ends —
        // exact double equality, since both come through the same cm→m conversion.
        var positions = model.Stations.Select(s => s.Position).ToHashSet();
        var legs = model.Shots.Where(s => (s.Flags & CaveShotFlags.Splay) == 0).ToList();
        Assert.True(legs.Count > 10, $"expected real centerline legs, found {legs.Count}");
        Assert.All(legs, leg =>
        {
            Assert.Contains(leg.FromPosition, positions);
            Assert.Contains(leg.ToPosition, positions);
        });

        // The v8 header metadata made it through.
        Assert.Equal("epsg:32635", model.CoordinateSystem);
        Assert.Equal('.', model.SeparatorChar);

        // The cave spans a plausible extent; named stations have real labels while
        // the anonymous splay endpoints legitimately have empty ones.
        var xs = model.Stations.Select(s => s.Position.X).ToList();
        Assert.True(xs.Max() - xs.Min() > 1.0, "cave should span more than a metre");
        var named = model.Stations.Where(s => (s.Flags & CaveStationFlags.Anonymous) == 0).ToList();
        Assert.True(named.Count > 10, $"expected named stations, found {named.Count}");
        Assert.All(named, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        Assert.Contains(model.Stations, s => (s.Flags & CaveStationFlags.Anonymous) != 0);
    }
}
