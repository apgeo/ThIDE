// Leads enrichment tests: exact / separator-insensitive / unique-last-segment matching,
// ambiguous-segment and absent-station drops, local coordinates, determinism.

using Therion.Blender;
using Therion.Blender.Geometry;
using Therion.Blender.Sources;

namespace Therion.Blender.Tests;

public class LeadsEnricherTests
{
    private static SceneMeta MetaWithStations(char separator, params (string Name, CaveVector3 Pos)[] stations)
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            SeparatorChar = separator,
            Stations = stations.Select((s, i) =>
                new CaveStation { Id = (uint)i, Name = s.Name, Position = s.Pos }).ToArray(),
        };
        // Recenter off so the meta station positions equal the inputs (easier to assert).
        return SceneMeta.Build(GeometryStage.Build(model, new GeometryOptions { Recenter = false, WallSource = WallSource.Tubes }));
    }

    [Fact]
    public void Enrich_ExactMatch_PlacesLeadAtStation()
    {
        var meta = MetaWithStations('.', ("cave.entrance", new CaveVector3(1, 2, 3)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("cave.entrance", "continuation flag", "goes big")]);

        var lead = Assert.Single(enriched.Leads);
        Assert.Equal("cave.entrance", lead.Station);
        Assert.Equal(new SceneMetaVec(1, 2, 3), lead.Position);
        Assert.Equal("goes big", lead.Note);
    }

    [Fact]
    public void Enrich_CaseInsensitiveMatch()
    {
        var meta = MetaWithStations('.', ("Cave.A", new CaveVector3(4, 5, 6)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("cave.a", "dig")]);
        Assert.Single(enriched.Leads);
        Assert.Equal(new SceneMetaVec(4, 5, 6), enriched.Leads[0].Position);
    }

    [Fact]
    public void Enrich_SeparatorInsensitiveMatch()
    {
        // Station names use ':' separator; lead register uses '.'.
        var meta = MetaWithStations(':', ("cave:north:3", new CaveVector3(7, 8, 9)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("cave.north.3", "lead")]);
        Assert.Single(enriched.Leads);
        Assert.Equal(new SceneMetaVec(7, 8, 9), enriched.Leads[0].Position);
    }

    [Fact]
    public void Enrich_UniqueLastSegment_BridgesLocalVsQualifiedNames()
    {
        // .lox exposes survey-local station names ("7"); the lead is fully qualified.
        var meta = MetaWithStations('.', ("7", new CaveVector3(10, 0, 0)), ("8", new CaveVector3(11, 0, 0)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("cave.branch.7", "continuation flag")]);
        Assert.Single(enriched.Leads);
        Assert.Equal(new SceneMetaVec(10, 0, 0), enriched.Leads[0].Position);
    }

    [Fact]
    public void Enrich_AmbiguousLastSegment_IsNotMatched()
    {
        // Two stations share the local name "1" → ambiguous, so a qualified lead that
        // only matches by last segment must be dropped rather than placed arbitrarily.
        var meta = MetaWithStations('.', ("a.1", new CaveVector3(1, 0, 0)), ("b.1", new CaveVector3(2, 0, 0)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("c.1", "dig")]);
        Assert.Empty(enriched.Leads);
    }

    [Fact]
    public void Enrich_UnmatchedStation_IsDropped()
    {
        var meta = MetaWithStations('.', ("cave.a", new CaveVector3(0, 0, 0)));
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("nowhere.z", "lead")]);
        Assert.Empty(enriched.Leads);
    }

    [Fact]
    public void Enrich_UsesLocalCoordinates()
    {
        var model = new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Stations =
            [
                new CaveStation { Id = 0, Name = "a", Position = new CaveVector3(1000, 2000, 100) },
                new CaveStation { Id = 1, Name = "b", Position = new CaveVector3(1010, 2010, 110) },
            ],
        };
        var meta = SceneMeta.Build(GeometryStage.Build(model)); // recenter ON
        var enriched = LeadsEnricher.Enrich(meta, [new SourceLead("a", "dig")]);

        // Position is recentered (local), not the UTM-scale world value.
        Assert.Single(enriched.Leads);
        Assert.Equal(new SceneMetaVec(-5, -5, -5), enriched.Leads[0].Position);
    }

    [Fact]
    public void Enrich_IsDeterministic_AndOrderedByStation()
    {
        var meta = MetaWithStations('.',
            ("z", new CaveVector3(3, 0, 0)), ("a", new CaveVector3(1, 0, 0)), ("m", new CaveVector3(2, 0, 0)));
        var leads = new[] { new SourceLead("z", "lead"), new SourceLead("a", "dig"), new SourceLead("m", "qm") };

        var first = LeadsEnricher.Enrich(meta, leads);
        var second = LeadsEnricher.Enrich(meta, leads);

        Assert.Equal(new[] { "a", "m", "z" }, first.Leads.Select(l => l.Station));
        Assert.Equal(first.Leads, second.Leads);
    }

    [Fact]
    public void Enrich_EmptyLeads_ClearsList()
    {
        var meta = MetaWithStations('.', ("a", new CaveVector3(0, 0, 0)));
        Assert.Empty(LeadsEnricher.Enrich(meta, []).Leads);
    }
}
