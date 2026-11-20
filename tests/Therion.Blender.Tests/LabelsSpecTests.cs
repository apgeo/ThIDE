// Labels spec tests (BA-B8 batch 1): the validation matrix + JSON round-trip of the new
// Labels sub-object (station filters, cap, component/lead knobs, overlays, events). The
// planner/emitter are covered elsewhere; this guards the spec surface.

using Therion.Blender;

namespace Therion.Blender.Tests;

public class LabelsSpecTests
{
    private static SceneSpec Base() => SceneSpecTests.ValidSpec();

    [Fact]
    public void Defaults_AreValid_AndAllGroupsOff()
    {
        var spec = Base();
        Assert.Empty(SceneSpecValidator.Validate(spec));
        Assert.False(spec.Labels.Stations.Show);
        Assert.False(spec.Labels.Components.Show);
        Assert.False(spec.Labels.Leads.Show);
    }

    public static TheoryData<string, SceneSpec> InvalidSpecs()
    {
        var b = Base();
        SceneSpec Labels(LabelsSpec l) => b with { Labels = l };
        return new TheoryData<string, SceneSpec>
        {
            { "labels.stations.maxCount", Labels(new LabelsSpec { Stations = new StationLabelSpec { MaxCount = 0 } }) },
            { "labels.stations.textScale", Labels(new LabelsSpec { Stations = new StationLabelSpec { TextScale = 0 } }) },
            { "labels.stations.pattern", Labels(new LabelsSpec { Stations = new StationLabelSpec { Filter = StationFilter.Regex } }) },
            { "labels.stations.pattern", Labels(new LabelsSpec { Stations = new StationLabelSpec { Filter = StationFilter.Regex, Pattern = "(" } }) },
            { "labels.stations.minDepth", Labels(new LabelsSpec { Stations = new StationLabelSpec { Filter = StationFilter.DepthRange, MinDepth = 5, MaxDepth = -5 } }) },
            { "labels.components.minStationCount", Labels(new LabelsSpec { Components = new ComponentLabelSpec { MinStationCount = 0 } }) },
            { "labels.leads.markerScale", Labels(new LabelsSpec { Leads = new LeadMarkerSpec { MarkerScale = -1 } }) },
            { "labels.color", Labels(new LabelsSpec { Color = new ColorRgb(2, 0, 0) }) },
            { "labels.events[0].hideFrame", Labels(new LabelsSpec { Events = [new VisibilityEvent { Target = VisibilityTarget.Overlays, ShowFrame = 50, HideFrame = 10 }] }) },
            { "labels.events[0].fadeSeconds", Labels(new LabelsSpec { Events = [new VisibilityEvent { Target = VisibilityTarget.StationLabels, FadeSeconds = -1 }] }) },
            { "labels.overlays.endCard.text", Labels(new LabelsSpec { Overlays = new OverlaySpec { EndCard = new EndCardSpec { Text = " " } } }) },
            { "labels.overlays.endCard.seconds", Labels(new LabelsSpec { Overlays = new OverlaySpec { EndCard = new EndCardSpec { Text = "End", Seconds = 0 } } }) },
            { "labels.overlays.titleSeconds", Labels(new LabelsSpec { Overlays = new OverlaySpec { Title = "T", TitleSeconds = -1 } }) },
            { "labels.overlays.fadeFromBlack", Labels(new LabelsSpec { Overlays = new OverlaySpec { FadeFromBlack = -1 } }) },
            { "labels.overlays.fadeToBlack", Labels(new LabelsSpec { Overlays = new OverlaySpec { FadeToBlack = double.NaN } }) },
            { "labels.overlays.captions[1].text", Labels(new LabelsSpec { Overlays = new OverlaySpec { Captions = [new CaptionSpec { Text = "a", End = 1 }, new CaptionSpec { End = 1 }] } }) },
            { "labels.overlays.captions[0].start", Labels(new LabelsSpec { Overlays = new OverlaySpec { Captions = [new CaptionSpec { Text = "a", Start = -1, End = 1 }] } }) },
            { "labels.overlays.captions[0].end", Labels(new LabelsSpec { Overlays = new OverlaySpec { Captions = [new CaptionSpec { Text = "a", Start = 2, End = 2 }] } }) },
            { "labels.overlays.cornerCaptions[0].end", Labels(new LabelsSpec { Overlays = new OverlaySpec { CornerCaptions = [new CaptionSpec { Text = "a", Start = 2, End = 1 }] } }) },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidSpecs))]
    public void EachRule_HasAFailingCase(string expectedPath, SceneSpec spec)
    {
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == expectedPath);
    }

    [Fact]
    public void ValidRegexAndDepthRange_AreAccepted()
    {
        var regex = Base() with { Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Regex, Pattern = "^entrance" } } };
        Assert.Empty(SceneSpecValidator.Validate(regex));

        var depth = Base() with { Labels = new LabelsSpec { Stations = new StationLabelSpec { Show = true, Filter = StationFilter.DepthRange, MinDepth = -50, MaxDepth = 0 } } };
        Assert.Empty(SceneSpecValidator.Validate(depth));
    }

    [Fact]
    public void Json_RoundTripsLabels()
    {
        var spec = Base() with
        {
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true, Filter = StationFilter.Regex, Pattern = "abc", MaxCount = 50, TextScale = 1.5 },
                Components = new ComponentLabelSpec { Show = true, MinStationCount = 10 },
                Leads = new LeadMarkerSpec { Show = true, Pulse = false, ShowText = true },
                Overlays = new OverlaySpec { Title = "Peștera Test", ScaleBar = true, NorthArrow = true },
                Events = [new VisibilityEvent { Target = VisibilityTarget.StationLabels, ShowFrame = 12, FadeSeconds = 0.5 }],
                Color = new ColorRgb(0.8, 0.7, 0.6),
            },
        };

        var json = SceneSpecSerializer.Write(spec);
        var back = SceneSpecSerializer.Read(json);
        Assert.Equal(json, SceneSpecSerializer.Write(back));
        Assert.Equal(StationFilter.Regex, back.Labels.Stations.Filter);
        Assert.Equal("Peștera Test", back.Labels.Overlays.Title);
        Assert.Single(back.Labels.Events);
        Assert.Equal(VisibilityTarget.StationLabels, back.Labels.Events[0].Target);
    }

    [Fact]
    public void Json_RoundTripsEveryTextOverThePicture()
    {
        var spec = Base() with
        {
            Labels = new LabelsSpec
            {
                Overlays = new OverlaySpec
                {
                    Title = "Peștera Test", Subtitle = "ridicare 2026", TitleSeconds = 3, Credits = "echipa", Corner = "Bihor",
                    EndCard = new EndCardSpec { Text = "Sfârșit", Subtitle = "mulțumim", Seconds = 5 },
                    Captions = [new CaptionSpec { Text = "Sala Mare", Start = 1.5, End = 4 }],
                    CornerCaptions = [new CaptionSpec { Text = "-40 m", Start = 2, End = 3 }],
                    FadeFromBlack = 1, FadeToBlack = 2,
                },
            },
        };
        Assert.Empty(SceneSpecValidator.Validate(spec));

        var json = SceneSpecSerializer.Write(spec);
        var overlays = SceneSpecSerializer.Read(json).Labels.Overlays;

        Assert.Equal(json, SceneSpecSerializer.Write(SceneSpecSerializer.Read(json)));
        Assert.Equal(("ridicare 2026", 3.0, "echipa", "Bihor", 1.0, 2.0),
            (overlays.Subtitle, overlays.TitleSeconds, overlays.Credits, overlays.Corner, overlays.FadeFromBlack, overlays.FadeToBlack));
        Assert.Equal(new EndCardSpec { Text = "Sfârșit", Subtitle = "mulțumim", Seconds = 5 }, overlays.EndCard);
        Assert.Equal([new CaptionSpec { Text = "Sala Mare", Start = 1.5, End = 4 }], overlays.Captions);
        Assert.Equal([new CaptionSpec { Text = "-40 m", Start = 2, End = 3 }], overlays.CornerCaptions);
        Assert.True(overlays.HasFlatOnlyTexts);
        Assert.True(overlays.Any);
    }

    [Fact]
    public void ASpecWithoutTheLaterTexts_IsTheJsonItWasBeforeTheyExisted_SoItsHashIsToo()
    {
        var spec = Base() with { Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "T", ScaleBar = true } } };

        string json = SceneSpecSerializer.Write(spec);

        Assert.Contains("\"overlays\": {\n      \"title\": \"T\",\n      \"scaleBar\": true,\n      \"northArrow\": false,\n      \"depthLegend\": false\n    }", json.ReplaceLineEndings("\n"));
        foreach (string later in new[] { "credits", "endCard", "captions", "subtitle", "titleSeconds", "corner", "cornerCaptions", "fadeFromBlack", "fadeToBlack", "hasFlatOnlyTexts", "any" })
            Assert.DoesNotContain("\"" + later + "\"", json);
        Assert.False(spec.Labels.Overlays.HasFlatOnlyTexts);
        // An end card's subtitle is written only when it has one.
        string card = SceneSpecSerializer.Write(Base() with { Labels = new LabelsSpec { Overlays = new OverlaySpec { EndCard = new EndCardSpec { Text = "End" } } } });
        Assert.Contains("\"endCard\"", card);
        Assert.DoesNotContain("\"subtitle\"", card);
        Assert.NotEqual(SceneSpecSerializer.ComputeHash(spec), SceneSpecSerializer.ComputeHash(spec with
        {
            Labels = new LabelsSpec { Overlays = spec.Labels.Overlays with { Credits = "someone" } },
        }));
    }
}
