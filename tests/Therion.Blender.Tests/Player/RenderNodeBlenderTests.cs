// The render node worker in a real Blender, on an invented cave: a draft job taken from a fake
// server on 127.0.0.1 is rendered with the worker's own player and handed back as an MP4 and as a
// WebM, the movie the server receives having every frame. Opt-in: THIDE_BLENDER_SMOKE=1 and
// THIDE_BLENDER_E2E=1; THIDE_BLENDER_PATH picks the Blender.

using System.Text;
using Therion.Blender.Execution;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Xunit.Abstractions;

namespace Therion.Blender.Tests.Player;

public sealed class RenderNodeBlenderTests(ITestOutputHelper output)
{
    [BlenderE2EFact]
    public Task ADraftJob_FromAServer_IsHandedBackAsAnMp4() => DraftJob("mp4");

    [BlenderE2EFact]
    public Task ADraftJob_FromAServer_IsHandedBackAsAWebm() => DraftJob("webm");

    private async Task DraftJob(string container)
    {
        string root = PlayerRunner.NewCase("node-" + container);
        string lox = Path.Combine(root, "small.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec(),
            Output = new OutputSpec { Kind = OutputKind.Video, Width = 96, Height = 64, BaseName = "clip" },
            Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
        };
        var content = RenderBundleWriter.FromModel(source, spec, Path.Combine(root, "work"), draft: true);
        string zip = Path.Combine(root, "tour.zip");
        RenderBundleWriter.WriteZip(content, zip);

        using var server = new FakeRenderServer();
        server.Offer("e2e-" + container, zip, plan: "draft", container: container == "mp4" ? null : container, lease: 30);
        var log = new List<string>();
        using var worker = new RenderNodeWorker(
            new BlenderLocator(new ProcessBlenderProbe()), new BlenderRunner(new RealBlenderProcessLauncher()),
            new RenderNodeOptions { Server = server.Url, Token = server.Token, NodeName = "e2e", Scratch = Path.Combine(root, "scratch"), Poll = TimeSpan.FromSeconds(1) },
            () => SceneProbeRunner.Blender.Path, line => { lock (log) log.Add(line); });
        var run = await worker.RunAsync(once: true, CancellationToken.None);

        Assert.Equal(new RenderNodeRun(1, false), run);
        Assert.True(server.Failed.IsEmpty, string.Join("; ", server.Failed.Select(f => f.Message.Reason + ": " + f.Message.Message)));
        var (_, complete) = Assert.Single(server.Completed);
        string movieName = $"e2e-{container}.{container}";
        Assert.Contains(complete.Files, f => f.Name == movieName && f.Role == "movie");
        string movie = Path.Combine(root, movieName);
        File.WriteAllBytes(movie, server.Files[$"e2e-{container}/{movieName}"]);
        Assert.Equal(8, (await SceneProbeRunner.ReadMovieAsync(movie)).Frames);
        string blenderLog = Encoding.UTF8.GetString(server.Files[$"e2e-{container}/e2e-{container}.log"]);
        Assert.Contains("THIDE:", blenderLog);
        Assert.DoesNotContain(log, l => l.Contains(server.Token, StringComparison.Ordinal));
        output.WriteLine($"{container}: {complete.Seconds:0.0} s, {complete.SecondsPerFrame:0.###} s a frame, {server.Beats.Count} heartbeats");
    }
}
