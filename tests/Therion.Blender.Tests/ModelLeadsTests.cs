// Leads without a project: the stations a model flags as continuations, and a leads file.

using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class ModelLeadsTests
{
    [Fact]
    public void FromModel_GivesTheStationsFlaggedAsContinuations_NamedWithTheirSurveys()
    {
        var model = InventedCave.Build(InventedCaveSize.Medium);
        var leads = ModelLeads.FromModel(model);

        Assert.Equal(InventedCave.Leads(model).Count, leads.Count);
        Assert.NotEmpty(leads);
        Assert.All(leads, lead => Assert.Equal(ModelLeads.ContinuationKind, lead.Kind));
        Assert.All(leads, lead => Assert.Contains('.', lead.Station));

        // Every one of them is found again in the model's metadata, at its own station.
        var meta = LeadsEnricher.Enrich(SceneMeta.Build(GeometryStage.Build(model)), leads);
        Assert.Equal(leads.Count, meta.Leads.Count);
    }

    [Fact]
    public void Read_TakesNames_AndObjectsWithAKindAndANote()
    {
        var leads = ModelLeads.Read("""
            // what is still to be pushed
            { "leads": [ "main.7", { "station": " side.3 ", "kind": "dig", "note": "draughts" }, { "Station": "main.9" }, ] }
            """);

        Assert.Equal(
            [new SourceLead("main.7", "continuation"), new SourceLead("side.3", "dig", "draughts"), new SourceLead("main.9", "continuation")],
            leads);
        Assert.Equal([new SourceLead("a", "continuation")], ModelLeads.Read("[\"a\"]"));
        Assert.Empty(ModelLeads.Read("[]"));
    }

    [Theory]
    [InlineData("{", "not valid JSON")]
    [InlineData("\"main.7\"", "JSON array")]
    [InlineData("[7]", "leads[0]")]
    [InlineData("[\"a\", {\"note\": \"no station\"}]", "leads[1].station")]
    [InlineData("[{\"station\": 7}]", "leads[0]")]
    public void Read_RefusesWhatIsNoLeadsFile_AndSaysWhere(string json, string expected)
    {
        var error = Assert.Throws<FormatException>(() => ModelLeads.Read(json));
        Assert.Contains(expected, error.Message);
    }
}
