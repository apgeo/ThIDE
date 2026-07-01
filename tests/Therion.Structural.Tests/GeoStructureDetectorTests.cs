// Phase 2 — detection: each signal, grouping modes, splay policy, synthetic origin row.

using System.Linq;
using Therion.Semantics;
using Therion.Structural;
using static Therion.Structural.Tests.TestModel;

namespace Therion.Structural.Tests;

public class GeoStructureDetectorTests
{
    private static readonly Vec3 P1 = new(1, 0, 0);
    private static readonly Vec3 P2 = new(0, 1, 0.2);
    private static readonly Vec3 P3 = new(1, 1, 0.1);

    [Fact]
    public void NameKeyword_GroupsConsecutiveSameFrom_AddsOriginRow()
    {
        var model = Model(new[]
        {
            Shot("geo1", "p1", P1), Shot("geo1", "p2", P2), Shot("geo1", "p3", P3),
            Shot("a", "b", new Vec3(0, 5, 0)),   // ordinary leg — not detected
        });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions());

        var batch = Assert.Single(batches);
        Assert.Equal("geo1", batch.Name);
        Assert.Equal(3, batch.Measurements.Count(m => !m.IsOrigin));
        Assert.Single(batch.Measurements, m => m.IsOrigin);            // origin row present
        Assert.False(batch.Measurements.Single(m => m.IsOrigin).IncludedByDefault); // off by default
        Assert.Equal(3, batch.DefaultIncluded().Length);              // origin excluded by default
        Assert.All(batch.Measurements.Where(m => !m.IsOrigin),
            m => Assert.True(m.MatchedBy.HasFlag(DetectionSignal.NameKeyword)));
    }

    [Fact]
    public void TwoFromStations_ProduceTwoBatches()
    {
        var model = Model(new[]
        {
            Shot("geo1", "p1", P1), Shot("geo1", "p2", P2), Shot("geo1", "p3", P3),
            Shot("geo2", "q1", P1), Shot("geo2", "q2", P2), Shot("geo2", "q3", P3),
        });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions());

        Assert.Equal(2, batches.Length);
        Assert.Equal("geo1", batches[0].Name);
        Assert.Equal("geo2", batches[1].Name);
    }

    [Fact]
    public void CommentMarker_GroupsByParameter_AcrossStations()
    {
        var opts = new DetectionOptions
        {
            NameKeywords = System.Collections.Immutable.ImmutableArray<string>.Empty, // name signal off
            MatchComment = true,
            Grouping = GroupingMode.ByCommentParameter,
        };
        var model = Model(new[]
        {
            Shot("s1", "p1", P1, comment: "plane fault-A"),
            Shot("s2", "p2", P2, comment: "plane fault-A"),
            Shot("s3", "p3", P3, comment: "plane fault-B"),
        });

        var batches = GeoStructureDetector.Detect(model, opts);

        Assert.Equal(2, batches.Length);
        Assert.Equal("fault-A", batches[0].Key);
        Assert.Equal(2, batches[0].Measurements.Count(m => !m.IsOrigin));
        Assert.Equal("fault-B", batches[1].Key);
        // Multi-station batch ⇒ no synthetic origin row.
        Assert.DoesNotContain(batches[0].Measurements, m => m.IsOrigin);
    }

    [Fact]
    public void StationFlag_Detects()
    {
        var opts = new DetectionOptions
        {
            NameKeywords = System.Collections.Immutable.ImmutableArray<string>.Empty,
            MatchStationFlag = true,
            StationFlags = System.Collections.Immutable.ImmutableArray.Create("geo"),
        };
        var model = Model(
            new[] { Shot("s1", "p1", P1), Shot("s1", "p2", P2), Shot("s1", "p3", P3) },
            new[] { Station("s1", "geo") });

        var batches = GeoStructureDetector.Detect(model, opts);

        var batch = Assert.Single(batches);
        Assert.Equal(3, batch.Measurements.Count(m => !m.IsOrigin));
        Assert.All(batch.Measurements.Where(m => !m.IsOrigin),
            m => Assert.True(m.MatchedBy.HasFlag(DetectionSignal.StationFlag)));
    }

    [Theory]
    [InlineData(SplayPolicy.Exclude, 2)]   // legs included, splays not
    [InlineData(SplayPolicy.Include, 4)]   // legs + splays
    public void SplayPolicy_ControlsDefaultInclusion(SplayPolicy policy, int expectedDefault)
    {
        var model = Model(new[]
        {
            Shot("geo1", "p1", P1),
            Shot("geo1", "p2", P2),
            Shot("geo1", "x1", new Vec3(2, 0, 1), ShotFlags.Splay),
            Shot("geo1", "x2", new Vec3(0, 2, 1), ShotFlags.Splay),
        });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions { Splays = policy });

        var batch = Assert.Single(batches);
        Assert.Equal(4, batch.Measurements.Count(m => !m.IsOrigin)); // all detected & listed
        Assert.Equal(expectedDefault, batch.DefaultIncluded().Length);
    }

    [Fact]
    public void SplayPolicy_OnlySplays_DropsLegs()
    {
        var model = Model(new[]
        {
            Shot("geo1", "p1", P1),                                   // leg — dropped
            Shot("geo1", "x1", new Vec3(2, 0, 1), ShotFlags.Splay),
            Shot("geo1", "x2", new Vec3(0, 2, 1), ShotFlags.Splay),
            Shot("geo1", "x3", new Vec3(1, 1, 1), ShotFlags.Splay),
        });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions { Splays = SplayPolicy.OnlySplays });

        var batch = Assert.Single(batches);
        Assert.Equal(3, batch.Measurements.Count(m => !m.IsOrigin));
        Assert.All(batch.Measurements.Where(m => !m.IsOrigin), m => Assert.True(m.IsSplay));
    }

    [Fact]
    public void IncludeOriginPoint_SeedsOriginIncluded()
    {
        var model = Model(new[] { Shot("geo1", "p1", P1), Shot("geo1", "p2", P2), Shot("geo1", "p3", P3) });

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions { IncludeOriginPoint = true });

        var origin = Assert.Single(batches[0].Measurements, m => m.IsOrigin);
        Assert.True(origin.IncludedByDefault);
        Assert.Equal(4, batches[0].DefaultIncluded().Length);
    }

    [Fact]
    public void World_PositionsAttached_WhenSolutionProvided()
    {
        var shots = new[] { Shot("geo1", "p1", P1), Shot("geo1", "p2", P2), Shot("geo1", "p3", P3) };
        var model = Model(shots);
        var solution = CenterlineGeometry.Solve(model.Shots, model.Equates);

        var batches = GeoStructureDetector.Detect(model, new DetectionOptions(), solution);

        Assert.All(batches[0].Measurements, m => Assert.NotNull(m.World));
    }
}
