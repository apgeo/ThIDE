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

    [Theory]
    [InlineData("folder")]
    [InlineData("server")]
    public async Task TheWorker_RefusesAScratchFolderOthersMayWrite(string mode)
    {
        if (OperatingSystem.IsWindows()) return;
        // As another user would leave a scratch folder in a shared temp folder: open to all.
        string scratch = Directory.CreateDirectory(Path.Combine(_dir, "open-scratch")).FullName;
        File.SetUnixFileMode(scratch, (UnixFileMode)0x1FF);
        string inbox = Directory.CreateDirectory(Path.Combine(_dir, "in")).FullName;
        using var server = new Therion.Blender.Tests.FakeRenderServer();
        string[] args = mode == "folder"
            ? ["render-worker", "--inbox", inbox, "--outbox", Path.Combine(_dir, "out"), "--once", "--scratch", scratch]
            : ["render-worker", "--server", server.Url.ToString(), "--token-file", Token("t0ken"), "--once", "--scratch", scratch, "--blender", "/nowhere/blender"];
        var (code, _, error) = await Run(args);
        Assert.Equal(2, code);
        Assert.Contains("the scratch folder cannot be used: Others may write the folder", error);
        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("render-worker --server https://render.example.org/nodes", "needs both --server <url> and --token-file <file>")]
    [InlineData("render-worker --server http://render.example.org/nodes --token-file TOKEN", "plain HTTP is allowed to this machine only")]
    [InlineData("render-worker --server nodes --token-file TOKEN", "not an absolute URL")]
    [InlineData("render-worker --server https://render.example.org/ --token-file MISSING", "--token-file: the token file is not there")]
    [InlineData("render-worker --server https://render.example.org/ --token-file TOKEN --poll-seconds -1", "--poll-seconds takes a number above 0")]
    public async Task TheServerMode_RefusesWhatItCannotRunOn(string args, string message)
    {
        string token = Token("t0ken");
        var (code, _, error) = await Run(args.Replace("TOKEN", token).Replace("MISSING", Path.Combine(_dir, "none")).Split(' '));
        Assert.Equal(2, code);
        Assert.Contains(message, error);
        Assert.DoesNotContain("t0ken", error);
    }

    [Fact]
    public async Task TheServerMode_RefusesATokenFileOthersMayRead()
    {
        if (OperatingSystem.IsWindows()) return;
        string token = Token("t0ken");
        File.SetUnixFileMode(token, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var (code, _, error) = await Run("render-worker", "--server", "https://render.example.org/", "--token-file", token);
        Assert.Equal(2, code);
        Assert.Contains("others may read the token file", error);
    }

    [Fact]
    public async Task TheServerMode_StopsWith3_WhenTheServerRefusesTheToken()
    {
        using var server = new Therion.Blender.Tests.FakeRenderServer();
        var (code, output, error) = await Run("render-worker", "--server", server.Url.ToString(), "--token-file", Token("not-the-servers"),
            "--scratch", Path.Combine(_dir, "scratch"), "--blender", "/nowhere/blender", "--node-name", "cli-test");
        Assert.True(code == 3, error);
        Assert.Equal("0 job(s)", output.Trim());
        Assert.Contains("refused the node token", error);
        Assert.DoesNotContain("not-the-servers", error);
        Assert.Equal(["POST /api/nodes/claim"], server.Requests.Select(r => r.Request));
    }

    private string Token(string token)
    {
        string path = Path.Combine(_dir, "node.token");
        File.WriteAllText(path, token + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }
}
