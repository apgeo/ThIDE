// What a composite says of its tours and of itself, on the invented tour cave: the side passages a
// tour shot visits or leaves out by hand and the order they are wanted in (route include, exclude,
// priority), the language of the texts the film writes itself, and a composite saved with a title
// and an end card read back as it was written.

using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class CompositeChoicesTests
{
    private static readonly Lazy<GeometryResult> Cave = new(() => GeometryStage.Build(TourCave.Build()));

    /// <summary>The Grand tour, its tour shot's route changed by <paramref name="route"/>.</summary>
    private static Storyboard GrandTour(Func<RouteParams, RouteParams>? route = null, string? language = null)
    {
        var film = BuiltInComposites.GrandTour().Storyboard;
        var tour = film.Shots[1];
        var camera = tour.Camera!;
        return film with
        {
            Language = language,
            Shots = [film.Shots[0], tour with { Camera = camera with { Route = (route ?? (r => r))(camera.Route!) } }],
        };
    }

    private static Tour TourOf(Storyboard film, string language = "en")
        => Assert.Single(StoryboardExpander.Expand(film, Cave.Value, language: language).Tours);

    private static TourRow Row(Tour tour, string id) => tour.Rows.Single(r => r.Id == id);

    [Fact]
    public void ATourShotsRoute_SaysWhichSidePassagesAreVisitedAndLeftOutByHand()
    {
        var rules = TourOf(GrandTour());
        Assert.Equal("cut", Row(rules, "B3").Mode);
        Assert.Equal("skip", Row(rules, "B6").Mode);

        var film = GrandTour(r => r with { Include = ["B6"], Exclude = ["B3"] });
        Assert.Empty(StoryboardValidator.Validate(film));
        var spec = StoryboardExpander.TourSpecOf(film, 1);
        Assert.Equal(["B6"], spec.Include);
        Assert.Equal(["B3"], spec.Exclude);
        var tour = TourOf(film);
        Assert.Equal(("excluded", "skip"), (Row(tour, "B3").Why, Row(tour, "B3").Mode));
        Assert.Equal("included", Row(tour, "B6").Why);
        Assert.NotEqual("skip", Row(tour, "B6").Mode);
    }

    [Fact]
    public void TheOrderOfPreference_DecidesWhichSidePassagesTheBudgetLeavesOut()
    {
        // The rules leave the lowest ranked out first; the order said turns that round.
        var ranked = TourBuilder.Build(Cave.Value, new TourSpec()).Rows.Where(r => r.Chosen && r.Kind != "trunk").OrderBy(r => r.Rank).Select(r => r.Id).ToList();
        Assert.Equal(4, ranked.Count);
        var wanted = Enumerable.Reverse(ranked).ToList();
        bool partly = false;
        for (double budget = 200; budget >= 20; budget -= 4)
        {
            var tour = TourBuilder.Build(Cave.Value, new TourSpec { TourSeconds = budget, Priority = wanted });
            var sides = tour.Rows.Where(r => r.Chosen && r.Kind != "trunk").ToList();
            var kept = sides.Where(r => r.Mode != "skip").Select(r => wanted.IndexOf(r.Id)).ToList();
            var dropped = sides.Where(r => r.Dropped == "budget").Select(r => wanted.IndexOf(r.Id)).ToList();
            if (kept.Count == 0 || dropped.Count == 0) continue;
            partly = true;
            Assert.True(kept.Max() < dropped.Min(), $"at {budget} s");
        }
        Assert.True(partly);

        // Said in a composite's route, it reaches the tour; an id that names nothing is a notice.
        var film = GrandTour(r => r with { Priority = [.. wanted, "B42"] });
        Assert.Equal([.. wanted, "B42"], StoryboardExpander.TourSpecOf(film, 1).Priority);
        Assert.Contains(TourOf(film).Notices, n => n == "'B42' (priority) names no side passage of this tour.");
    }

    [Fact]
    public void TheTourTexts_AreInTheFilmsLanguage_ElseInTheLanguageTheRendererGives()
    {
        static string Corner(Tour tour) => tour.Storyboard.Shots.Single(s => s.Name == "B1").Texts!.Corner!;
        Assert.Equal("Side passage B1 · 60 m", Corner(TourOf(GrandTour())));
        Assert.Equal("Galerie laterală B1 · 60 m", Corner(TourOf(GrandTour(), language: "ro")));
        Assert.Equal("Galerie laterală B1 · 60 m", Corner(TourOf(GrandTour(language: "ro"))));
        // The film's own language wins over the renderer's.
        Assert.Equal("Side passage B1 · 60 m", Corner(TourOf(GrandTour(language: "en"), language: "ro")));
        // And the expanded film's shots carry the texts.
        var expanded = StoryboardExpander.Expand(GrandTour(language: "ro"), Cave.Value).Storyboard;
        Assert.Contains(expanded.Shots, s => s.Texts?.Corner == "Galerie laterală B1 · 60 m");
    }

    [Fact]
    public void TheValidator_RefusesALanguageItCannotWrite_AndTourChoicesOnARouteThatIsNoTour()
    {
        Assert.Equal(["language"], StoryboardValidator.Validate(GrandTour(language: "de")).Select(e => e.Path));
        var plain = GrandTour(r => r with { Branches = null, Exclude = ["B3"] });
        Assert.Equal(["shots[1].camera.route"], StoryboardValidator.Validate(plain).Select(e => e.Path));
        // A scene's route refuses them too: they mean nothing to a flight between stations.
        var route = new RouteParams { From = "t0", To = "t20", Priority = ["B1"] };
        var ex = Assert.Throws<ArgumentException>(() => Therion.Blender.Emit.CameraRoutes.Resolve(Therion.Blender.Emit.CameraFraming.FromGeometry(Cave.Value), route));
        Assert.Equal("camera.route", ex.ParamName);
        Assert.Throws<ArgumentException>(() => StoryboardExpander.TourSpecOf(GrandTour(), 0));
    }

    [Fact]
    public void ACompositeSavedWithItsTitleEndCardLanguageAndTourChoices_RoundTrips()
    {
        var film = GrandTour(r => r with { Include = ["B6"], Exclude = ["B3"], Priority = ["B5", "B1"] }, language: "ro") with
        {
            Texts = new OverlaySpec
            {
                Title = "{cave}", Subtitle = "Invented Caving Club", TitleSeconds = 4,
                EndCard = new EndCardSpec { Text = "The end", Subtitle = "Invented survey", Seconds = 5 },
            },
        };
        var composite = new CompositePreset { Name = "My tour", Description = "Invented.", Storyboard = film };
        string json = CompositePresetSerializer.Write(composite);
        Assert.Contains("\"language\": \"ro\"", json);
        Assert.Contains("\"priority\": [", json);
        var back = CompositePresetSerializer.Read(json);
        Assert.Equal(("My tour", "Invented."), (back.Name, back.Description));
        Assert.Equal(json, CompositePresetSerializer.Write(back));
        Assert.Equal("ro", back.Storyboard.Language);
        Assert.Equal("The end", back.Storyboard.Texts!.EndCard!.Text);
        Assert.Equal(["B5", "B1"], back.Storyboard.Shots[1].Camera!.Route!.Priority);
        Assert.Empty(StoryboardValidator.Validate(back.Storyboard));

        // A route without choices is the JSON it was.
        string plain = StoryboardSerializer.Write(BuiltInComposites.GrandTour().Storyboard);
        Assert.DoesNotContain("\"include\"", plain);
        Assert.DoesNotContain("\"priority\"", plain);
        Assert.DoesNotContain("\"language\"", plain);
    }
}
