using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

/// <summary>A .lox file's station names are local to their survey ("7" in several surveys);
/// the leads register names a station top-down ("cave.branch.7"). The survey tree joins them.</summary>
public class LeadsEnricherSurveyTreeTests
{
    // root (unnamed) ─ cave ─┬─ branch : 7 at (10,0,0)
    //                        └─ upper  : 7 at (20,0,0), 8 at (21,0,0)
    private static SceneMeta Meta() => SceneMeta.Build(GeometryStage.Build(new CaveModel
    {
        SourceFormat = CaveSourceFormat.Lox,
        Surveys =
        [
            new CaveSurvey(0, 0, "", null),
            new CaveSurvey(1, 0, "cave", "The Cave"),
            new CaveSurvey(2, 1, "branch", null),
            new CaveSurvey(3, 1, "upper", null),
        ],
        Stations =
        [
            new CaveStation { Id = 0, SurveyId = 2, Name = "7", Position = new CaveVector3(10, 0, 0) },
            new CaveStation { Id = 1, SurveyId = 3, Name = "7", Position = new CaveVector3(20, 0, 0) },
            new CaveStation { Id = 2, SurveyId = 3, Name = "8", Position = new CaveVector3(21, 0, 0) },
        ],
    }, new GeometryOptions { Recenter = false }));

    [Theory]
    [InlineData("cave.branch.7", 10)]
    [InlineData("cave.upper.7", 20)]
    [InlineData("CAVE.UPPER.8", 21)]
    public void QualifiedLead_FindsTheStationOfItsSurvey(string lead, double x)
    {
        var enriched = LeadsEnricher.Enrich(Meta(), [new SourceLead(lead, "continuation")]);

        Assert.Equal(x, Assert.Single(enriched.Leads).Position.X);
    }

    [Fact]
    public void ExtraOuterSurvey_InTheRegister_StillMatchesUniquely()
        => Assert.Equal(20, Assert.Single(
            LeadsEnricher.Enrich(Meta(), [new SourceLead("system.cave.upper.7", "dig")]).Leads).Position.X);

    [Fact]
    public void ShorterPath_MatchesWhenItIsUnique()
        => Assert.Equal(10, Assert.Single(
            LeadsEnricher.Enrich(Meta(), [new SourceLead("branch.7", "dig")]).Leads).Position.X);

    [Fact]
    public void BareLocalName_SharedBySurveys_IsNotPlaced()
        => Assert.Empty(LeadsEnricher.Enrich(Meta(), [new SourceLead("7", "dig")]).Leads);

    [Fact]
    public void UnknownSurvey_IsNotPlaced()
        => Assert.Empty(LeadsEnricher.Enrich(Meta(), [new SourceLead("cave.lower.7", "dig")]).Leads);
}
