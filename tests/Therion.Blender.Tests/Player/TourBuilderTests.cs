// A tour lowered to a storyboard, without Blender: the shots and how each begins, the texts and
// the lead markers, the two time budgets and each thing done to keep within them, in their
// order, and the storyboard as a valid film that lowers to one plan a shot. The cave is invented
// (SceneProbe/TourCave): a main passage of 200 m with six side systems.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests.Player;

public class TourBuilderTests
{
    private static readonly Lazy<GeometryResult> Cave = new(() => GeometryStage.Build(TourCave.Build()));

    private static Tour Build(TourSpec? spec = null, IReadOnlyList<SourceLead>? leads = null)
    {
        var tour = TourBuilder.Build(Cave.Value, spec ?? new TourSpec(), leads);
        Assert.Empty(StoryboardValidator.Validate(tour.Storyboard).Select(e => $"{e.Path}: {e.Message}"));
        return tour;
    }

    private static TourRow Row(Tour tour, string id) => tour.Rows.Single(r => r.Id == id);

    private static ShotSpec Shot(Tour tour, string name) => tour.Storyboard.Shots.Single(s => s.Name == name);

    private static IReadOnlyList<string> Way(ShotSpec shot) => shot.Camera!.Route!.Stations;

    private static string[] T(int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => $"t{i}").ToArray();

    // ---- the film ----

    [Fact]
    public void ATour_FliesTheMainPassageInStretches_AndVisitsTheChosenSidePassagesAtTheirJunctions()
    {
        var tour = Build();
        var shots = tour.Storyboard.Shots;
        Assert.Equal(["Main passage 1", "B1", "Main passage 2", "B3", "B4", "Main passage 3", "B5", "Main passage 4"], shots.Select(s => s.Name));
        Assert.All(shots, s => Assert.Equal(CameraTemplate.Route, s.Camera!.Template));
        Assert.All(shots, s => Assert.Equal("Full flythrough", s.Preset));
        Assert.All(shots, s => Assert.NotNull(s.Seconds));

        Assert.Equal(T(0, 3), Way(shots[0]));
        Assert.Equal(["t3", "n1", "n2", "n3", "n4", "n5", "n6"], Way(shots[1]));
        Assert.Equal(T(3, 9), Way(shots[2]));
        Assert.Equal("t9", Way(shots[3])[0]);
        Assert.Equal(["t9", "p1", "p2", "p3", "t13"], Way(shots[4]));
        // After the bypass the film goes back to its first junction: no passage is missed.
        Assert.Equal(T(9, 16), Way(shots[5]));
        Assert.Equal(["t16", "x1", "x2", "x3"], Way(shots[6]));
        Assert.Equal(T(16, 20), Way(shots[7]));

        // The camera holds at a dead end, and briefly where a bypass meets the main passage again.
        Assert.Equal(TourBuilder.EndHoldSeconds, shots[1].Camera!.Route!.EndHoldSeconds);
        Assert.Equal(TourBuilder.ThroughHoldSeconds, shots[4].Camera!.Route!.EndHoldSeconds);
        Assert.Equal(0.0, shots[0].Camera!.Route!.EndHoldSeconds);
        Assert.Equal(TourBuilder.EndHoldSeconds, shots[7].Camera!.Route!.EndHoldSeconds);
    }

    [Fact]
    public void AShotThatStartsWhereTheLastEnded_IsACut_AndACutBackToAJunction_IsAShortDip()
    {
        var shots = Build().Storyboard.Shots;
        // Into a side passage from the junction the camera stands at: a cut.
        Assert.All(new[] { shots[0], shots[1], shots[3], shots[6] }, s => Assert.Null(s.In));
        // Back from a dead end, or from the far end of a bypass: a dip.
        Assert.All(new[] { shots[2], shots[4], shots[5], shots[7] }, s =>
        {
            Assert.Equal(TransitionKind.Dip, s.In!.Kind);
            Assert.Equal(TourBuilder.DipSeconds, s.In.Seconds);
        });
    }

    [Fact]
    public void TheStoryboard_LowersToOnePlanAShot_AsLongAsTheTableSays()
    {
        var tour = Build(new TourSpec { Fps = 10 });
        var film = StoryboardPlanner.Lower(
            tour.Storyboard with { Output = tour.Storyboard.Output with { OutputDirectory = "out" } },
            new SourceSpec { PlyPath = "model.ply" }, CameraFraming.FromGeometry(Cave.Value), SceneMeta.Build(Cave.Value));

        Assert.Equal(tour.Storyboard.Shots.Count, film.Segments.Count);
        int frames = film.Segments.Sum(s => s.Timeline.FrameCount);
        Assert.Equal((int)Math.Round(tour.Seconds * 10), frames);
        Assert.Equal(tour.Seconds, tour.Storyboard.Shots.Sum(s => s.Seconds!.Value), 2);
        Assert.Equal(tour.Seconds, tour.Rows.Sum(r => r.Seconds), 1);
        Assert.True(tour.Seconds <= 180.0);
        Assert.Equal("{cave}: tour", tour.Storyboard.Name);
        Assert.Equal("cave-tour", tour.Storyboard.Output.BaseName);

        // The lead at the end of B1 is in its plan: a marker and its text, where the station is.
        var north = film.Segments[1];
        var n6 = Cave.Value.RecenteredModel.Stations.Single(s => s.Name == "n6").Position;
        Assert.Equal(2, north.Annotations!.Count);
        Assert.All(north.Annotations, a => Assert.True((a.At - n6).Length < 1e-3));
        Assert.Equal("Draughting crawl", north.Annotations.Single(a => a.Text is not null).Text);
        Assert.Null(film.Segments[0].Annotations);
    }

    // ---- texts and leads ----

    [Fact]
    public void ASidePassage_HasItsNameAndLengthInTheCorner_AndItsLeadIsMarkedWhileTheCameraIsNear()
    {
        var tour = Build();
        Assert.Equal("Side passage B1 · 60 m", Shot(tour, "B1").Texts!.Corner);
        Assert.Equal("Bypass B4 · 90 m", Shot(tour, "B4").Texts!.Corner);
        Assert.Null(Shot(tour, "Main passage 1").Texts);
        // Nothing of the cave's own labels in a tour's shots.
        Assert.All(tour.Storyboard.Shots, s => Assert.False(s.Labels!.Leads.Show));

        var marks = Shot(tour, "B1").Annotations!;
        Assert.Equal([AnnotationKind.Marker, AnnotationKind.Callout], marks.Select(a => a.Kind));
        // In the model's own coordinates, as an annotation's point is.
        var n6 = TourCave.Build().Stations.Single(s => s.Name == "n6").Position;
        Assert.All(marks, a => Assert.True((a.At!.Value - n6).Length < 1e-3));
        Assert.All(marks, a => Assert.True(a.Within > 0));
        Assert.Equal("Draughting crawl", marks[1].Text);
        Assert.Equal("Lead", Shot(tour, "B5").Annotations![1].Text);   // no note: the word
        Assert.Null(Shot(tour, "B3").Annotations);
    }

    [Fact]
    public void TheTexts_AreInTheLanguageAsked()
    {
        var tour = Build(new TourSpec { Language = "ro" });
        Assert.Equal("Galerie laterală B1 · 60 m", Shot(tour, "B1").Texts!.Corner);
        Assert.Equal("Ocolire B4 · 90 m", Shot(tour, "B4").Texts!.Corner);
        Assert.Equal("continuă încă 13 m", Shot(tour, "B3").Texts!.CornerCaptions.Single().Text);
        Assert.Equal("Pistă", Shot(tour, "B5").Annotations![1].Text);
        Assert.Equal("{cave}: tur", tour.Storyboard.Name);
        Assert.StartsWith("Galeria principală", tour.Storyboard.Shots[0].Name);
    }

    [Fact]
    public void LeadsGiven_ReplaceTheModelsFlags()
    {
        var tour = Build(leads: [new SourceLead("q1", "question mark", "Too tight")]);
        Assert.Equal(1, Row(tour, "B4").Leads);
        Assert.Equal(0, Row(tour, "B1").Leads);
        Assert.Equal("short", Row(tour, "B5").Why);   // its flag no longer counts
        // q1 is off the bypass's way at depth 1: counted, not flown past.
        Assert.Null(Shot(tour, "B4").Annotations);

        var deeper = Build(new TourSpec { Depth = 2, MinLength = 5 }, [new SourceLead("q1", "question mark", "Too tight")]);
        Assert.Equal("Too tight", Shot(deeper, "B4.1").Annotations![1].Text);
    }

    // ---- the table ----

    [Fact]
    public void TheTable_HasTheMainPassage_AndEverySideSystemWithWhatTheFilmDoesAboutIt()
    {
        var tour = Build();
        Assert.Equal(["trunk", "B1", "B2", "B3", "B4", "B5", "B6"], tour.Rows.Select(r => r.Id));

        var trunk = Row(tour, "trunk");
        Assert.Equal(("t0", "t20", 200.0, "trunk", 1.0), (trunk.Junction, trunk.End, trunk.Length, trunk.Mode, trunk.Speed));

        var north = Row(tour, "B1");
        Assert.Equal(("branch", "t3", "n6", 60.0, 60.0, 2.5, 1, 2), (north.Kind, north.Junction, north.End, north.Length, north.Reach!.Value, north.MeanWidth!.Value, north.Leads, north.Rank!.Value));
        Assert.Equal((true, "byRank", "cut"), (north.Chosen, north.Why, north.Mode));
        Assert.Equal(["north"], north.Surveys);

        Assert.Equal(("bypass", "t9", "t13", "through"), (Row(tour, "B4").Kind, Row(tour, "B4").Junction, Row(tour, "B4").End, Row(tour, "B4").Mode));
        Assert.Equal((true, "lead", "cut"), (Row(tour, "B5").Chosen, Row(tour, "B5").Why, Row(tour, "B5").Mode));
        Assert.Equal((false, "short", "skip", 0.0), (Row(tour, "B2").Chosen, Row(tour, "B2").Why, Row(tour, "B2").Mode, Row(tour, "B2").Seconds));
        Assert.Equal(-25.0, Row(tour, "B3").DepthChange);

        string[] lines = tour.TableText().Split('\n');
        Assert.Equal(8, lines.Length);
        Assert.StartsWith("Branch", lines[0]);
        Assert.Matches(@"^B4\s+t9 → t13\s+90 m\s+bypass\s", lines[5]);
        Assert.Matches(@"^B2\s+t6\s+8 m\s.*no \(short\)\s+skip", lines[3]);
        Assert.Contains("- The tour stays in the part of the cave it starts in: 1 other part(s), 15 m", tour.ReadmeText());
    }

    // ---- a side passage's own budget ----

    [Fact]
    public void ASidePassageOverItsBudget_IsFlownFaster_UpToTwice()
    {
        var unhurried = Build(new TourSpec { BranchSeconds = 60 });
        Assert.All(unhurried.Rows, r => Assert.Equal(1.0, r.Speed));
        Assert.All(unhurried.Rows, r => Assert.Equal(0.0, r.ContinuesMeters));
        double natural = Row(unhurried, "B1").Seconds;
        Assert.InRange(natural, 25.0, 35.0);   // 60 m at about 2.25 m/s, and the hold

        var tour = Build();
        var north = Row(tour, "B1");
        Assert.InRange(north.Seconds, 19.9, 20.0);
        Assert.InRange(north.Speed, 1.3, 1.6);
        Assert.Equal(0.0, north.ContinuesMeters);
        Assert.Equal(1.0, Row(tour, "B5").Speed);   // 30 m: within its 20 s as it is
        Assert.Equal(Row(unhurried, "B5").Seconds, Row(tour, "B5").Seconds);
    }

    [Fact]
    public void ASidePassageStillOverItsBudget_IsFlownAsFarAsTheTimeAllows_AndSaysHowFarItGoesOn()
    {
        var tour = Build();
        var south = Row(tour, "B3");   // 65 m of narrow passage: 1.5 m/s
        Assert.Equal(2.0, south.Speed);
        Assert.True(south.Seconds <= 20.0);
        Assert.Equal((52.0, 13.0), (south.FlownMeters, south.ContinuesMeters));
        Assert.Equal("cut", south.Mode);

        var shot = Shot(tour, "B3");
        Assert.Equal(["t9", "d1", "d2", "d3", "d4"], Way(shot));
        var says = shot.Texts!.CornerCaptions.Single();
        Assert.Equal("continues 13 m", says.Text);
        Assert.Equal(shot.Seconds!.Value, says.End, 3);
        Assert.Equal(shot.Seconds.Value - TourBuilder.EndHoldSeconds, says.Start, 3);
        Assert.Contains(tour.Notices, n => n.StartsWith("B3 is flown for 52 m of its way of 65 m, to keep within 20 s: it continues 13 m."));

        // A branch whose first leg alone is too long is flown that far all the same, and says so.
        var tight = Build(new TourSpec { BranchSeconds = 2, TourSeconds = 600 });
        Assert.Equal(13.0, Row(tight, "B3").FlownMeters);
        Assert.Contains(tight.Notices, n => n.StartsWith("B3 takes more than 2 s"));
    }

    // ---- the tour's budget, rule by rule ----

    [Fact]
    public void ATourOverItsBudget_IsFirstFlownFaster_AndNothingIsLeftOut()
    {
        var easy = Build();
        var tour = Build(new TourSpec { TourSeconds = 120 });
        Assert.True(easy.Seconds > 120);
        Assert.InRange(tour.Seconds, 119.5, 120.0);
        Assert.InRange(Row(tour, "trunk").Speed, 1.05, 1.95);
        Assert.Equal(Row(tour, "trunk").Speed, Row(tour, "B5").Speed);
        Assert.True(Row(tour, "B1").Speed > Row(tour, "trunk").Speed);   // its own budget asks for more
        Assert.Equal(easy.Storyboard.Shots.Select(s => s.Name), tour.Storyboard.Shots.Select(s => s.Name));
        Assert.Contains(tour.Notices, n => n.StartsWith("The tour is flown 1.") && n.EndsWith("to keep within 120 s."));
        Assert.DoesNotContain(easy.Notices, n => n.Contains("faster"));
    }

    private sealed record State(double Budget, Tour Tour)
    {
        public List<TourRow> Sides => Tour.Rows.Where(r => r.Chosen && r.Kind != "trunk").ToList();
        public List<TourRow> Returns => Sides.Where(r => r.Mode == "return").ToList();
        public List<TourRow> Kept => Sides.Where(r => r.Mode != "skip").ToList();
        public List<TourRow> Dropped => Sides.Where(r => r.Dropped == "budget").ToList();
        public bool TrunkCutShort => Tour.Rows[0].ContinuesMeters > 0;
    }

    /// <summary>The same tour under ever less time: every budget from 260 s down to 6 s.</summary>
    private static List<State> Sweep(TourSpec spec)
        => Enumerable.Range(0, 128).Select(i => 260.0 - 2 * i).Select(budget => new State(budget, Build(spec with { TourSeconds = budget }))).ToList();

    [Fact]
    public void UnderEverLessTime_FlightsBackBecomeCuts_ThenTheLowestRankedAreLeftOut_AndTheMainPassageIsCutShortLast()
    {
        // Every chosen side passage flown in and back out, each with time enough of its own.
        var states = Sweep(new TourSpec { Mode = TourBranchMode.Return, BranchSeconds = 90 });
        Assert.Equal(["B1", "B3", "B5"], states[0].Returns.Select(r => r.Id));   // the bypass is flown through
        Assert.Equal(4, states[0].Kept.Count);

        foreach (var state in states)
        {
            string at = $"at {state.Budget} s";
            // Within the time, until nothing is left to give.
            if (!state.Tour.Notices.Any(n => n.StartsWith("The tour takes"))) Assert.True(state.Tour.Seconds <= state.Budget + 1e-6, at);
            // A flight back is given up only when twice the speed is not enough …
            if (state.Returns.Count < 3) Assert.True(state.Tour.Rows[0].Speed > 1.0, at);
            // … a side passage is left out only when no flight back is left …
            if (state.Dropped.Count > 0) Assert.True(state.Returns.Count == 0, at);
            // … and the main passage is cut short only when no side passage is.
            if (state.TrunkCutShort) Assert.True(state.Kept.Count == 0, at);
            // The lowest-ranked go first: flights back, and side passages.
            var cuts = state.Kept.Where(r => r.Mode == "cut").ToList();
            if (state.Returns.Count > 0 && cuts.Count > 0) Assert.True(state.Returns.Max(r => r.Rank) < cuts.Min(r => r.Rank), at);
            if (state.Dropped.Count > 0 && state.Kept.Count > 0)
                Assert.True(state.Kept.Max(r => r.Rank) < state.Dropped.Min(r => r.Rank), at);
            Assert.Equal(state.Dropped.Count, state.Tour.Notices.Count(n => n.Contains("is left out, to keep the tour within")));
        }
        // Less time never brings anything back.
        for (int i = 1; i < states.Count; i++)
        {
            Assert.True(states[i].Returns.Count <= states[i - 1].Returns.Count);
            Assert.True(states[i].Kept.Count <= states[i - 1].Kept.Count);
        }
        // Every rule had its turn.
        Assert.Contains(states, s => s.Returns.Count is 1 or 2 && s.Dropped.Count == 0);
        Assert.Contains(states, s => s.Returns.Count == 0 && s.Kept.Count == 4);
        Assert.Contains(states, s => s.Kept.Count is 1 or 2 or 3 && !s.TrunkCutShort);
        Assert.Contains(states, s => s.Kept.Count == 0 && !s.TrunkCutShort);
        Assert.Contains(states, s => s.TrunkCutShort);

        var turned = states.First(s => s.Returns.Count < 3).Tour;
        Assert.Contains(turned.Notices, n => n.StartsWith("B5 is left by a cut instead of a flight back, to keep the tour within"));
        var cut = states.First(s => s.TrunkCutShort).Tour;
        Assert.Contains(cut.Notices, n => n.StartsWith("The main passage is flown for") && n.Contains("it continues"));
        Assert.StartsWith("continues", cut.Storyboard.Shots.Single().Texts!.CornerCaptions.Single().Text);
        Assert.Single(states[^1].Tour.Storyboard.Shots);
    }

    [Fact]
    public void ASidePassageChosenByHand_IsTheLastToBeLeftOut()
    {
        // B5 is the lowest ranked of the four; chosen by hand it outlasts the others.
        var states = Sweep(new TourSpec { Include = ["B5"] });
        Assert.Equal("included", Row(states[0].Tour, "B5").Why);
        var partly = states.Where(s => s.Kept.Count is > 0 and < 4).ToList();
        Assert.NotEmpty(partly);
        Assert.All(partly, s => Assert.Contains(s.Kept, r => r.Id == "B5"));
    }

    // ---- modes ----

    [Fact]
    public void AFlightBack_IsOneShot_InAtThePassagesSpeedAndOutAtTwice()
    {
        var spec = new TourSpec { BranchSeconds = 90, Modes = new Dictionary<string, TourBranchMode> { ["B5"] = TourBranchMode.Return } };
        var tour = Build(spec);
        var route = Shot(tour, "B5").Camera!.Route!;
        Assert.Equal(("t16", "x3", "t16"), (route.From, route.Via.Single(), route.To));
        Assert.Equal([1.0, TourBuilder.ReturnSpeed], route.Speeds);
        Assert.Equal(TourBuilder.EndHoldSeconds, route.HoldSeconds);
        Assert.Equal("return", Row(tour, "B5").Mode);
        // It comes back to the junction: the main passage goes on with a cut.
        Assert.Null(Shot(tour, "Main passage 4").In);

        double cut = Row(Build(new TourSpec { BranchSeconds = 90 }), "B5").Seconds;
        Assert.InRange(Row(tour, "B5").Seconds / cut, 1.3, 1.9);
    }

    [Fact]
    public void AFlightBackOverTheSidePassagesBudget_BecomesACut_AndABypassIsAlwaysFlownThrough()
    {
        var tour = Build(new TourSpec { Modes = new Dictionary<string, TourBranchMode> { ["B1"] = TourBranchMode.Return, ["B4"] = TourBranchMode.Return, ["B9"] = TourBranchMode.Cut } });
        Assert.Equal("cut", Row(tour, "B1").Mode);
        Assert.Equal(0.0, Row(tour, "B1").ContinuesMeters);
        Assert.Contains("B1 is left by a cut instead of a flight back, to keep within 20 s.", tour.Notices);
        Assert.Equal("through", Row(tour, "B4").Mode);
        Assert.Contains(tour.Notices, n => n.StartsWith("B4 is a bypass"));
        Assert.Contains("'B9' (modes) names no side passage of this tour.", tour.Notices);
    }

    [Fact]
    public void APeek_LooksIntoTheSidePassageFromItsJunction_AndASkipLeavesItOut()
    {
        var tour = Build(new TourSpec
        {
            PeekShort = true,
            Modes = new Dictionary<string, TourBranchMode> { ["B1"] = TourBranchMode.Peek, ["B3"] = TourBranchMode.Skip, ["B2"] = TourBranchMode.Cut },
        });
        Assert.Equal(
            ["Main passage 1", "B1: a look", "Main passage 2", "B2", "Main passage 3", "B4", "Main passage 4", "B5", "Main passage 5", "B6: a look", "Main passage 6"],
            tour.Storyboard.Shots.Select(s => s.Name));
        Assert.Equal(("peek", "excluded"), (Row(tour, "B1").Mode, Row(tour, "B1").Why));
        Assert.Equal(("skip", "excluded"), (Row(tour, "B3").Mode, Row(tour, "B3").Why));
        Assert.Equal(("cut", "included"), (Row(tour, "B2").Mode, Row(tour, "B2").Why));   // short, but asked for
        Assert.Equal(("peek", "short"), (Row(tour, "B6").Mode, Row(tour, "B6").Why));

        var look = Shot(tour, "B1: a look");
        Assert.Equal(TourBuilder.PeekSeconds, look.Seconds);
        Assert.Equal(CameraTemplate.Viewpoints, look.Camera!.Template);
        Assert.Equal("Side passage B1 · 60 m", look.Texts!.Corner);
        var (first, second) = (look.Camera.Viewpoints!.Viewpoints[0], look.Camera.Viewpoints.Viewpoints[1]);
        var t3 = Cave.Value.RecenteredModel.Stations.Single(s => s.Name == "t3").Position;
        // At the junction, looking north into the passage, and moving a little that way.
        Assert.True((first.Position - t3).Length < 2.5);
        Assert.True(first.Target.Y - first.Position.Y > 4.0);
        Assert.True(second.Position.Y > first.Position.Y);
        // The camera never left the junction: cuts on both sides.
        Assert.Null(look.In);
        Assert.Null(Shot(tour, "Main passage 2").In);
        Assert.Equal(TourBuilder.PeekSeconds, Row(tour, "B1").Seconds);

        // Skip for every side passage: the main passage in one shot.
        Assert.Single(Build(new TourSpec { Mode = TourBranchMode.Skip }).Storyboard.Shots);
    }

    [Fact]
    public void AtDepth2_ABranchIsFlownInStretches_WithItsOwnSidePassagesVisitedOnTheWay()
    {
        var tour = Build(new TourSpec { Depth = 2, MinLength = 5, Fps = 8 });
        var names = tour.Storyboard.Shots.Select(s => s.Name).ToList();
        int at = names.IndexOf("B4 (1)");
        Assert.Equal(["B4 (1)", "B4.1", "B4 (2)"], names.Skip(at).Take(3));
        Assert.Equal(["t9", "p1", "p2"], Way(Shot(tour, "B4 (1)")));
        Assert.Equal(["p2", "q1"], Way(Shot(tour, "B4.1")));
        Assert.Equal(["p2", "p3", "t13"], Way(Shot(tour, "B4 (2)")));
        Assert.Null(Shot(tour, "B4.1").In);
        Assert.Equal(TransitionKind.Dip, Shot(tour, "B4 (2)").In!.Kind);
        Assert.Equal("Side passage B4.1 · 10 m", Shot(tour, "B4.1").Texts!.Corner);
        Assert.Equal(2, Row(tour, "B4.1").Depth);
        // At 8 frames a second a dip is still two frames at least.
        Assert.True(Shot(tour, "B4 (2)").In!.Seconds * 8 >= 2);
    }

    // ---- what is refused, and the tour file ----

    [Fact]
    public void ATourThatCannotBeMade_IsRefusedByTheNameOfItsOption()
    {
        static string Refused(TourSpec spec) => Assert.Throws<ArgumentException>(() => TourBuilder.Build(Cave.Value, spec)).ParamName!;
        Assert.Equal("tour.tourSeconds", Refused(new TourSpec { TourSeconds = 0 }));
        Assert.Equal("tour.branchSeconds", Refused(new TourSpec { BranchSeconds = double.NaN }));
        Assert.Equal("tour.fps", Refused(new TourSpec { Fps = 0 }));
        Assert.Equal("tour.language", Refused(new TourSpec { Language = "de" }));
        Assert.Equal("tour.preset", Refused(new TourSpec { Preset = "No such preset" }));
        Assert.Equal("tour.start", Refused(new TourSpec { Start = "nowhere" }));
        Assert.Equal("tour.trunk", Refused(new TourSpec { Trunk = TourTrunkKind.Through, Start = "y2", End = "y2" }));

        var tour = Build(new TourSpec { Include = ["nothing"] });
        Assert.Contains("'nothing' (include or exclude) names no side passage of this tour.", tour.Notices);
    }

    [Fact]
    public void ATourFile_IsReadAndWritten_AndAKeyThatIsNotKnownIsRefused()
    {
        var spec = new TourSpec
        {
            Trunk = TourTrunkKind.Through, Start = "t0", End = "y2", MinLength = 30, MaxCount = 5, Include = ["B2"], Mode = TourBranchMode.Return,
            Modes = new Dictionary<string, TourBranchMode> { ["B5"] = TourBranchMode.Peek }, TourSeconds = 90, BranchSeconds = 15, Language = "ro",
            Output = new OutputSpec { Width = 640, Height = 360, BaseName = "tur" },
        };
        string json = TourSpecSerializer.Write(spec);
        var read = TourSpecSerializer.Read(json);
        Assert.Equal(json, TourSpecSerializer.Write(read));
        Assert.Equal((TourTrunkKind.Through, "y2", 30.0, TourBranchMode.Peek, "tur"), (read.Trunk, read.End, read.MinLength, read.Modes["B5"], read.Output!.BaseName));

        var words = TourSpecSerializer.Read("""{ "trunk": "DEEPEST", "mode": "return", "modes": { "B1": "skip" }, "tourSeconds": 60, /* a comment */ }""");
        Assert.Equal((TourTrunkKind.Deepest, TourBranchMode.Return, TourBranchMode.Skip, 60.0), (words.Trunk, words.Mode, words.Modes["B1"], words.TourSeconds));
        Assert.Equal(20.0, words.BranchSeconds);

        Assert.Contains("budget", Assert.Throws<TourFormatException>(() => TourSpecSerializer.Read("""{ "budget": 60 }""")).Message);
        // What is not a tour is refused without a word of it.
        var not = Assert.Throws<TourFormatException>(() => TourSpecSerializer.Read("station,x,y\nsecret-place,1,2"));
        Assert.DoesNotContain("secret", not.Message);
        Assert.Throws<TourFormatException>(() => TourSpecSerializer.Read("[1, 2]"));
    }

    // ---- the route camera's part ----

    [Fact]
    public void ARoute_FliesAStretchAsManyTimesFasterAsItsSpeedsSay()
    {
        var framing = CameraFraming.FromGeometry(Cave.Value);
        var there = new RouteParams { From = "t3", Via = ["n6"], To = "t3", HoldSeconds = 1.5 };
        var even = CameraRoutes.Measure(framing, there);
        var faster = CameraRoutes.Measure(framing, there with { Speeds = [1.0, 2.0] });
        Assert.Equal(even.LengthMeters, faster.LengthMeters, 6);
        Assert.Equal(even.HoldSeconds + even.TurnSeconds, faster.HoldSeconds + faster.TurnSeconds, 6);
        // In as before, out in about half the time.
        Assert.InRange(faster.TravelSeconds / even.TravelSeconds, 0.72, 0.80);

        var spec = SceneSpecTests.ValidSpec() with { Camera = new CameraSpec { Template = CameraTemplate.Route, Route = there with { Speeds = [1.0, 0.0] } } };
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == "camera.route.speeds[1]");
        // A route that says nothing of speeds is the JSON it was.
        Assert.DoesNotContain("speeds", SceneSpecSerializer.Write(spec with { Camera = spec.Camera with { Route = there } }));
    }
}
