// The player refuses a key it does not know, in the core's sections and in every module's, and
// knows the keys the schema has: no more, no fewer. These run the player's Python as it is
// shipped, on the system Python, with empty stand-ins for Blender's own modules (nothing here
// reaches them). In Blender, the refusal is in PlayerBlenderTests.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Therion.Blender.Plan;
using Therion.Blender.Player;

namespace Therion.Blender.Tests.Player;

public class PlayerKnownKeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-player-keys-{Guid.NewGuid():N}");

    public PlayerKnownKeysTests()
    {
        Directory.CreateDirectory(_dir);
        foreach (var file in PlayerFiles.JobFiles)
        {
            string path = Path.Combine(_dir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Text, new UTF8Encoding(false));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Loads the core and every module of the player without Blender, then runs
    /// <paramref name="body"/>, which has them as <c>core</c> and <c>modules</c>. Null when there
    /// is no Python on the PATH: the tests are then a documented no-op, as for the generated
    /// scripts.</summary>
    private string? RunPython(string body, params string[] arguments)
    {
        string? python = PlayerWriterTests.FindPython();
        if (python is null) return null;
        string script = Path.Combine(_dir, "check.py");
        File.WriteAllText(script, $$"""
            import importlib, json, sys, types

            sys.dont_write_bytecode = True
            sys.path.insert(0, {{JsonSerializer.Serialize(_dir)}})
            for name in ("bpy", "bmesh", "mathutils"):
                sys.modules[name] = types.ModuleType(name)
            from thide_player import api, core
            names = {{JsonSerializer.Serialize(ScenePlan.PlayerFeatures.Append(MoviePlan.SegmentsFeature))}}
            modules = [importlib.import_module("thide_player.modules." + name) for name in names]

            """.Replace("            ", "") + body.Replace("            ", ""), new UTF8Encoding(false));
        var (exitCode, output) = PlayerWriterTests.Run(python, [script, .. arguments]);
        Assert.True(exitCode == 0, output);
        return output.ReplaceLineEndings("\n");
    }

    [Fact]
    public void ThePlayersKeys_AreTheSchemas_SectionBySection()
    {
        string? output = RunPython("""
            def plain(keys):
                if isinstance(keys, list):
                    return plain(keys[0])
                if isinstance(keys, dict):
                    return {key: plain(value) for key, value in keys.items()}
                return None

            known = {"frame": core.FRAME_KEYS, "timeline": core.TIMELINE_KEYS}
            for module in modules:
                known[module.SECTION] = module.KEYS
            print(json.dumps({section: plain(keys) for section, keys in known.items()}, sort_keys=True))
            """);
        if (output is null) return;

        var player = JsonNode.Parse(output)!.AsObject();
        // Every section of the schema that has keys is the core's or a module's.
        var schema = ScenePlanSchema.Keys.Keys.Where(s => s.Value is not null).ToDictionary(s => s.Key, s => s.Value!);
        Assert.Equal(schema.Keys.Order(StringComparer.Ordinal), player.Select(p => p.Key).Order(StringComparer.Ordinal));
        foreach (var (section, keys) in schema)
            Assert.Equal(Plain(keys)!.ToJsonString(), Sorted(player[section])!.ToJsonString());
    }

    private static JsonNode? Plain(PlanKeys? keys)
    {
        if (keys is null) return null;
        var node = new JsonObject();
        foreach (var (key, inner) in keys.Keys.OrderBy(k => k.Key, StringComparer.Ordinal)) node[key] = Plain(inner);
        return node;
    }

    private static JsonNode? Sorted(JsonNode? node)
    {
        if (node is not JsonObject obj) return node?.DeepClone();
        var sorted = new JsonObject();
        foreach (var (key, inner) in obj.OrderBy(k => k.Key, StringComparer.Ordinal)) sorted[key] = Sorted(inner);
        return sorted;
    }

    /// <summary>Where a key the player does not know is put, and the path it is named by.</summary>
    public static TheoryData<string, string> UnknownKeys => new()
    {
        { "frame", "frame.tint" },
        { "timeline", "timeline.tint" },
        { "models/1", "models[1].tint" },
        { "models/1/rotation", "models[1].rotation.tint" },
        { "lines/0", "lines[0].tint" },
        { "lines/0/rotation", "lines[0].rotation.tint" },
        { "lines/0/strips/1", "lines[0].strips[1].tint" },
        { "camera", "camera.tint" },
        { "camera/track/2", "camera.track[2].tint" },
        { "look", "look.tint" },
        { "look/materials/0/stops/1", "look.materials[0].stops[1].tint" },
        { "look/lights/1", "look.lights[1].tint" },
        { "look/world", "look.world.tint" },
        { "texturedMaterials/0", "texturedMaterials[0].tint" },
        { "labels", "labels.tint" },
        { "labels/texts", "labels.texts.tint" },
        { "labels/texts/items/0", "labels.texts.items[0].tint" },
        { "labels/markers/pulse", "labels.markers.pulse.tint" },
        { "labels/markers/byDistance", "labels.markers.byDistance.tint" },
        { "labels/markers/items/1", "labels.markers.items[1].tint" },
        { "annotations/0", "annotations[0].tint" },
        { "annotations/3/frame", "annotations[3].frame.tint" },
        { "changes/0/target", "changes[0].target.tint" },
        { "changes/0/visible/1", "changes[0].visible[1].tint" },
        { "changes/1/opacity/0", "changes[1].opacity[0].tint" },
        { "changes/1/tint/1", "changes[1].tint[1].tint" },
        { "changes/1/scale/1", "changes[1].scale[1].tint" },
        { "changes/2/worldColor/0", "changes[2].worldColor[0].tint" },
        { "changes/3/energy/1", "changes[3].energy[1].tint" },
        { "changes/5/offset/0", "changes[5].offset[0].tint" },
        { "changes/6/worldStrength/0", "changes[6].worldStrength[0].tint" },
        { "texts", "texts.tint" },
        { "texts/items/1", "texts.items[1].tint" },
        { "texts/items/3/turn/2", "texts.items[3].turn[2].tint" },
        { "blend", "blend.tint" },
        { "output", "output.tint" },
    };

    [Theory]
    [MemberData(nameof(UnknownKeys))]
    public void AKeyThePlayerDoesNotKnow_IsRefused_ByItsPath_AndThePlayersVersion(string where, string path)
    {
        string plan = Path.Combine(_dir, "plan.json");
        File.WriteAllText(plan, ScenePlanSerializer.Write(PlanFixtures.Full()));

        string? output = RunPython("""
            with open(sys.argv[1], encoding="utf-8") as stream:
                plan = json.load(stream)
            core.check(plan)
            core.check_sections(plan, modules)
            print("as written | played")
            target = plan
            for step in sys.argv[2].split("/"):
                target = target[int(step)] if isinstance(target, list) else target[step]
            target["tint"] = [1, 0, 0]
            try:
                core.check_sections(plan, modules)
                print("with tint | played")
            except api.PlanError as error:
                print("with tint |", error)
            """, plan, where);
        if (output is null) return;

        Assert.Equal(
            $"as written | played\nwith tint | {path} is not known to player {PlayerFiles.Version}; a newer player may be needed\n",
            output);
    }

    /// <summary>
    /// The curve the player keys for a track, asked for its value at whole frames the way Blender
    /// works a curve out: a smooth move that a jump cuts short stays on its way to the value it
    /// never reaches, a colour mixed as a screen mixes has a key a frame, and two first keys on
    /// one frame are a value before and a value from it on. In Blender, with its own curves, this
    /// is in PlayerChangesBlenderTests.
    /// </summary>
    [Fact]
    public void ATracksCurve_GoesThroughAJump_AndMixesAColourAsAScreenDoes()
    {
        string? output = RunPython("""
            changes = modules[names.index("changes")]

            def at(points, frame, last=100):
                return changes.value_at(changes.curve_of(points, last), frame)

            def frames(points, last=100):
                return [frame for frame, _ in changes.curve_of(points, last)[0]]

            rows = {}
            cut = [(1, 0.0, "linear", "linear"), (11, 2.0, "smooth", "linear"), (11, 5.0, "constant", "linear"),
                   (21, 1.0, "linear", "linear")]
            rows["cut"] = [at(cut, frame) for frame in (1, 6, 10, 11, 16, 21, 30)]
            rows["cutKeys"] = frames(cut)
            whole = [(1, 0.0, "linear", "linear"), (11, 2.0, "smooth", "linear")]
            rows["whole"] = [at(whole, frame) for frame in (1, 6, 10, 11)]
            red, green = [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]
            screen = [(1, red, "linear", "linear"), (5, green, "linear", "srgb")]
            rows["screen"] = at(screen, 3)
            rows["screenKeys"] = frames(screen)
            rows["light"] = at([(1, red, "linear", "linear"), (5, green, "linear", "linear")], 3)
            rows["farKeys"] = frames([(1, red, "linear", "linear"), (1001, green, "linear", "srgb")], 3)
            first = [(5, 1.0, "linear", "linear"), (5, 2.0, "constant", "linear")]
            rows["first"] = [at(first, frame) for frame in (1, 4, 5, 9)]
            rows["firstKeys"] = frames(first)
            held = [(1, 1.0, "linear", "linear"), (6, 3.0, "constant", "linear"), (6, 4.0, "constant", "linear")]
            rows["held"] = [at(held, frame) for frame in (1, 5, 6)]
            print(json.dumps(rows))
            """);
        if (output is null) return;

        var rows = JsonNode.Parse(output)!.AsObject();
        double[] Numbers(string key) => [.. rows[key]!.AsArray().Select(n => n!.GetValue<double>())];
        static double Smooth(double x) => x * x * (3 - 2 * x);
        void Same(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 12);
        }

        // On the way to 2 until the frame before the jump, 5 on its frame, then evenly down to 1.
        Same([0, 2 * Smooth(0.5), 2 * Smooth(0.9), 5, 3, 1, 1], Numbers("cut"));
        Same([1, 10, 11, 21], Numbers("cutKeys"));
        // The same move left whole has the same values where both are there.
        Same([0, 2 * Smooth(0.5), 2 * Smooth(0.9), 2], Numbers("whole"));
        // Half-way from red to green: the dull yellow of a screen, and the bright one of two lights.
        double half = Math.Pow((0.5 + 0.055) / 1.055, 2.4);
        Same([half, half, 0], Numbers("screen"));
        Same([1, 2, 3, 4, 5], Numbers("screenKeys"));
        Same([0.5, 0.5, 0], Numbers("light"));
        // A key far after the clip's last frame does not get a key for every frame up to it.
        Same([1, 2, 3, 1001], Numbers("farKeys"));
        // Two first keys on one frame: the first until then, the second from it on.
        Same([1, 1, 2, 2], Numbers("first"));
        Same([4, 5], Numbers("firstKeys"));
        // A jump that ends a hold shows nothing of the value it replaces.
        Same([1, 1, 4], Numbers("held"));
    }

    /// <summary>
    /// The plan of a movie of several scenes, as a storyboard is lowered to it: the player's own
    /// checks take it, put every segment on the frame the library puts it on, and refuse a key
    /// they do not know, a segment out of its place and a transition longer than what it joins.
    /// In Blender, the movie itself is in PlayerMovieBlenderTests.
    /// </summary>
    [Fact]
    public void TheMoviesPlan_IsCheckedByThePlayer_AsTheLibraryChecksIt()
    {
        var movie = StoryboardPlanner.Lower(
            new Storyboard
            {
                Fps = 10,
                Shots =
                [
                    new ShotSpec { Name = "Outside", Seconds = 2 },
                    new ShotSpec { Seconds = 3, In = new TransitionSpec { Kind = TransitionKind.Dissolve, Seconds = 0.5 } },
                    new ShotSpec { Seconds = 2, In = new TransitionSpec { Kind = TransitionKind.Dip, Seconds = 0.7, Color = new ColorRgb(1, 0, 0) } },
                ],
                Texts = new OverlaySpec { Title = "Invented Cave" },
                Output = new OutputSpec { Width = 320, Height = 180, BaseName = "film" },
            },
            new SourceSpec { PlyPath = "/job/model.ply" }, PlanFixtures.MediumFraming, PlanFixtures.MediumMeta).Movie;
        string plan = Path.Combine(_dir, "movie.json");
        File.WriteAllText(plan, ScenePlanSerializer.Write(movie));

        string? output = RunPython("""
            segments = modules[names.index("segments")]
            with open(sys.argv[1], encoding="utf-8") as stream:
                plan = json.load(stream)
            core.check(plan)
            core.check_sections(plan, [segments, modules[names.index("texts")], modules[names.index("output")]])
            ctx = api.Context(plan, "/job", None)
            parts = segments.check(ctx, plan["segments"])
            print(json.dumps([[p["first"], p["count"], p["kind"], p["over"], p["out"], p["into"], p["name"]] for p in parts]))
            print(json.dumps(parts[2]["colour"]), parts[1]["plan"].replace("\\", "/"))

            def refused(change):
                changed = json.loads(json.dumps(plan))
                change(changed["segments"])
                try:
                    core.check_sections(changed, [segments])
                    segments.check(ctx, changed["segments"])
                    print("played")
                except api.PlanError as error:
                    print(error)

            refused(lambda s: s[1].update(tint=1))
            refused(lambda s: s[1]["in"].update(curve=1))
            refused(lambda s: s[1].update(start=2))
            refused(lambda s: s[2]["in"].update(seconds=9))
            refused(lambda s: s[0].update({"in": {"kind": "dip", "seconds": 1}}))
            refused(lambda s: s[2].update(plan="../other.json"))
            refused(lambda s: s[2].update(frames="s01"))
            refused(lambda s: s.pop())
            """, plan);
        if (output is null) return;

        string[] lines = output.TrimEnd('\n').Split('\n');
        // Frames 1–20, 16–45 over a dissolve of five, 46–65 after a dip of three frames and four.
        Assert.Equal(
            "[[1, 20, \"cut\", 0, 0, 0, \"Outside\"], [16, 30, \"dissolve\", 5, 0, 0, \"s02\"], [46, 20, \"dip\", 0, 3, 4, \"s03\"]]", lines[0]);
        Assert.Equal(movie.Placed().Select(p => p.First), new[] { 1, 16, 46 });
        Assert.EndsWith("[1.0, 0.0, 0.0] /job/scene-plan.s02.json", lines[1]);
        Assert.Equal($"segments[1].tint is not known to player {PlayerFiles.Version}; a newer player may be needed", lines[2]);
        Assert.Equal($"segments[1].in.curve is not known to player {PlayerFiles.Version}; a newer player may be needed", lines[3]);
        Assert.StartsWith("segments[1].start: the segment starts on frame 21; after the one before it, it starts on frame 16", lines[4]);
        Assert.StartsWith("segments[2].in.seconds: the dip is longer than the segments it joins", lines[5]);
        Assert.StartsWith("segments[0]: the first segment starts the movie", lines[6]);
        Assert.StartsWith("segments[2].plan: \"../other.json\" is not a path beside the plan", lines[7]);
        Assert.StartsWith("segments[2].frames: every segment needs a frames folder of its own", lines[8]);
        Assert.StartsWith("segments: the last segment ends on frame 45 and the movie has 65", lines[9]);
    }

    [Fact]
    public void AModuleThatDoesNotSayItsKeys_ChecksItsSectionItself()
    {
        string? output = RunPython("""
            from thide_player.registry import Registry

            extra = Registry([sys.argv[1]]).load("marker_cube")
            plan = {"frame": {}, "timeline": {}, "marker_cube": {"points": [], "size": 1, "colour": "red"}}
            core.check_sections(plan, modules + [extra])
            print("played")
            """, PlayerRunner.ExtraModules);
        if (output is null) return;

        Assert.Equal("played\n", output);
    }
}
