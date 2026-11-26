// Composite presets on invented caves: each built-in one is a valid storyboard, is made into a
// film of the tiny tour cave (its tour, the passage surveyed last) and lowers to segment plans; the
// files round trip; a user's presets are written into the shots without changing the film.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class CompositePresetTests
{
    private static readonly Lazy<GeometryResult> Tiny = new(() => GeometryStage.Build(TourCave.BuildTiny()));

    /// <summary>The tiny cave with its main passage and the side passage u surveyed in 1990, and
    /// the side passage v (a4 – v1 – v2) in 2001.</summary>
    private static readonly Lazy<GeometryResult> DatedTiny = new(() => GeometryStage.Build(
        RevealFixtures.Dated(TourCave.BuildTiny(), (_, i) => i >= 8 ? new DateOnly(2001, 5, 1) : new DateOnly(1990, 8, 1))));

    private static StoryboardPlan Lower(Storyboard storyboard, GeometryResult geometry)
        => StoryboardPlanner.Lower(storyboard, new SourceSpec { PlyPath = "model.ply" }, CameraFraming.FromGeometry(geometry));

    [Fact]
    public void TheBuiltInComposites_AreValidStoryboards_WithSymbolsOnly()
    {
        Assert.Equal(["Showcase", "Grand tour", "Exploration history"], BuiltInComposites.All.Select(c => c.Name));
        foreach (var composite in BuiltInComposites.All)
        {
            Assert.True(composite.BuiltIn);
            Assert.Empty(StoryboardValidator.Validate(composite.Storyboard));
            Assert.StartsWith("{cave}", composite.Storyboard.Name, StringComparison.Ordinal);
        }
        Assert.False(StoryboardExpander.IsSymbolic(BuiltInComposites.Showcase().Storyboard));
        Assert.True(StoryboardExpander.IsSymbolic(BuiltInComposites.GrandTour().Storyboard));
        Assert.True(StoryboardExpander.IsSymbolic(BuiltInComposites.ExplorationHistory().Storyboard));
    }

    [Fact]
    public void Showcase_LowersAsItIs_ThreeSegments()
    {
        var storyboard = BuiltInComposites.Showcase().Storyboard;
        var expanded = StoryboardExpander.Expand(storyboard, Tiny.Value);
        Assert.Same(storyboard, expanded.Storyboard);
        var film = Lower(expanded.Storyboard, Tiny.Value);
        Assert.Equal(3, film.Segments.Count);
        Assert.Equal(30 * 12, film.Segments[0].Timeline.FrameCount);
    }

    [Fact]
    public void GrandTour_TheTourShotBecomesTheTour_AfterTheOrbit_WithTheDip()
    {
        var storyboard = BuiltInComposites.GrandTour().Storyboard;
        var expanded = StoryboardExpander.Expand(storyboard, Tiny.Value);
        var tour = Assert.Single(expanded.Tours);
        var shots = expanded.Storyboard.Shots;
        Assert.Equal(1 + tour.Storyboard.Shots.Count, shots.Count);
        Assert.Equal("Outside", shots[0].Name);
        Assert.Equal(tour.Storyboard.Shots.Select(s => s.Name), shots.Skip(1).Select(s => s.Name));
        // The tour begins as the shot did, with the look of its preset.
        Assert.Equal(TransitionKind.Dip, shots[1].In!.Kind);
        Assert.Equal(BuiltInPresets.FullFlythrough().Spec.Materials, shots[1].Materials);
        Assert.All(shots.Skip(1), s => Assert.Null(s.Camera!.Route!.Branches));
        Assert.Empty(StoryboardValidator.Validate(expanded.Storyboard));
        var film = Lower(expanded.Storyboard, Tiny.Value);
        Assert.Equal(shots.Count, film.Segments.Count);
        Assert.NotNull(film.Movie.Texts);
    }

    [Fact]
    public void ExplorationHistory_FliesThePassageSurveyedLast_AndHoldsTheReveal()
    {
        var storyboard = BuiltInComposites.ExplorationHistory().Storyboard;
        var expanded = StoryboardExpander.Expand(storyboard, DatedTiny.Value);
        var route = expanded.Storyboard.Shots[1].Camera!.Route!;
        Assert.Equal("a4", route.From);
        Assert.Equal("v2", route.To);
        Assert.Equal(ShotRevealMode.Hold, expanded.Storyboard.Shots[1].Reveal!.Mode);
        Assert.Contains(expanded.Notices, n => n.Contains("2001", StringComparison.Ordinal));
        var film = Lower(expanded.Storyboard, DatedTiny.Value);
        Assert.Equal(2, film.Segments.Count);
    }

    [Fact]
    public void ExplorationHistory_WithoutDates_FliesTheMainPassage_AndSaysSo()
    {
        var expanded = StoryboardExpander.Expand(BuiltInComposites.ExplorationHistory().Storyboard, Tiny.Value);
        var route = expanded.Storyboard.Shots[1].Camera!.Route!;
        Assert.Null(route.From);
        Assert.Contains(expanded.Notices, n => n.Contains("no survey dates", StringComparison.Ordinal));
    }

    [Fact]
    public void ASymbolicRoute_NotExpanded_IsRefusedWhenLowered()
    {
        var error = Assert.Throws<ArgumentException>(() => Lower(BuiltInComposites.GrandTour().Storyboard, Tiny.Value));
        Assert.Contains("branches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BranchesOtherThanAuto_AreRefusedByTheExpander()
    {
        var storyboard = BuiltInComposites.GrandTour().Storyboard;
        var shot = storyboard.Shots[1];
        storyboard = storyboard with { Shots = [storyboard.Shots[0], shot with { Camera = shot.Camera! with { Route = shot.Camera.Route! with { Branches = "all" } } }] };
        Assert.Throws<ArgumentException>(() => StoryboardExpander.Expand(storyboard, Tiny.Value));
    }

    [Fact]
    public void ACompositeFile_RoundTrips()
    {
        var composite = BuiltInComposites.ExplorationHistory() with { Name = "Mine", BuiltIn = false };
        string json = CompositePresetSerializer.Write(composite);
        var back = CompositePresetSerializer.Read(json);
        Assert.Equal("Mine", back.Name);
        Assert.Equal(composite.Description, back.Description);
        Assert.Equal(StoryboardSerializer.Write(composite.Storyboard), StoryboardSerializer.Write(back.Storyboard));
        Assert.Throws<PresetFormatException>(() => CompositePresetSerializer.Read("{\"version\":1,\"name\":\"x\",\"storyboard\":{},\"extra\":1}"));
        Assert.Throws<PresetFormatException>(() => CompositePresetSerializer.Read("{\"version\":1,\"storyboard\":{\"version\":1,\"shots\":[{}]}}"));
    }

    [Fact]
    public void TheStore_KeepsCompositesBesideThePresets()
    {
        string dir = Path.Combine(Path.GetTempPath(), "composite-store-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new PresetStore(dir);
            store.Save(BuiltInPresets.OrbitShowcase() with { Name = "My orbit" });
            store.SaveComposite(BuiltInComposites.Showcase() with { Name = "My film" });
            File.WriteAllText(Path.Combine(dir, "broken" + PresetStore.CompositeExtension), "{ not json");
            Assert.Equal(["My orbit"], store.Load().Select(p => p.Name));
            var composite = Assert.Single(store.LoadComposites());
            Assert.Equal("My film", composite.Name);
            Assert.False(composite.BuiltIn);
            Assert.True(store.DeleteComposite("My film"));
            Assert.Empty(store.LoadComposites());
            Assert.Single(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AUsersPreset_WrittenIntoTheShots_GivesTheSameShots()
    {
        var mine = BuiltInPresets.HelixDescent() with { Name = "Slow helix", BuiltIn = false };
        mine = mine with { Spec = mine.Spec with { Animation = mine.Spec.Animation with { DurationSeconds = 7 } } };
        RenderPreset? Find(string name) => name == mine.Name ? mine : BuiltInPresets.ByName(name);
        var storyboard = new Storyboard
        {
            Shots =
            [
                new ShotSpec { Name = "One", Preset = "Slow helix" },
                new ShotSpec { Name = "Two", Preset = "Orbit showcase", Seconds = 5 },
            ],
        };
        var inlined = StoryboardExpander.InlinePresets(storyboard, Find);
        Assert.Null(inlined.Shots[0].Preset);
        Assert.Equal("Orbit showcase", inlined.Shots[1].Preset);
        Assert.Empty(StoryboardValidator.Validate(inlined));
        for (int i = 0; i < 2; i++)
            Assert.Equal(
                SceneSpecSerializer.Write(StoryboardPlanner.ShotSpec(storyboard, i, Find)),
                SceneSpecSerializer.Write(StoryboardPlanner.ShotSpec(inlined, i)));
    }

    [Fact]
    public void TheFilesAStoryboardNames_AreRootedAndListed()
    {
        var storyboard = new Storyboard
        {
            Defaults = new StoryboardDefaults { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" } },
            Shots = [new ShotSpec { Reveal = ShotReveal.Of(new RevealSpec { By = RevealBy.Date, DatesPath = "dates.3d" }) }],
        };
        Assert.Equal(["sky.hdr", "dates.3d"], StoryboardFiles.Named(storyboard));
        string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "films"));
        var rooted = StoryboardFiles.Rooted(storyboard, folder);
        Assert.Equal(Path.Combine(folder, "sky.hdr"), rooted.Defaults.Lighting!.HdriPath);
        Assert.Equal(Path.Combine(folder, "dates.3d"), rooted.Shots[0].Reveal!.Spec!.DatesPath);
    }
}
