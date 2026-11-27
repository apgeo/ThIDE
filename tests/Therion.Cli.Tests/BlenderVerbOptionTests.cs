// Options of `therion-cli blender` and `render-worker` that only mean something beside another, or
// that take a number with bounds: refused with exit 2 and a reason, never dropped or thrown. Also one
// shot of a composite alone. Invented caves only; nothing here runs Blender.

using System.IO.Compression;
using Therion.Blender;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderVerbOptionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-options-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;
    private readonly string _storyboard;

    public BlenderVerbOptionTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(TourCave.Build(), _lox);
        _storyboard = Path.Combine(_dir, "film.storyboard.json");
        StoryboardSerializer.WriteFile(new Storyboard
        {
            Fps = 8,
            Shots =
            [
                new ShotSpec { Name = "One", Seconds = 1, Preset = "Orbit showcase" },
                new ShotSpec { Name = "Two", Seconds = 1, Preset = "Helix descent" },
            ],
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "film" },
        }, _storyboard);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    private static string Plan(string zip, string name)
    {
        using var archive = ZipFile.OpenRead(zip);
        string root = archive.Entries[0].FullName.Split('/')[0] + "/";
        using var reader = new StreamReader(archive.GetEntry(root + name)!.Open());
        return reader.ReadToEnd();
    }

    // --route is a scene option: a whole storyboard says its shots' cameras itself.
    [Theory]
    [InlineData("--route", "entrance:farthest")]
    [InlineData("--route-seconds", "5")]
    public async Task A_scene_option_with_a_whole_storyboard_is_refused_not_dropped(string option, string value)
    {
        string zip = Path.Combine(_dir, "film.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--storyboard", _storyboard, option, value, "--bundle", zip);
        Assert.True(code == 2, $"exit {code}: {stderr}");
    }

    // The same through --tour.
    [Fact]
    public async Task Route_with_a_tour_is_refused_not_dropped()
    {
        string zip = Path.Combine(_dir, "tour.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--tour", "--route", "entrance:farthest", "--bundle", zip);
        Assert.True(code == 2, $"exit {code}: {stderr}");
    }

    // --estimate estimates a render: with --bundle or --export it would be dropped.
    [Theory]
    [InlineData("--bundle", "scene.zip")]
    [InlineData("--export", "exported")]
    public async Task Estimate_with_a_bundle_or_an_export_is_refused(string option, string target)
    {
        string path = Path.Combine(_dir, target);
        var (code, _, stderr) = await Run("blender", _lox, "--preset", "Orbit showcase", "--estimate", option, path);
        Assert.True(code == 2, $"exit {code}: {stderr}");
        Assert.Contains("--estimate", stderr);
        Assert.False(File.Exists(path) || Directory.Exists(path));
    }

    // --readme-language is a bundle's (or an export's) README: alone, a render would follow without it.
    [Fact]
    public async Task Readme_language_without_bundle_is_refused()
    {
        var (code, _, stderr) = await Run("blender", _lox, "--preset", "Orbit showcase", "--readme-language", "ro", "--estimate");
        Assert.True(code == 2, $"exit {code}: {stderr}");
        Assert.Contains("--readme-language", stderr);
    }

    // The tour's depth has one rule on every surface.
    [Theory]
    [InlineData("17")]
    [InlineData("0")]
    public async Task A_tour_depth_out_of_bounds_is_refused(string depth)
    {
        var (code, _, stderr) = await Run("blender", _lox, "--tour", "--tour-depth", depth, "--bundle", Path.Combine(_dir, "t.zip"));
        Assert.True(code == 2, $"exit {code}: {stderr}");
        Assert.Contains("--tour-depth", stderr);
    }

    // One shot of a composite that is a tour shot: a scene with a route that says "branches: auto"
    // is not a tour; what does the bundle get?
    [Fact]
    public async Task One_tour_shot_of_a_composite_alone_is_the_tour_or_is_refused()
    {
        string zip = Path.Combine(_dir, "shot2.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--composite", "Grand tour", "--shot", "2", "--bundle", zip);
        if (code != 0) return;   // refused: fine
        string plan = Plan(zip, "scene-plan.json");
        // The tour writes its side passages' corner texts; a plain route does not.
        Assert.Contains("Side passage", plan);
    }

    // One shot of "Exploration history" that flies "from": "newest".
    [Fact]
    public async Task One_newest_shot_of_a_composite_alone_is_planned_or_refused_with_reason()
    {
        string zip = Path.Combine(_dir, "shot-newest.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--composite", "Exploration history", "--shot", "2", "--bundle", zip);
        Assert.True(code == 0 || stderr.Contains("newest", StringComparison.OrdinalIgnoreCase), $"exit {code}: {stderr}");
    }

    // render-worker: a number that parses but cannot be held is a usage error, not an exception.
    [Theory]
    [InlineData("--max-hours", "Infinity")]
    [InlineData("--poll", "1e300")]
    [InlineData("--poll-seconds", "1e300")]
    [InlineData("--max-triangles", "1e30")]
    [InlineData("--MAX-HOURS", "-1")]
    public async Task Render_worker_refuses_a_time_it_cannot_hold(string option, string value)
    {
        string inbox = Path.Combine(_dir, "in"), outbox = Path.Combine(_dir, "out");
        Directory.CreateDirectory(inbox);
        var so = new StringWriter();
        var se = new StringWriter();
        int code = -1;
        var ex = await Record.ExceptionAsync(async () => code = await RenderWorkerVerb.RunAsync(
            ["render-worker", "--inbox", inbox, "--outbox", outbox, "--once", option, value], so, se));
        Assert.Null(ex);
        Assert.Equal(2, code);
        Assert.Contains(option, se.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Render_worker_takes_one_poll_option()
    {
        string inbox = Path.Combine(_dir, "in"), outbox = Path.Combine(_dir, "out");
        Directory.CreateDirectory(inbox);
        var se = new StringWriter();
        int code = await RenderWorkerVerb.RunAsync(
            ["render-worker", "--inbox", inbox, "--outbox", outbox, "--once", "--poll", "2", "--poll-seconds", "3"], new StringWriter(), se);
        Assert.Equal(2, code);
        Assert.Contains("--poll", se.ToString());
    }
}
