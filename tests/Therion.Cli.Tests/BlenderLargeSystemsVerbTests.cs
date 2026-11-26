// `therion-cli blender` for large caves and long renders, and `therion-cli render-worker`, without
// Blender: the estimate of a render, and what the options of a range of frames, of the parts and of
// the worker refuse. On an invented cave.

using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public sealed class BlenderLargeSystemsVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-large-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderLargeSystemsVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "cave.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Medium), _lox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = args[0] == "render-worker"
            ? await RenderWorkerVerb.RunAsync(args, so, se)
            : await BlenderVerb.RunAsync(["blender", _lox, .. args], so, se);
        return (code, so.ToString(), se.ToString());
    }

    [Fact]
    public async Task Estimate_PrintsFramesTimeDiskAndMemory_WithoutBlender()
    {
        var (code, output, error) = await Run("--preset", "Orbit showcase", "--out", _dir, "--estimate", "--blender", "/nowhere/blender");
        Assert.True(code == 0, error);
        Assert.Matches(@"^\d+ frame\(s\) at about [\d.]+ s a frame \(assumed\): .+ GB of disk; .+ GB of memory", output.Trim());
        Assert.Empty(Directory.GetDirectories(_dir));  // the job folder of the estimate is gone
    }

    [Theory]
    [InlineData("--frame-range 1-4", "--frame-range renders frames into the folder --frames names")]
    [InlineData("--frame-range 4-1 --frames f", "--frame-range takes two frame numbers")]
    [InlineData("--frame-range 1-4 --frames f --compose-only", "does not go with --compose-only")]
    [InlineData("--parts sometimes", "--parts takes auto, on or off")]
    [InlineData("--parts 1", "--parts takes auto, on or off")]
    public async Task TheOptionsOfLargeRenders_RefuseWhatMeansNothing(string options, string message)
    {
        var (code, _, error) = await Run(["--preset", "Orbit showcase", .. options.Split(' ')]);
        Assert.Equal(2, code);
        Assert.Contains(message, error);
    }

    [Theory]
    [InlineData("render-worker --inbox in", "needs --inbox <dir> and --outbox <dir>")]
    [InlineData("render-worker --inbox nowhere --outbox out", "the inbox is not a folder")]
    [InlineData("render-worker --inbox . --outbox out --max-hours 0", "--max-hours takes a number above 0")]
    public async Task TheWorker_RefusesWhatItCannotRunOn(string args, string message)
    {
        var (code, _, error) = await Run(args.Split(' '));
        Assert.Equal(2, code);
        Assert.Contains(message, error);
    }

    [Fact]
    public async Task TheWorker_Once_TakesNothingFromAnEmptyInbox()
    {
        string inbox = Path.Combine(_dir, "in");
        Directory.CreateDirectory(inbox);
        var (code, output, _) = await Run("render-worker", "--inbox", inbox, "--outbox", Path.Combine(_dir, "out"), "--once",
            "--scratch", Path.Combine(_dir, "scratch"));
        Assert.Equal(0, code);
        Assert.Equal("0 job(s)", output.Trim());
    }
}
