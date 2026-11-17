// The player as shipped in the assembly, and the two forms it is written in: a job folder, and one
// script that carries the plan and the player. What needs a real Blender is in PlayerBlenderTests.

using System.Diagnostics;
using System.Text;
using Therion.Blender.Plan;
using Therion.Blender.Player;

namespace Therion.Blender.Tests.Player;

public class PlayerWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-player-writer-{Guid.NewGuid():N}");

    public PlayerWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>The full plan with only its first model, whose file the tests invent.</summary>
    private static ScenePlan OneModelPlan => PlanFixtures.Full() with { Models = [PlanFixtures.Full().Models[0]] };

    private string MeshFile(string name = "source.ply")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [1, 2, 3, 250]);
        return path;
    }

    // ---- the shipped player ----

    [Fact]
    public void ThePlayer_IsACoreAndOneFilePerFeature()
    {
        Assert.Equal(
            [
                "render.py",
                "thide_player/__init__.py",
                "thide_player/api.py",
                "thide_player/core.py",
                "thide_player/modules/__init__.py",
                "thide_player/modules/camera_track.py",
                "thide_player/modules/look.py",
                "thide_player/modules/models.py",
                "thide_player/modules/output.py",
                "thide_player/registry.py",
            ],
            PlayerFiles.JobFiles.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryFeatureOfAPlan_HasItsModule()
    {
        var modules = PlayerFiles.JobFiles.Select(f => f.Path).ToHashSet();

        Assert.All(ScenePlan.PlayerFeatures, feature => Assert.Contains($"thide_player/modules/{feature}.py", modules));
    }

    [Fact]
    public void ThePlayer_ReadsThePlanVersionThisLibraryWrites()
    {
        Assert.Equal(BlenderModule.ScenePlanSchemaVersion, PlayerFiles.PlanVersion);
        Assert.Matches(@"^\d+\.\d+$", PlayerFiles.Version);
    }

    [Fact]
    public void TheCore_KnowsNoFeature()
    {
        string core = PlayerFiles.JobFiles.Single(f => f.Path == "thide_player/core.py").Text;

        // It reaches the modules through the registry alone: it neither imports one nor names one.
        Assert.All(ScenePlan.PlayerFeatures, feature =>
        {
            Assert.DoesNotContain("modules." + feature, core);
            Assert.DoesNotContain("\"" + feature + "\"", core);
        });
        Assert.Contains("registry.load(name)", core);
    }

    [Fact]
    public void ThePlayer_UsesOnlyBlenderAndTheStandardLibrary()
    {
        string[] allowed =
        [
            "argparse", "atexit", "base64", "importlib", "importlib.abc", "importlib.util", "json", "os", "re", "shutil",
            "sys", "tempfile", "traceback", "bpy", "bmesh", "mathutils", "thide_player", ".", ".registry",
        ];
        foreach (var file in PlayerFiles.JobFiles)
        {
            Assert.DoesNotContain('\r', file.Text);
            foreach (string line in file.Text.Split('\n'))
            {
                string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (line.StartsWith("import ", StringComparison.Ordinal) || line.StartsWith("from ", StringComparison.Ordinal))
                    Assert.True(allowed.Contains(words[1]), $"{file.Path} imports {words[1]}");
            }
        }
    }

    // ---- job folder ----

    [Fact]
    public void WriteJob_PutsThePlanThePlayerAndTheMeshesInOneFolder()
    {
        string folder = Path.Combine(_dir, "job");

        var job = PlayerWriter.WriteJob(OneModelPlan, folder, new Dictionary<string, string> { ["model.ply"] = MeshFile() });

        Assert.Equal(folder, job.Folder);
        Assert.Equal(Path.Combine(folder, "render.py"), job.ScriptPath);
        Assert.Equal(Path.Combine(folder, "scene-plan.json"), job.PlanPath);
        Assert.Equal(ScenePlanSerializer.Write(OneModelPlan), File.ReadAllText(job.PlanPath));
        Assert.Equal([1, 2, 3, 250], File.ReadAllBytes(Path.Combine(folder, "model.ply")));
        Assert.All(PlayerFiles.JobFiles, file =>
            Assert.Equal(file.Text, File.ReadAllText(Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar)))));
    }

    [Fact]
    public void WriteJob_TakesMeshesThatAreAlreadyThere_AndPutsOthersInSubfolders()
    {
        string folder = Path.Combine(_dir, "job");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "model.ply"), [9]);

        PlayerWriter.WriteJob(PlanFixtures.Full(), folder, new Dictionary<string, string> { ["meshes/far.glb"] = MeshFile() });

        Assert.Equal([9], File.ReadAllBytes(Path.Combine(folder, "model.ply")));
        Assert.True(File.Exists(Path.Combine(folder, "meshes", "far.glb")));
    }

    [Fact]
    public void WriteJob_ReplacesAPlayerThatIsAlreadyThere()
    {
        string folder = Path.Combine(_dir, "job");
        string stale = Path.Combine(folder, "thide_player", "modules", "old_feature.py");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "raise SystemExit(1)\n");
        var files = new Dictionary<string, string> { ["model.ply"] = MeshFile() };

        PlayerWriter.WriteJob(OneModelPlan, folder, files);

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void WriteJob_RefusesAPlanWhoseMeshIsMissing()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => PlayerWriter.WriteJob(OneModelPlan, Path.Combine(_dir, "job")));
        Assert.Contains("CaveModel", ex.Message);
    }

    [Fact]
    public void WriteJob_RefusesAnInvalidPlan_AndAFileOutsideTheFolder()
    {
        string folder = Path.Combine(_dir, "job");

        var invalid = Assert.Throws<ArgumentException>(() => PlayerWriter.WriteJob(new ScenePlan(), folder));
        Assert.Contains("camera.track", invalid.Message);
        Assert.Throws<ArgumentException>(() => PlayerWriter.WriteJob(
            OneModelPlan, folder, new Dictionary<string, string> { ["../model.ply"] = MeshFile() }));
        Assert.False(File.Exists(Path.Combine(_dir, "model.ply")));
    }

    // ---- single file ----

    [Fact]
    public void SingleFile_CarriesThePlanEveryModuleAndTheGivenFiles()
    {
        string script = PlayerWriter.SingleFile(OneModelPlan, new Dictionary<string, byte[]> { ["model.ply"] = [1, 2, 3, 250] });

        Assert.Contains("PLAN = r'''\n{\n  \"version\": 1,", script);
        Assert.Contains("    \"model.ply\": \"AQID+g==\",\n", script);
        foreach (string module in new[]
                 {
                     "thide_player", "thide_player.api", "thide_player.core", "thide_player.registry", "thide_player.modules",
                     "thide_player.modules.models", "thide_player.modules.camera_track", "thide_player.modules.look",
                     "thide_player.modules.output",
                 })
            Assert.Contains($"    \"{module}\": r'''\n", script);
        Assert.EndsWith("core.main(os.path.dirname(os.path.abspath(__file__)), PLAN, FILES)\n", script);
        Assert.DoesNotContain('\r', script);
    }

    [Fact]
    public void SingleFile_IsTheSameBytesEveryTime()
        => Assert.Equal(PlayerWriter.SingleFile(OneModelPlan), PlayerWriter.SingleFile(OneModelPlan));

    [Fact]
    public void SingleFile_RefusesAFileNoModelNames()
        => Assert.Throws<ArgumentException>(
            () => PlayerWriter.SingleFile(OneModelPlan, new Dictionary<string, byte[]> { ["Model.ply"] = [1] }));

    [Fact]
    public void SingleFile_NoTextOfThePlanCanEndItsQuotes()
    {
        var plan = OneModelPlan with { Name = HostileName };

        string script = PlayerWriter.SingleFile(plan);

        // The plan is the one raw string between PLAN = r''' and the next '''. No apostrophe is left
        // in it to close it early.
        int start = script.IndexOf("PLAN = r'''\n", StringComparison.Ordinal) + "PLAN = r'''\n".Length;
        int end = script.IndexOf("'''", start, StringComparison.Ordinal);
        string carried = script[start..end];
        Assert.DoesNotContain('\'', carried);
        Assert.Equal(plan.Name, ScenePlanSerializer.Read(carried).Name);
    }

    /// <summary>A name that tries to leave a string and run something.</summary>
    internal const string HostileName =
        "O'Brien's ''' + __import__('os').system('echo owned') + r''' \"cave\" \\ \\' \n# end — Peștera №5";

    [Fact]
    public void ThePlayerAndASingleFile_CompileAsPython_WhenPythonIsAvailable()
    {
        string? python = FindPython();
        if (python is null) return; // no Python on PATH: a documented no-op, as for the generated scripts

        string folder = Path.Combine(_dir, "job");
        PlayerWriter.WriteJob(OneModelPlan, folder, new Dictionary<string, string> { ["model.ply"] = MeshFile() });
        string single = Path.Combine(_dir, "single.py");
        File.WriteAllText(single, PlayerWriter.SingleFile(OneModelPlan with { Name = HostileName }), new UTF8Encoding(false));

        var files = PlayerFiles.JobFiles.Select(f => Path.Combine(folder, f.Path.Replace('/', Path.DirectorySeparatorChar))).Append(single);
        foreach (string file in files)
        {
            var (exitCode, output) = Run(python, "-m", "py_compile", file);
            Assert.True(exitCode == 0, $"py_compile failed for {file}: {output}");
        }
    }

    [Fact]
    public void ASingleFilesPlan_ArrivesInPythonAsItWasWritten_WhenPythonIsAvailable()
    {
        string? python = FindPython();
        if (python is null) return;

        // Everything before the player's start is data: run that much and read the plan back.
        string script = PlayerWriter.SingleFile(OneModelPlan with { Name = HostileName });
        string data = script[..script.IndexOf("# ---- the player's start", StringComparison.Ordinal)];
        string probe = Path.Combine(_dir, "data.py");
        File.WriteAllText(
            probe,
            data + "import json, sys\nsys.stdout.buffer.write(json.loads(PLAN)[\"name\"].encode(\"utf-8\"))\n",
            new UTF8Encoding(false));

        var (exitCode, output) = Run(python, probe);

        Assert.Equal(0, exitCode);
        Assert.Equal(HostileName, output);
    }

    [Fact]
    public void TheRegistry_LoadsAModuleFromAnExtraFolder_AndRefusesWhatItShould_WhenPythonIsAvailable()
    {
        string? python = FindPython();
        if (python is null) return;

        // The registry needs no Blender: it runs here as it is shipped. In Blender, with a module
        // that builds something, it is in PlayerBlenderTests.
        string folder = Path.Combine(_dir, "job");
        PlayerWriter.WriteJob(OneModelPlan, folder, new Dictionary<string, string> { ["model.ply"] = MeshFile() });
        string extra = Path.Combine(_dir, "extra");
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, "doubler.py"), "SECTION = \"doubler\"\n\n\ndef build(ctx, section):\n    ctx.append(section * 2)\n");
        File.WriteAllText(Path.Combine(extra, "look.py"), "def build(ctx, section):\n    pass\n");
        File.WriteAllText(Path.Combine(extra, "no_build.py"), "SECTION = \"none\"\n");
        string check = Path.Combine(_dir, "check.py");
        File.WriteAllText(check, """
            import sys

            sys.dont_write_bytecode = True
            sys.path.insert(0, sys.argv[1])
            from thide_player import api
            from thide_player.registry import Registry

            registry = Registry([sys.argv[2]])
            module = registry.load("doubler")
            built = []
            module.build(built, 21)
            print("doubler", module.SECTION, built[0])
            for name in ("look", "no_build", "nowhere", "Bad-Name", "../doubler"):
                try:
                    registry.load(name)
                    print(name, "was loaded")
                except api.PlanError as error:
                    print(name, "|", error)
            try:
                Registry([sys.argv[2] + "-missing"])
            except api.PlanError as error:
                print("folder |", error)
            """.Replace("            ", ""));

        var (exitCode, output) = Run(python, check, folder, extra);

        Assert.True(exitCode == 0, output);
        string[] lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("doubler doubler 42", lines[0]);
        Assert.StartsWith("look | the module \"look\" is defined more than once: thide_player.modules, ", lines[1]);
        Assert.StartsWith("no_build | the module \"no_build\" (", lines[2]);
        Assert.EndsWith("has no build(ctx, section) function", lines[2]);
        Assert.StartsWith("nowhere | the plan needs the module \"nowhere\", which this player does not have; looked in ", lines[3]);
        Assert.StartsWith("Bad-Name | features: \"Bad-Name\" is not a module name", lines[4]);
        Assert.StartsWith("../doubler | features: \"../doubler\" is not a module name", lines[5]);
        Assert.StartsWith("folder | module folder not found: ", lines[6]);
    }

    private static (int ExitCode, string Output) Run(string program, params string[] arguments)
    {
        var psi = new ProcessStartInfo(program)
        {
            RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);
        return (process.ExitCode, output + error.Result);
    }

    private static string? FindPython()
    {
        foreach (string name in new[] { "python3", "python" })
        {
            try
            {
                if (Run(name, "--version").ExitCode == 0) return name;
            }
            catch
            {
                // not on PATH — try the next candidate
            }
        }
        return null;
    }
}
