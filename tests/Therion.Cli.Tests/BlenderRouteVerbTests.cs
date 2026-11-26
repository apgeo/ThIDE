// `therion-cli blender <model> --route …`: the camera flies a route between stations, from the
// command line, on an invented cave. What the option writes is read back from a render bundle's
// plan; a station the model has not is an error before anything is written. No Blender is run.

using System.IO.Compression;
using Therion.Blender;
using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderRouteVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-route-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderRouteVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), _lox);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    private static ScenePlan Bundled(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        using var plan = new StreamReader(archive.GetEntry(Path.GetFileNameWithoutExtension(zip) + "/scene-plan.json")!.Open());
        return ScenePlanSerializer.Read(plan.ReadToEnd());
    }

    [Fact]
    public async Task ARouteSetsTheClipsLengthAndHasAPoseForEveryFrame()
    {
        string zip = Path.Combine(_dir, "route.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--preset", "Full flythrough", "--bundle", zip, "--route", "entrance:side3:main12");
        Assert.True(code == 0, stderr);

        var plan = Bundled(zip);
        var framing = CameraFraming.FromGeometry(GeometryStage.Build(CaveModelReader.ReadFile(_lox)));
        double seconds = CameraRoutes.Measure(framing, new RouteParams { From = "entrance", Via = ["side3"], To = "main12" }).Seconds;
        Assert.InRange(plan.Timeline.FrameCount, (int)Math.Round(plan.Timeline.Fps * seconds), (int)Math.Round(plan.Timeline.Fps * seconds) + 2);
        Assert.Equal(plan.Timeline.FrameCount, plan.Camera.Track.Count);
    }

    [Fact]
    public async Task RouteSecondsFitsTheRouteIntoAGivenLength()
    {
        string zip = Path.Combine(_dir, "fit.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--preset", "Full flythrough", "--bundle", zip, "--route", "main0:main12", "--route-seconds", "7");
        Assert.True(code == 0, stderr);
        var plan = Bundled(zip);
        Assert.Equal(plan.Timeline.Fps * 7, plan.Timeline.FrameCount);
        Assert.Equal(plan.Timeline.FrameCount, plan.Camera.Track.Count);
    }

    [Theory]
    [InlineData("main0:nowhere", "'nowhere'")]
    [InlineData("main0", "--route takes")]
    [InlineData("main0::main4", "--route takes")]
    public async Task ABadRouteIsAnError(string route, string expected)
    {
        var (code, stdout, stderr) = await Run("blender", _lox, "--bundle", Path.Combine(_dir, "bad.zip"), "--route", route);
        Assert.Equal(2, code);
        Assert.Contains(expected, stderr);
        Assert.Empty(stdout);
        Assert.False(File.Exists(Path.Combine(_dir, "bad.zip")));
    }

    [Fact]
    public async Task RouteSecondsAloneIsAnError()
    {
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", Path.Combine(_dir, "x.zip"), "--route-seconds", "5");
        Assert.Equal(2, code);
        Assert.Contains("--route-seconds goes with --route", stderr);
    }
}
