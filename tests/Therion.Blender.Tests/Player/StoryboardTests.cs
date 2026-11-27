// A storyboard without Blender: how it is written and read, what the validator and the schema
// hold it to, and how it is lowered over an invented cave: where the shots lie in the movie, what
// becomes of their texts and of the reveal, and that a film of one shot is the scene it was.

using System.IO.Compression;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class StoryboardTests
{
    private static readonly SourceSpec Source = new() { PlyPath = "/job/model.ply" };

    private static CameraSpec Orbit => new() { Template = CameraTemplate.Orbit, Orbit = new OrbitParams { Revolutions = 0.25 } };

    private static ShotSpec Shot(string name, double seconds, TransitionSpec? begins = null) => new()
    {
        Name = name, Seconds = seconds, Camera = Orbit, In = begins,
    };

    private static TransitionSpec Dissolve(double seconds) => new() { Kind = TransitionKind.Dissolve, Seconds = seconds };

    private static TransitionSpec Dip(double seconds) => new() { Kind = TransitionKind.Dip, Seconds = seconds, Color = new ColorRgb(1, 1, 1) };

    private static Storyboard Film(params ShotSpec[] shots) => new()
    {
        Name = "{cave}: showcase",
        Fps = 10,
        Shots = shots,
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180, OutputDirectory = "out", BaseName = "film" },
    };

    /// <summary>Three shots: 2 s, then 3 s after a dissolve of half a second, then 2 s after a
    /// dip of 0.6 s. At 10 fps: frames 1–20, 16–45 and 46–65.</summary>
    private static Storyboard ThreeShots => Film(
        Shot("Outside", 2), Shot("Main passage", 3, Dissolve(0.5)), Shot("Way out", 2, Dip(0.6)));

    private static StoryboardPlan Lower(Storyboard storyboard)
        => StoryboardPlanner.Lower(storyboard, Source, PlanFixtures.MediumFraming, PlanFixtures.MediumMeta);

    private static IEnumerable<string> Paths(Storyboard storyboard, bool film = true)
        => StoryboardValidator.Validate(storyboard, film: film).Select(e => e.Path);

    // ---- where the shots lie ----

    [Fact]
    public void Lowering_PutsEveryShotInTheMovie_AndADissolveOverTheShotBefore()
    {
        var film = Lower(ThreeShots);
        var movie = film.Movie;

        Assert.Equal(["segments", "texts", "output"], movie.Features);
        Assert.Equal(10, movie.Timeline.Fps);
        Assert.Equal(65, movie.Timeline.FrameCount);
        Assert.Equal(["s01", "s02", "s03"], movie.Segments.Select(s => s.Id));
        Assert.Equal(["scene-plan.s01.json", "scene-plan.s02.json", "scene-plan.s03.json"], movie.Segments.Select(s => s.Plan));
        Assert.Equal(["s01", "s02", "s03"], movie.Segments.Select(s => s.Frames));
        Assert.Equal(["Outside", "Main passage", "Way out"], movie.Segments.Select(s => s.Name));
        Assert.Equal([0, 1.5, 4.5], movie.Segments.Select(s => s.Start));
        Assert.Equal([20, 30, 20], movie.Segments.Select(s => s.FrameCount));
        Assert.Equal([(1, 0), (16, 5), (46, 0)], movie.Placed());
        Assert.Null(movie.Segments[0].In);
        Assert.Equal(new PlanTransition { Kind = PlanTransitionKind.Dissolve, Seconds = 0.5 }, movie.Segments[1].In);
        Assert.Equal(new PlanTransition { Kind = PlanTransitionKind.Dip, Seconds = 0.6, Color = new ColorRgb(1, 1, 1) }, movie.Segments[2].In);
        Assert.Equal(PlanOutputKind.Video, movie.Output.Kind);
        Assert.Equal("film", movie.Output.BaseName);
        Assert.Equal("out", movie.Output.Directory);
        Assert.Empty(MoviePlanValidator.Validate(movie));

        // Every segment is a scene plan like any other, of its shot's length, with no texts.
        Assert.Equal(3, film.Segments.Count);
        Assert.All(film.Segments, plan => Assert.Empty(ScenePlanValidator.Validate(plan)));
        Assert.Equal([20, 30, 20], film.Segments.Select(p => p.Timeline.FrameCount));
        Assert.All(film.Segments, plan => Assert.Null(plan.Texts));
        Assert.All(film.Segments, plan => Assert.DoesNotContain(ScenePlan.TextsFeature, plan.Features));
        Assert.Equal(["film.s01", "film.s02", "film.s03"], film.Segments.Select(p => p.Output.BaseName));
        Assert.Equal((320, 180), (film.Width, film.Height));
        PlayerWriter.ThrowIfInvalid(film);
    }

    [Fact]
    public void TheFilmsName_HasItsPlaceholdersFilledIn_AndTheLoweringIsTheSameEveryTime()
    {
        var film = Lower(ThreeShots);
        Assert.Equal(TextPlaceholders.CaveName(PlanFixtures.MediumMeta) + ": showcase", film.Movie.Name);

        var again = Lower(ThreeShots);
        Assert.Equal(ScenePlanSerializer.Write(film.Movie), ScenePlanSerializer.Write(again.Movie));
        Assert.Equal(film.Segments.Select(ScenePlanSerializer.Write), again.Segments.Select(ScenePlanSerializer.Write));
    }

    [Fact]
    public void AShot_TakesWhatItLeavesOut_FromItsPreset_OrFromTheDefaults()
    {
        var storyboard = Film(
            new ShotSpec { Name = "Default look", Seconds = 1, Camera = Orbit },
            new ShotSpec { Name = "Preset", Preset = "Helix descent" },
            new ShotSpec { Name = "Preset, changed", Preset = "Helix descent", Seconds = 2, Materials = new MaterialsSpec { Roughness = 0.25 } }) with
        {
            Defaults = new StoryboardDefaults
            {
                Seed = 7,
                Materials = new MaterialsSpec { Roughness = 0.75 },
                Labels = new LabelsSpec { Overlays = new OverlaySpec { Corner = "Invented Caving Club" } },
            },
        };
        var helix = PlanFixtures.Preset("Helix descent");

        var first = StoryboardPlanner.ShotSpec(storyboard, 0);
        Assert.Equal(0.75, first.Materials.Roughness);
        Assert.Equal(7, first.Seed);
        Assert.Equal("Invented Caving Club", first.Labels.Overlays.Corner);
        Assert.Equal(CameraTemplate.Orbit, first.Camera.Template);
        Assert.Equal("film.s01", first.Output.BaseName);

        var second = StoryboardPlanner.ShotSpec(storyboard, 1);
        Assert.Equal(helix.Camera, second.Camera);
        Assert.Equal(helix.Materials, second.Materials);
        Assert.Equal(helix.Lighting, second.Lighting);
        Assert.Equal(helix.Animation.DurationSeconds, second.Animation.DurationSeconds);
        Assert.Equal(10, second.Animation.Fps);
        // The preset's own texts are not the film's: the defaults' are written over the shot.
        Assert.Null(second.Labels.Overlays.Title);
        Assert.Equal("Invented Caving Club", second.Labels.Overlays.Corner);
        Assert.Equal(7, second.Seed);

        var third = StoryboardPlanner.ShotSpec(storyboard, 2);
        Assert.Equal(0.25, third.Materials.Roughness);
        Assert.Equal(2, third.Animation.DurationSeconds);
        Assert.Equal("Preset, changed", third.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => StoryboardPlanner.ShotSpec(storyboard, 3));
    }

    // ---- a route ----

    [Fact]
    public void AShotThatFliesARoute_IsAsLongAsItSays_OrAsTheRouteTakes()
    {
        var route = new CameraSpec { Template = CameraTemplate.Route, Route = new RouteParams { From = RouteParams.Entrance, To = RouteParams.Farthest } };
        var natural = route with { Route = route.Route! with { Timing = RouteTiming.Natural } };
        var storyboard = Film(
            new ShotSpec { Name = "Fitted", Seconds = 3, Camera = route },
            new ShotSpec { Name = "As long as it takes", Camera = natural, In = Dissolve(0.5) },
            new ShotSpec { Name = "Natural, but told", Seconds = 2, Camera = natural },
            Shot("After", 1));
        Assert.Empty(Paths(storyboard));

        var film = Lower(storyboard);

        // The route's own time, and one frame: what the same spec has when it is rendered alone.
        double seconds = CameraRoutes.Measure(PlanFixtures.MediumFraming, natural.Route!).Seconds;
        int frames = (int)Math.Round(10 * Math.Round(seconds + 0.1, 3));
        Assert.True(frames > 30, $"the route takes {seconds:F1} s");
        Assert.Equal([30, frames, 20, 10], film.Movie.Segments.Select(s => s.FrameCount));
        Assert.Equal([0, 2.5, Math.Round(2.5 + frames / 10.0, 6), Math.Round(4.5 + frames / 10.0, 6)], film.Movie.Segments.Select(s => s.Start));
        Assert.Equal(frames, film.Segments[1].Timeline.FrameCount);
        Assert.All(film.Segments, plan => Assert.Empty(ScenePlanValidator.Validate(plan)));
        Assert.All(film.Segments.Take(3), plan => Assert.Equal(plan.Timeline.FrameCount, plan.Camera.Track.Count));
        Assert.Equal(RouteTiming.Fit, StoryboardPlanner.ShotSpec(storyboard, 2).Camera.Route!.Timing);
        Assert.Equal(RouteTiming.Natural, StoryboardPlanner.ShotSpec(storyboard, 1).Camera.Route!.Timing);
        PlayerWriter.ThrowIfInvalid(film);

        // A station the cave does not have is named with its shot.
        var lost = Assert.Throws<ArgumentException>(() => Lower(Film(Shot("Outside", 1), new ShotSpec { Name = "Lost", Seconds = 2, Camera = route with { Route = new RouteParams { From = "no.such", To = RouteParams.Farthest } } })));
        Assert.StartsWith("Shot 2 (Lost): ", lost.Message);
        Assert.Contains("no.such", lost.Message);
    }

    // ---- one shot is the scene it was ----

    [Fact]
    public void AFilmOfOneShot_HasTheSceneOfTheSameSpec_ButForItsTextsAndItsOutput()
    {
        var spec = PlanFixtures.WithPaths(PlanFixtures.Preset("Helix descent") with
        {
            Seed = 5,
            Animation = new AnimationSpec { Fps = 10, DurationSeconds = 3 },
            Labels = new LabelsSpec
            {
                Stations = new StationLabelSpec { Show = true },
                Overlays = new OverlaySpec { Title = "Invented Cave", NorthArrow = true, ScaleBar = true },
            },
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180, BaseName = "film" },
        }) with { Source = Source };
        var single = ScenePlanBuilder.FromSpec(spec, PlanFixtures.MediumFraming, PlanFixtures.MediumMeta);

        var storyboard = new Storyboard
        {
            Fps = 10,
            Defaults = new StoryboardDefaults
            {
                Seed = 5, Engine = spec.Engine, Materials = spec.Materials, Lighting = spec.Lighting, Labels = spec.Labels,
            },
            Shots = [new ShotSpec { Seconds = 3, Camera = spec.Camera }],
            Output = spec.Output,
        };
        var film = Lower(storyboard);

        // What shapes the frames is the same plan: the first stage's digest leaves out the name,
        // the texts and the output.
        var segment = Assert.Single(film.Segments);
        Assert.Equal(
            ScenePlanSerializer.Write(single with { Texts = null, Features = single.Features.Where(f => f != ScenePlan.TextsFeature).ToList() }),
            ScenePlanSerializer.Write(segment with { Output = single.Output }));

        // And the movie has the scene's texts, each under its shot's id, for the same times.
        Assert.Equal(single.Texts!.Items.Select(i => "s01." + i.Id), film.Movie.Texts!.Items.Select(i => i.Id));
        Assert.Equal(single.Texts.Items.Select(i => (i.Start, i.End, i.Text)), film.Movie.Texts.Items.Select(i => (i.Start, i.End, i.Text)));
        Assert.Equal(single.Timeline, film.Movie.Timeline);
        Assert.Equal(single.OutputFrameCount, film.Movie.OutputFrameCount);
    }

    // ---- texts ----

    [Fact]
    public void AShotsTexts_AreInTheMovie_InTheMoviesTime_AndGoWhereTheNextShotBegins()
    {
        var storyboard = ThreeShots;
        storyboard = storyboard with
        {
            Shots =
            [
                storyboard.Shots[0] with { Texts = new OverlaySpec { Corner = "Outside" } },
                storyboard.Shots[1] with
                {
                    Texts = new OverlaySpec
                    {
                        Corner = "{cave}", NorthArrow = true, ScaleBar = true,
                        Captions = [new CaptionSpec { Text = "The big chamber", Start = 0.5, End = 1.5 }, new CaptionSpec { Text = "Late", Start = 2.5, End = 9 }],
                    },
                },
                storyboard.Shots[2],
            ],
        };
        var film = Lower(storyboard);
        var items = film.Movie.Texts!.Items.ToDictionary(i => i.Id);
        string cave = TextPlaceholders.CaveName(PlanFixtures.MediumMeta);

        // The first shot's corner text goes half-way through the dissolve, where the second's comes.
        Assert.Equal((null, 1.7), (items["s01.corner"].Start, items["s01.corner"].End));
        Assert.Equal((1.7, 4.5), (items["s02.corner"].Start, items["s02.corner"].End));
        Assert.Equal(cave, items["s02.corner"].Text);
        // A caption keeps its time in its shot, and is cut where the shot ends.
        Assert.Equal((2.0, 3.0), (items["s02.caption.1"].Start, items["s02.caption.1"].End));
        Assert.Equal((4.0, 4.5), (items["s02.caption.2"].Start, items["s02.caption.2"].End));
        // What is measured through the shot's camera is hidden while the dissolve and the dip run:
        // frames 21 to 42 of the movie (the dip takes the shot's last three frames).
        var arrow = film.Movie.Texts.Items.Single(i => i.Role == ScreenTextPlanner.NorthArrowRole);
        Assert.Equal("s02.north-arrow", arrow.Id);
        Assert.Equal((2.0, 4.2), (arrow.Start, arrow.End));
        Assert.All(arrow.Turn!, turn => Assert.True(turn.T >= 1.5));
        Assert.All(film.Movie.Texts.Items.Where(i => i.Role == ScreenTextPlanner.ScaleBarRole), bar => Assert.True(bar.Start >= 2.0 && bar.End <= 4.2));
        Assert.Contains(film.Movie.Texts.Items, i => i.Role == ScreenTextPlanner.ScaleBarRole);
        Assert.Empty(MoviePlanValidator.Validate(film.Movie));

        // The captions are the film's subtitles, in its time.
        Assert.Equal([("The big chamber", 2.0, 3.0), ("Late", 4.0, 4.5)], film.Captions.Select(c => (c.Text, c.Start, c.End)));
    }

    [Fact]
    public void TheFilmsTexts_LieOverTheWholeMovie_AndItsEndCardHoldsTheLastFrame()
    {
        var storyboard = ThreeShots with
        {
            Texts = new OverlaySpec
            {
                Title = "{cave}", TitleSeconds = 1.5, Credits = "Surveyed by the Invented Caving Club",
                EndCard = new EndCardSpec { Text = "The end", Seconds = 2 }, FadeFromBlack = 0.5, FadeToBlack = 0.4,
                Captions = [new CaptionSpec { Text = "Five kilometres of passage", Start = 3, End = 5 }],
            },
        };
        var film = Lower(storyboard);
        var texts = film.Movie.Texts!;

        Assert.Equal((2, 0.5, 0.4), (texts.Hold, texts.FromBlack, texts.ToBlack));
        Assert.Equal(65 + 20, film.Movie.OutputFrameCount);
        var title = texts.Items.Single(i => i.Id == "title");
        Assert.Equal(TextPlaceholders.CaveName(PlanFixtures.MediumMeta), title.Text);
        Assert.Null(title.Start);
        Assert.Null(texts.Items.Single(i => i.Id == "credits").End);
        // The end card comes after the last frame of the last shot: 6.5 s into the film.
        Assert.Equal(6.5, texts.Items.Single(i => i.Id == "end-card").Start);
        Assert.Equal((3, 5), (texts.Items.Single(i => i.Id == "caption.1").Start, texts.Items.Single(i => i.Id == "caption.1").End));
        Assert.Equal(["Five kilometres of passage"], film.Captions.Select(c => c.Text));
        Assert.Empty(MoviePlanValidator.Validate(film.Movie));
    }

    // ---- the reveal ----

    private static readonly RevealSpec ByDistance = new() { By = RevealBy.Distance, Start = 0.5, End = 1.5, CounterFormat = "{} m" };

    private static (StoryboardPlan Film, SceneMeta Meta) LowerRevealed(Storyboard storyboard)
    {
        var geometry = GeometryStage.Build(
            InventedCave.Build(InventedCaveSize.Small), RevealPlanner.GeometryFor(StoryboardPlanner.ConversionSpec(storyboard)));
        var meta = SceneMeta.Build(geometry);
        return (StoryboardPlanner.Lower(storyboard, Source, CameraFraming.FromGeometry(geometry), meta), meta);
    }

    private static PlanChange? RevealOf(ScenePlan plan) => plan.Changes?.SingleOrDefault(c => c.Reveal is not null);

    [Fact]
    public void TheReveal_RunsInItsShot_IsHeldByTheNext_AndEachSegmentHasTheKeysOfItsStretch()
    {
        var shots = ThreeShots.Shots;
        var storyboard = ThreeShots with
        {
            Shots =
            [
                shots[0] with { Reveal = ShotReveal.Of(ByDistance) },
                shots[1] with { Reveal = ShotReveal.Hold },
                shots[2] with { Reveal = ShotReveal.None },
            ],
        };
        Assert.Equal(RevealBy.Distance, StoryboardPlanner.ConversionSpec(storyboard).Reveal!.By);
        var (film, meta) = LowerRevealed(storyboard);
        Assert.NotNull(meta.Reveal);

        // The first segment runs the reveal in its own time, as a scene of its own would.
        var first = RevealOf(film.Segments[0])!;
        Assert.Equal([0.5, 1.5], first.Reveal!.Select(k => k.T));
        Assert.Equal(meta.Reveal!.Min, first.Reveal![0].Value, 6);
        Assert.True(first.Reveal[1].Value > meta.Reveal.Max);
        // The second holds what the first ended on: one key, and the same look of the edge.
        var held = RevealOf(film.Segments[1])!;
        Assert.Equal([new PlanNumberKey(0, first.Reveal[1].Value)], held.Reveal);
        Assert.Equal(first.RevealFront, held.RevealFront);
        // The third shows the whole cave.
        Assert.Null(RevealOf(film.Segments[2]));
        Assert.All(film.Segments, plan => Assert.Empty(ScenePlanValidator.Validate(plan)));

        // The counters are texts of the movie and count the same keys, in the movie's time.
        var counters = film.Movie.Texts!.Items.Where(i => i.Counter is not null).ToList();
        Assert.Equal(["s01.reveal-counter", "s02.reveal-counter"], counters.Select(c => c.Id));
        Assert.Equal(first.Reveal, counters[0].Counter!.Keys);
        Assert.Equal([new PlanNumberKey(1.5, first.Reveal[1].Value)], counters[1].Counter!.Keys);
        Assert.Equal((null, 1.7), (counters[0].Start, counters[0].End));
        Assert.Equal((1.7, 4.5), (counters[1].Start, counters[1].End));
        Assert.All(counters, c => Assert.Equal("{} m", c.Text));
        Assert.Empty(MoviePlanValidator.Validate(film.Movie));
        Assert.Contains("\"counter\": { \"keys\": [{ \"t\": 0.5, \"value\": ", ScenePlanSerializer.Write(film.Movie));
    }

    [Fact]
    public void AReveal_ThatHasNotEnded_IsHeldWhereItHadGotTo()
    {
        var shots = ThreeShots.Shots;
        // The reveal would take four seconds; its shot has two.
        var storyboard = Film(
            shots[0] with { Reveal = ShotReveal.Of(ByDistance with { Start = 0, End = 4 }) },
            shots[1] with { Reveal = ShotReveal.Hold, In = null });
        var (film, _) = LowerRevealed(storyboard);

        var keys = RevealOf(film.Segments[0])!.Reveal!;
        double held = RevealOf(film.Segments[1])!.Reveal!.Single().Value;
        // On the shot's last frame, the twentieth of the forty the reveal's keys are apart.
        Assert.Equal(keys[0].Value + (keys[1].Value - keys[0].Value) * 19 / 40, held, 5);
    }

    [Fact]
    public void Reveals_ByDifferentThings_AreRefused_AndSoIsAHoldWithNothingBefore()
    {
        var shots = ThreeShots.Shots;
        Assert.Contains("shots[1].reveal", Paths(ThreeShots with
        {
            Shots = [shots[0] with { Reveal = ShotReveal.Of(ByDistance) }, shots[1] with { Reveal = ShotReveal.Of(new RevealSpec { By = RevealBy.Order }) }],
        }));
        Assert.Contains("shots[0].reveal", Paths(ThreeShots with { Shots = [shots[0] with { Reveal = ShotReveal.Hold }] }));
        Assert.Empty(Paths(ThreeShots with
        {
            Defaults = new StoryboardDefaults { Reveal = ByDistance },
            Shots = [shots[0], shots[1] with { Reveal = ShotReveal.Of(ByDistance with { Start = 1 }) }],
        }));
    }

    [Fact]
    public void WhatTheFilmCannotShow_IsSaid_WithItsShotNamed()
    {
        var shots = ThreeShots.Shots;
        var film = Lower(ThreeShots with
        {
            Shots =
            [
                shots[0],
                // Nothing was revealed before it; and one of its captions begins after it has ended.
                shots[1] with
                {
                    Reveal = ShotReveal.Hold,
                    Texts = new OverlaySpec { Captions = [new CaptionSpec { Text = "In time", Start = 1, End = 2 }, new CaptionSpec { Text = "Too late", Start = 5, End = 6 }] },
                },
                shots[2] with { Annotations = [new AnnotationSpec { Id = "far", Text = "Never near", Station = "upper0", Within = 0.001 }] },
            ],
        });

        Assert.Contains("Shot 2 (Main passage): the shot before it left no reveal to hold; the whole cave is shown.", film.Notices);
        Assert.Contains("Shot 2 (Main passage): 1 of its captions lie outside the time the shot's texts show for, and are left out.", film.Notices);
        Assert.Contains(film.Notices, n => n.StartsWith("Shot 3 (Way out): ", StringComparison.Ordinal) && n.Contains("far", StringComparison.Ordinal));
        Assert.Equal(["In time"], film.Captions.Select(c => c.Text));
        Assert.Null(RevealOf(film.Segments[1]));
        Assert.Empty(Lower(ThreeShots).Notices);
    }

    // ---- the validator ----

    [Fact]
    public void TheValidator_NamesWhatIsWrong_ByItsPathInTheFile()
    {
        var shots = ThreeShots.Shots;
        Assert.Empty(Paths(ThreeShots));
        Assert.Contains("shots", Paths(Film()));
        Assert.Contains("fps", Paths(ThreeShots with { Fps = 0 }));
        Assert.Contains("version", Paths(ThreeShots with { Version = 2 }));
        Assert.Contains("output.kind", Paths(ThreeShots with { Output = ThreeShots.Output with { Kind = OutputKind.Still } }));
        Assert.Equal(["output.width"], Paths(ThreeShots with { Output = ThreeShots.Output with { Width = 4 } }));
        Assert.Contains("shots[1].preset", Paths(Film(shots[0], new ShotSpec { Preset = "No such preset" })));
        Assert.Contains("shots[0].seconds", Paths(Film(shots[0] with { Seconds = 0 })));
        Assert.Contains("shots[0].camera.focalLength", Paths(Film(shots[0] with { Camera = Orbit with { FocalLength = 0 } })));
        Assert.Contains("shots[0].camera.template", Paths(Film(shots[0] with { Camera = new CameraSpec { Template = CameraTemplate.StillSet } })));

        // How a shot begins.
        Assert.Contains("shots[0].in", Paths(Film(shots[0] with { In = Dissolve(0.5) })));
        Assert.Contains("shots[1].in.seconds", Paths(Film(shots[0], shots[1] with { In = Dissolve(0.01) })));
        Assert.Contains("shots[1].in.seconds", Paths(Film(shots[0], shots[1] with { In = Dissolve(2.5) })));
        // A dissolve in and a dip out may not meet in the middle of a shot.
        Assert.Contains("shots[2].in.seconds", Paths(Film(shots[0], Shot("Short", 1, Dissolve(0.8)), Shot("Last", 2, Dip(0.6)))));
        Assert.Contains("shots[1].in.color", Paths(Film(shots[0], shots[1] with { In = Dip(0.6) with { Color = new ColorRgb(2, 0, 0) } })));

        // Whose the texts are.
        Assert.Contains("shots[0].texts.endCard", Paths(Film(shots[0] with { Texts = new OverlaySpec { EndCard = new EndCardSpec { Text = "End" } } })));
        Assert.Contains("shots[0].texts", Paths(Film(shots[0] with { Texts = new OverlaySpec { FadeToBlack = 1 } })));
        Assert.Contains("texts.scaleBar", Paths(ThreeShots with { Texts = new OverlaySpec { ScaleBar = true } }));
        Assert.Contains("texts.northArrow", Paths(ThreeShots with { Texts = new OverlaySpec { NorthArrow = true } }));
        Assert.Contains("texts.endCard.seconds", Paths(ThreeShots with { Texts = new OverlaySpec { EndCard = new EndCardSpec { Text = "End", Seconds = -1 } } }));

        var invalid = Assert.Throws<ArgumentException>(() => Lower(Film(shots[0] with { In = Dissolve(0.5) })));
        Assert.Contains("shots[0].in", invalid.Message);
    }

    [Fact]
    public void OneShotAlone_MayBeAStill_AndIsNotHeldToHowTheShotsAreJoined()
    {
        var storyboard = Film(ThreeShots.Shots[0] with { In = Dissolve(0.5) }) with { Output = ThreeShots.Output with { Kind = OutputKind.Still } };
        Assert.NotEmpty(Paths(storyboard));
        Assert.Empty(Paths(storyboard, film: false));
        Assert.Equal(OutputKind.Still, StoryboardPlanner.ShotSpec(storyboard, 0).Output.Kind);
    }

    // ---- written and read ----

    private const string Example = """
        {
          "version": 1,
          "name": "{cave}: showcase",
          "fps": 30,
          "defaults": { "materials": { "rock": "depthGradient" }, "lighting": { "rig": "threePoint" } },
          "texts": { "title": "{cave}", "titleSeconds": 3, "endCard": { "text": "Surveyed by the Invented Caving Club" } },
          "shots": [
            { "name": "Outside", "preset": "Orbit showcase", "seconds": 12 },
            { "name": "Main passage", "preset": "Full flythrough", "seconds": 21, "in": { "kind": "dissolve", "seconds": 1 },
              "reveal": "hold", "texts": { "corner": "Main passage", "northArrow": true } },
            { "name": "Way out", "preset": "Helix descent", "seconds": 11, "in": { "kind": "dip", "seconds": 0.6, "color": { "r": 1, "g": 1, "b": 1 } },
              "reveal": { "by": "distance", "end": 8 } }
          ],
          "output": { "kind": "video", "container": "mp4", "width": 1280, "height": 720, "baseName": "invented-showcase" }
        }
        """;

    [Fact]
    public void AStoryboard_IsReadFromJson_WithItsWordsInAnyCase()
    {
        var storyboard = StoryboardSerializer.Read(Example);

        Assert.Equal("{cave}: showcase", storyboard.Name);
        Assert.Equal(30, storyboard.Fps);
        Assert.Equal(3, storyboard.Shots.Count);
        Assert.Equal(TransitionKind.Dissolve, storyboard.Shots[1].In!.Kind);
        Assert.Equal(ShotRevealMode.Hold, storyboard.Shots[1].Reveal!.Mode);
        Assert.Equal(new ColorRgb(1, 1, 1), storyboard.Shots[2].In!.Color);
        Assert.Equal(RevealBy.Distance, storyboard.Shots[2].Reveal!.Spec!.By);
        Assert.Equal(8, storyboard.Shots[2].Reveal!.Spec!.End);
        Assert.Equal(3, storyboard.Texts!.TitleSeconds);
        Assert.Equal((1280, 720, "invented-showcase"), (storyboard.Output.Width, storyboard.Output.Height, storyboard.Output.BaseName));
        Assert.Empty(StoryboardValidator.Validate(storyboard));
        Assert.Empty(JsonSchemaSubset.Validate(StoryboardSchema.Text, Example));

        // 12 s, then 21 s that start a second early, then 11 s.
        var film = Lower(storyboard);
        Assert.Equal([0, 11, 32], film.Movie.Segments.Select(s => s.Start));
        Assert.Equal(1290, film.Movie.Timeline.FrameCount);
    }

    [Fact]
    public void AStoryboard_IsWrittenWithWhatItSays_AndReadsBackTheSame()
    {
        var storyboard = StoryboardSerializer.Read(Example) with
        {
            Defaults = new StoryboardDefaults { Seed = 3, Annotations = [new AnnotationSpec { Id = "sump", Text = "Sump", Station = "a.3" }] },
        };
        string json = StoryboardSerializer.Write(storyboard);

        Assert.Equal(json, StoryboardSerializer.Write(StoryboardSerializer.Read(json)));
        Assert.Empty(JsonSchemaSubset.Validate(StoryboardSchema.Text, json));
        Assert.DoesNotContain("\r", json);
        Assert.Contains("\"reveal\": \"hold\"", json);
        Assert.Contains("\"seed\": 3", json);
        // A shot says what it has and no more; a cut is not written at all.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("shots")[0];
        Assert.Equal(["name", "seconds", "preset"], first.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["kind", "seconds"], doc.RootElement.GetProperty("shots")[1].GetProperty("in").EnumerateObject().Select(p => p.Name));
        Assert.Equal(["kind", "seconds", "color"], doc.RootElement.GetProperty("shots")[2].GetProperty("in").EnumerateObject().Select(p => p.Name));
        // Every key the writer has for the storyboard's own objects is one the schema knows.
        string full = StoryboardSerializer.Write(new Storyboard
        {
            Name = "n", Texts = new OverlaySpec(), Defaults = new StoryboardDefaults
            {
                Seed = 2, Engine = new EngineSpec(), Materials = new MaterialsSpec(), Lighting = new LightingSpec(), Labels = new LabelsSpec(),
                Surface = new SurfaceSpec(), Annotations = [], Reveal = new RevealSpec(),
            },
            Shots =
            [
                new ShotSpec
                {
                    Name = "n", Seconds = 1, Preset = "Orbit showcase", Camera = new CameraSpec(), Materials = new MaterialsSpec(),
                    Lighting = new LightingSpec(), Engine = new EngineSpec(), Labels = new LabelsSpec(), Annotations = [],
                    Reveal = ShotReveal.None, Texts = new OverlaySpec(), In = Dip(1),
                },
            ],
        });
        Assert.Empty(JsonSchemaSubset.Validate(StoryboardSchema.Text, full));
    }

    [Theory]
    [InlineData("\"shots\": [", "\"tint\": 1, \"shots\": [", "tint")]
    [InlineData("\"name\": \"Outside\"", "\"name\": \"Outside\", \"carriesOn\": true", "carriesOn")]
    [InlineData("\"kind\": \"dissolve\"", "\"kind\": \"dissolve\", \"curve\": 2", "curve")]
    [InlineData("\"rig\": \"threePoint\"", "\"rig\": \"threePoint\", \"glare\": 1", "glare")]
    [InlineData("\"reveal\": \"hold\"", "\"reveal\": \"keep\"", "keep")]
    public void AKeyThatIsNotKnown_IsRefused_AtEveryDepth(string old, string changed, string named)
    {
        var refused = Assert.Throws<StoryboardFormatException>(() => StoryboardSerializer.Read(Example.Replace(old, changed)));
        Assert.Contains(named, refused.Message);
    }

    [Fact]
    public void AStoryboard_OfANewerVersion_OrThatIsNoJson_IsRefusedWithoutItsText()
    {
        Assert.Contains("newer", Assert.Throws<StoryboardFormatException>(() => StoryboardSerializer.Read(Example.Replace("\"version\": 1", "\"version\": 2"))).Message);
        Assert.Contains("version", Assert.Throws<StoryboardFormatException>(() => StoryboardSerializer.Read("{}")).Message);
        var notJson = Assert.Throws<StoryboardFormatException>(() => StoryboardSerializer.Read("Peștera Inventată, 1987"));
        Assert.DoesNotContain("Inventată", notJson.Message);
    }

    // ---- the movie's plan ----

    [Fact]
    public void TheMoviesPlan_IsAPlanThePlayerReads_AndReadsBackTheSame()
    {
        var movie = Lower(ThreeShots with { Texts = new OverlaySpec { Title = "Invented Cave" } }).Movie;
        string json = ScenePlanSerializer.Write(movie);

        Assert.True(ScenePlanSerializer.IsMovie(json));
        Assert.False(ScenePlanSerializer.IsMovie(ScenePlanSerializer.Write(PlanFixtures.Full())));
        Assert.False(ScenePlanSerializer.IsMovie("not a plan"));
        Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json));
        Assert.Equal(json, ScenePlanSerializer.Write(ScenePlanSerializer.ReadMovie(json)));
        Assert.Contains("\"kind\": \"dissolve\"", json);
        Assert.Contains("\"color\": [1, 1, 1]", json);

        var unknown = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.ReadMovie(json.Replace("\"frames\": \"s02\"", "\"frames\": \"s02\", \"tint\": 1")));
        Assert.Contains("segments[1].tint", unknown.Message);
        Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.ReadMovie(json.Replace("\"segments\": [", "\"models\": [], \"segments\": [")));
    }

    [Fact]
    public void TheMoviesValidator_HoldsTheSegmentsToTheirPlaces()
    {
        var movie = Lower(ThreeShots).Movie;
        IEnumerable<string> Bad(Func<IReadOnlyList<PlanSegment>, IEnumerable<PlanSegment>> change, int? frames = null)
            => MoviePlanValidator.Validate(movie with
            {
                Segments = change(movie.Segments).ToList(),
                Timeline = movie.Timeline with { FrameCount = frames ?? movie.Timeline.FrameCount },
            }).Select(e => e.Path);

        Assert.Contains("segments", Bad(_ => []));
        Assert.Contains("timeline.frameCount", Bad(s => s, frames: 70));
        Assert.Contains("segments[1].start", Bad(s => [s[0], s[1] with { Start = 2 }, s[2]]));
        Assert.Contains("segments[1].id", Bad(s => [s[0], s[1] with { Id = "s01" }, s[2]]));
        Assert.Contains("segments[1].frames", Bad(s => [s[0], s[1] with { Frames = "s01" }, s[2]]));
        Assert.Contains("segments[2].plan", Bad(s => [s[0], s[1], s[2] with { Plan = "../other.json" }]));
        Assert.Contains("segments[0]", Bad(s => [s[0] with { In = s[1].In }, s[1], s[2]]));
        Assert.Contains("segments[2].in.seconds", Bad(s => [s[0], s[1], s[2] with { In = new PlanTransition { Kind = PlanTransitionKind.Dip, Seconds = 10 } }]));
        Assert.Contains("features", MoviePlanValidator.Validate(movie with { Features = ["segments", "output"] }).Select(e => e.Path));
        Assert.Contains("output.kind", MoviePlanValidator.Validate(movie with { Output = movie.Output with { Kind = PlanOutputKind.Still } }).Select(e => e.Path));
    }

    // ---- a draft, a job folder, a bundle ----

    [Fact]
    public void ADraft_IsADraftOfEverySegment_UnderNamesOfItsOwn()
    {
        var film = Lower(ThreeShots);
        var draft = film.Draft();

        Assert.All(draft.Segments, plan => Assert.Equal(PlanEngine.Workbench, plan.Look.Engine));
        Assert.Equal((160, 90), (draft.Width, draft.Height));
        Assert.Equal(["scene-plan.s01.draft.json", "scene-plan.s02.draft.json", "scene-plan.s03.draft.json"], draft.Movie.Segments.Select(s => s.Plan));
        Assert.Equal("film-draft", draft.Movie.Output.BaseName);
        Assert.Equal(film.Movie.Timeline, draft.Movie.Timeline);
        Assert.Equal(film.Movie.Segments.Select(s => (s.Start, s.FrameCount, s.Frames)), draft.Movie.Segments.Select(s => (s.Start, s.FrameCount, s.Frames)));
        PlayerWriter.ThrowIfInvalid(draft);
    }

    [Fact]
    public void AJobFolder_HoldsTheMoviesPlan_ThePlanOfEverySegment_AndOneMesh()
    {
        string dir = Path.Combine(Path.GetTempPath(), "thide-film-job-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string mesh = Path.Combine(dir, "source.ply");
            File.WriteAllText(mesh, "ply");
            var film = Lower(ThreeShots);

            var job = PlayerWriter.WriteJob(film, Path.Combine(dir, "job"), new Dictionary<string, string> { ["model.ply"] = mesh });

            Assert.Equal(Path.Combine(dir, "job", "scene-plan.json"), job.PlanPath);
            Assert.True(ScenePlanSerializer.IsMovie(File.ReadAllText(job.PlanPath)));
            foreach (var segment in film.Movie.Segments)
                Assert.Equal(segment.FrameCount, ScenePlanSerializer.ReadFile(Path.Combine(dir, "job", segment.Plan)).Timeline.FrameCount);
            Assert.True(File.Exists(Path.Combine(dir, "job", "model.ply")));
            Assert.True(File.Exists(Path.Combine(dir, "job", "thide_player", "modules", "segments.py")));
            Assert.Throws<FileNotFoundException>(() => PlayerWriter.WriteJob(film, Path.Combine(dir, "empty")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("en", "A FILM OF SEVERAL SHOTS")]
    [InlineData("ro", "UN FILM DIN MAI MULTE SECVENȚE")]
    public void ABundle_OfAFilm_HoldsEveryPlan_AndItsReadmeSaysHowItIsRendered(string language, string heading)
    {
        var film = Lower(ThreeShots with { Texts = new OverlaySpec { Captions = [new CaptionSpec { Text = "The big chamber", Start = 1, End = 2 }] } });
        var content = new RenderBundleContent
        {
            Plan = film.Segments[0], Film = film, FilmDraft = film.Draft(), Language = language, Captions = film.Captions,
            Streams = new Dictionary<string, Func<Stream>> { ["model.ply"] = () => new MemoryStream([1, 2, 3]) },
        };
        using var zip = new MemoryStream();
        var entries = RenderBundleWriter.WriteZip(content, zip, "film");

        foreach (string name in new[]
                 {
                     "scene-plan.json", "scene-plan.draft.json", "scene-plan.s01.json", "scene-plan.s02.json", "scene-plan.s03.json",
                     "scene-plan.s01.draft.json", "scene-plan.s03.draft.json", "model.ply", "render.sh", "render.cmd", "README.txt",
                     "thide_player/modules/segments.py", "out/film.srt", "out/film.vtt",
                 })
            Assert.Contains("film/" + name, entries);
        Assert.Equal(entries.Count, entries.Distinct().Count());

        using var archive = new ZipArchive(new MemoryStream(zip.ToArray()), ZipArchiveMode.Read);
        string Read(string name)
        {
            using var reader = new StreamReader(archive.GetEntry("film/" + name)!.Open());
            return reader.ReadToEnd();
        }
        Assert.True(ScenePlanSerializer.IsMovie(Read("scene-plan.json")));
        Assert.True(ScenePlanSerializer.IsMovie(Read("scene-plan.draft.json")));
        Assert.Equal(ScenePlanSerializer.Write(film.Segments[1]), Read("scene-plan.s02.json"));
        string readme = Read("README.txt");
        Assert.Contains(heading, readme);
        Assert.Contains("scene-plan.s01.json ... scene-plan.s03.json", readme);
        Assert.Contains("out/film.mp4", readme);
        Assert.Contains("65", readme);
        Assert.Contains("--plan scene-plan.s01.json", readme);

        // The same content gives the same bytes.
        using var again = new MemoryStream();
        RenderBundleWriter.WriteZip(content, again, "film");
        Assert.Equal(zip.ToArray(), again.ToArray());
    }

    [Fact]
    public void ABundle_RefusesAFilm_WhoseOutputLiesOutsideIt_OrWhoseFilesAreMissing()
    {
        var film = Lower(ThreeShots);
        var content = new RenderBundleContent { Plan = film.Segments[0], Film = film };
        Assert.Contains("model.ply", Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(content, new MemoryStream(), "film")).Message);

        var outside = Lower(ThreeShots with { Output = ThreeShots.Output with { OutputDirectory = "/elsewhere" } });
        var refused = Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(
            new RenderBundleContent
            {
                Plan = outside.Segments[0], Film = outside,
                Streams = new Dictionary<string, Func<Stream>> { ["model.ply"] = () => new MemoryStream([1]) },
            }, new MemoryStream(), "film"));
        Assert.Contains("output directory", refused.Message);
    }

    [Fact]
    public void ADipOfOneFrame_ShowsItsColourOnTheFirstFrameOfTheShotItLeadsTo_Only()
    {
        // 10 fps: a dip of 0.1 s takes one frame. The shot before keeps all of its frames (the
        // player's colour strip starts on the next shot's first frame), the next one gives one.
        var storyboard = new Storyboard { Fps = 10, Shots = [Shot("One", 1), Shot("Two", 1, Dip(0.1))], Output = new OutputSpec { BaseName = "film" } };
        var timing = StoryboardPlanner.Timing(storyboard, [11, 11]);
        Assert.Equal(new ShotTiming(12, 11, 0, 0, 1), timing[1]);
    }
}
