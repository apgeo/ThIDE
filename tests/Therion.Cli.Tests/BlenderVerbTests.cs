// CLI `blender` verb tests (BA-B14). The export path needs no Blender, so it's a full smoke:
// a real corpus .lox → render.py + model.ply + scene-meta.json. Plus the argument/spec error
// paths and their exit codes. A real render is behind the module's THIDE_BLENDER_E2E gate.

using System.IO;
using System.Threading.Tasks;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-" + Guid.NewGuid().ToString("N"));

    public BlenderVerbTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static string CorpusLox() => TestCorpus.AvCerbulLox();

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    // ---- export smoke (no Blender) ----

    [CorpusFact]
    public async Task Export_WritesScriptAndAssets()
    {
        var (code, stdout, _) = await Run("blender", CorpusLox(), "--preset", "Orbit showcase", "--export", _dir);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(_dir, "render.py")));
        Assert.True(File.Exists(Path.Combine(_dir, "model.ply")));
        Assert.True(File.Exists(Path.Combine(_dir, "scene-meta.json")));
        Assert.Contains("render.py", stdout);
        Assert.Contains("CAM_KEYS", File.ReadAllText(Path.Combine(_dir, "render.py"))); // orbit ⇒ keyframed camera
    }

    [CorpusFact]
    public async Task Export_DefaultsToOrbitShowcase_WhenNoPreset()
    {
        var (code, _, _) = await Run("blender", CorpusLox(), "--export", _dir);
        Assert.Equal(0, code);
        var script = File.ReadAllText(Path.Combine(_dir, "render.py"));
        Assert.Contains("camera: orbit", script);
    }

    [CorpusFact]
    public async Task Export_HonoursASpecFile()
    {
        // A minimal still-set spec routed through --spec.
        var specJson = """
            { "version": 1, "camera": { "template": "StillSet" },
              "output": { "kind": "FrameSequence", "width": 800, "height": 600 } }
            """;
        var specPath = Path.Combine(_dir, "spec.json");
        await File.WriteAllTextAsync(specPath, specJson);

        var outDir = Path.Combine(_dir, "out");
        var (code, _, _) = await Run("blender", CorpusLox(), "--spec", specPath, "--export", outDir);
        Assert.Equal(0, code);
        var script = File.ReadAllText(Path.Combine(outDir, "render.py"));
        Assert.Contains("camera: stillset", script);              // the spec's StillSet template
        Assert.Contains("file_format = 'PNG'", script);           // FrameSequence output
    }

    // ---- error paths + exit codes ----

    [Fact]
    public async Task MissingModel_IsUsageError()
    {
        var (code, _, err) = await Run("blender", Path.Combine(_dir, "nope.lox"), "--export", _dir);
        Assert.Equal(2, code);
        Assert.Contains("not found", err);
    }

    [Fact]
    public async Task NoModelArgument_IsUsageError()
    {
        var (code, _, err) = await Run("blender", "--export", _dir);
        Assert.Equal(2, code);
        Assert.Contains("requires a model file", err);
    }

    [Fact]
    public async Task UnknownFormat_IsUsageError()
    {
        var txt = Path.Combine(_dir, "notamodel.txt");
        await File.WriteAllTextAsync(txt, "hi");
        var (code, _, err) = await Run("blender", txt, "--export", _dir);
        Assert.Equal(2, code);
        Assert.Contains("format", err);
    }

    [CorpusFact]
    public async Task UnknownPreset_ListsTheOptions()
    {
        var (code, _, err) = await Run("blender", CorpusLox(), "--preset", "No Such Preset", "--export", _dir);
        Assert.Equal(2, code);
        Assert.Contains("Orbit showcase", err); // the error names the available presets
    }

    [CorpusFact]
    public async Task BadSpecJson_IsUsageError()
    {
        var specPath = Path.Combine(_dir, "bad.json");
        await File.WriteAllTextAsync(specPath, "{ not json");
        var (code, _, err) = await Run("blender", CorpusLox(), "--spec", specPath, "--export", _dir);
        Assert.Equal(2, code);
        Assert.Contains("spec", err);
    }
}
