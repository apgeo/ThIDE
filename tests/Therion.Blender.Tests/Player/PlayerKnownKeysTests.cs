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
            names = {{JsonSerializer.Serialize(ScenePlan.PlayerFeatures)}}
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
