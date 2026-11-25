// The Blender render tools over a fake renderer: what they ask the renderer for (preset, overrides,
// model, output folder), how a job is followed and stopped, and what they refuse. The renderer itself is
// tested in Therion.Blender.Tests; the in-app wiring end to end in ThIDE.Tests.

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Cave3D;
using Therion.Mcp.Dispatch;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public sealed class BlenderRenderToolsTests : IDisposable
{
    private readonly FixtureWorkspace _fixture = FixtureWorkspace.Create();
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "thmcp_blender_" + Guid.NewGuid().ToString("N"));
    private readonly FakeBlenderHost _blender;
    private readonly BlenderRenderJobs _jobs = new();

    public BlenderRenderToolsTests()
    {
        Directory.CreateDirectory(_scratch);
        // The fake renderer never parses it, so a few bytes stand for an invented model.
        Directory.CreateDirectory(_fixture.PathTo("out"));
        File.WriteAllBytes(_fixture.PathTo("out", "cave.3d"), [1, 2, 3]);
        File.WriteAllText(_fixture.PathTo("notes.txt"), "not a model");
        _blender = new FakeBlenderHost(_fixture.PathTo("out", "cave.3d"), Path.Combine(_scratch, "jobs"));
    }

    public void Dispose()
    {
        _jobs.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task<(BlenderRenderTools Render, BlenderStatusTools Status)> ToolsAsync()
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(_fixture.Thconfig);
        return (new BlenderRenderTools(host, _blender, _jobs), new BlenderStatusTools(_blender, _jobs));
    }

    private static async Task<RenderJobStatus> FinishedAsync(BlenderStatusTools status, string jobId)
    {
        for (int i = 0; i < 10; i++)
        {
            var answer = await status.RenderStatus(jobId, waitSeconds: 10);
            Assert.True(answer.Ok, answer.Error?.Message);
            if (answer.Data!.State != "running") return answer.Data;
        }
        throw new TimeoutException("the render did not finish");
    }

    // ---- presets --------------------------------------------------------------------------------

    [Fact]
    public async Task The_presets_are_the_built_in_ones_then_the_users()
    {
        _blender.Saved.Add(new RenderPreset { Name = "Quick look", Spec = new SceneSpec() });
        var (_, status) = await ToolsAsync();

        var presets = status.ListRenderPresets().Data!.Presets;

        Assert.Equal([.. BuiltInPresets.All.Select(p => p.Name), "Quick look"], presets.Select(p => p.Name));
        Assert.All(presets.SkipLast(1), p => Assert.True(p.BuiltIn));
        Assert.False(presets[^1].BuiltIn);
        var orbit = presets[0];
        Assert.Equal(("video", 1920, 1080, "orbit"), (orbit.OutputKind, orbit.Width, orbit.Height, orbit.Camera));
        Assert.Equal(12, orbit.DurationSeconds);
        Assert.Equal(360, orbit.Frames);
        var stills = presets.Single(p => p.Name == "Documentation stills");
        Assert.Null(stills.DurationSeconds);
        Assert.Equal(4, stills.Frames);
    }

    // ---- start, follow, finish ------------------------------------------------------------------

    [Fact]
    public async Task A_render_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();
        var result = await new BlenderRenderTools(host, _blender, _jobs).StartRender();

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task A_render_runs_as_a_job_and_reports_its_outputs_workspace_relative()
    {
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(model: "out/cave.3d", outputDirectory: "renders");

        Assert.True(started.Ok, started.Error?.Message);
        Assert.Equal("Orbit showcase", started.Data!.Preset);
        Assert.Equal("renders", started.Data.OutputDirectory);
        var done = await FinishedAsync(status, started.Data.JobId);
        Assert.Equal("succeeded", done.State);
        Assert.Equal(["renders/cave-render.mp4"], done.Outputs);
        Assert.True(File.Exists(_fixture.PathTo("renders", "cave-render.mp4")));
        Assert.Equal("out/cave.3d", done.Model);
        Assert.Equal("externalFile", done.ModelKind);
        Assert.Equal("CPU", done.Device);
        Assert.Equal(["a warning"], done.Warnings);
        Assert.Null(done.FailureKind);
    }

    [Fact]
    public async Task The_overrides_reach_the_renderer_and_the_rest_of_the_preset_stays()
    {
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(preset: "helix descent", model: "out/cave.3d", outputKind: "frames",
            width: 320, height: 240, durationSeconds: 2, samples: 8, outputDirectory: "renders/helix", baseName: "draft");
        Assert.True(started.Ok, started.Error?.Message);
        await FinishedAsync(status, started.Data!.JobId);

        var spec = _blender.Renderer.LastSpec!;
        var preset = BuiltInPresets.HelixDescent().Spec;
        Assert.Equal(OutputKind.FrameSequence, spec.Output.Kind);
        Assert.Equal((320, 240, "draft"), (spec.Output.Width, spec.Output.Height, spec.Output.BaseName));
        Assert.Equal(_fixture.PathTo("renders", "helix"), spec.Output.OutputDirectory);
        Assert.Equal(2, spec.Animation.DurationSeconds);
        Assert.Equal(8, spec.Engine.Samples);
        Assert.Equal(preset.Camera, spec.Camera);
        Assert.Equal(preset.Lighting, spec.Lighting);
        Assert.Equal(("frames", 60), (started.Data.OutputKind, started.Data.Frames));
    }

    [Fact]
    public async Task Without_an_output_directory_the_render_goes_to_a_temporary_folder()
    {
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(model: "out/cave.3d");
        var done = await FinishedAsync(status, started.Data!.JobId);

        Assert.Null(started.Data.OutputDirectory);
        Assert.Equal("", _blender.Renderer.LastSpec!.Output.OutputDirectory);
        Assert.StartsWith(Path.Combine(_scratch, "jobs"), Assert.Single(done.Outputs));
    }

    [Fact]
    public async Task A_users_preset_cannot_send_the_outputs_where_it_was_saved()
    {
        var elsewhere = Path.Combine(_scratch, "elsewhere");
        _blender.Saved.Add(BuiltInPresets.OrbitShowcase() with
        {
            Name = "Mine",
            BuiltIn = false,
            Spec = BuiltInPresets.OrbitShowcase().Spec with { Output = BuiltInPresets.OrbitShowcase().Spec.Output with { OutputDirectory = elsewhere } },
        });
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(preset: "Mine", model: "out/cave.3d");
        await FinishedAsync(status, started.Data!.JobId);

        Assert.Equal("", _blender.Renderer.LastSpec!.Output.OutputDirectory);
        Assert.False(Directory.Exists(elsewhere));
    }

    [Fact]
    public async Task Without_a_model_the_projects_own_is_asked_for_re_exported_unless_declined()
    {
        var (render, status) = await ToolsAsync();

        var first = await render.StartRender();
        var done = await FinishedAsync(status, first.Data!.JobId);
        var second = await render.StartRender(reExport: false);
        await FinishedAsync(status, second.Data!.JobId);

        Assert.Null(_blender.Requests[0].ExternalFilePath);
        Assert.True(_blender.Requests[0].AllowReExport);
        Assert.False(_blender.Requests[1].AllowReExport);
        Assert.Equal("workspaceArtifact", done.ModelKind);
        Assert.Equal("The model is older than the sources.", done.StaleModel);
    }

    [Fact]
    public async Task A_named_model_is_asked_for_by_its_full_path()
    {
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(model: "out/../out/cave.3d");
        await FinishedAsync(status, started.Data!.JobId);

        Assert.Equal(_fixture.PathTo("out", "cave.3d"), Assert.Single(_blender.Requests).ExternalFilePath);
    }

    [Fact]
    public async Task One_render_runs_at_a_time()
    {
        _blender.Renderer.Gate = new TaskCompletionSource();
        var (render, status) = await ToolsAsync();

        var first = await render.StartRender(model: "out/cave.3d");
        var second = await render.StartRender(model: "out/cave.3d");

        Assert.Equal(ToolErrorCodes.RenderBusy, second.Error!.Code);
        Assert.Contains(first.Data!.JobId, second.Error.Message);
        _blender.Renderer.Gate.SetResult();
        Assert.Equal("succeeded", (await FinishedAsync(status, first.Data.JobId)).State);
        Assert.True((await render.StartRender(model: "out/cave.3d")).Ok, "a finished render frees the slot");
    }

    [Fact]
    public async Task A_render_the_host_runs_itself_keeps_the_agent_waiting_and_each_render_gives_the_slot_back()
    {
        var (render, status) = await ToolsAsync();

        _blender.PanelRendering = true;
        var refused = await render.StartRender(model: "out/cave.3d");
        Assert.Equal(ToolErrorCodes.RenderBusy, refused.Error!.Code);
        Assert.Contains("panel", refused.Error.Message);
        Assert.Empty(_blender.Ended);

        _blender.PanelRendering = false;
        var started = await render.StartRender(model: "out/cave.3d");
        Assert.Equal("succeeded", (await FinishedAsync(status, started.Data!.JobId)).State);
        var ended = Assert.Single(_blender.Ended);
        Assert.Contains(started.Data.JobId, ended.Name);
        Assert.True(ended.Succeeded);
    }

    [Fact]
    public async Task Status_reports_progress_while_the_render_runs()
    {
        _blender.Renderer.Gate = new TaskCompletionSource();
        var (render, status) = await ToolsAsync();
        var started = await render.StartRender(model: "out/cave.3d");
        await _blender.Renderer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var running = (await status.RenderStatus()).Data!;

        Assert.Equal("running", running.State);
        Assert.Equal(started.Data!.JobId, running.JobId);
        Assert.Equal(new RenderProgressDto("rendering", "Rendering", 0.5, 1, 2), running.Progress);
        Assert.Equal("CPU", running.Device);
        Assert.Empty(running.Outputs);
        _blender.Renderer.Gate.SetResult();
    }

    [Fact]
    public async Task Cancelling_stops_the_renderer_and_says_so()
    {
        _blender.Renderer.Gate = new TaskCompletionSource();
        var (render, _) = await ToolsAsync();
        var started = await render.StartRender(model: "out/cave.3d");
        await _blender.Renderer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var cancelled = await render.CancelRender(started.Data!.JobId);

        Assert.True(cancelled.Ok, cancelled.Error?.Message);
        Assert.Equal("cancelled", cancelled.Data!.State);
        Assert.Equal("cancelled", cancelled.Data.FailureKind);
        Assert.True(_blender.Renderer.SawCancellation);
        Assert.Equal("cancelled", (await render.CancelRender()).Data!.State);   // again: nothing changes
    }

    [Fact]
    public async Task Cancelling_before_blender_runs_ends_the_job_too()
    {
        _blender.AcquireGate = new TaskCompletionSource();
        var (render, _) = await ToolsAsync();
        var started = await render.StartRender();

        var cancelled = await render.CancelRender(started.Data!.JobId);

        Assert.Equal(("cancelled", "cancelled"), (cancelled.Data!.State, cancelled.Data.FailureKind));
        Assert.Null(_blender.Renderer.LastSpec);
    }

    [Fact]
    public async Task Stopping_the_server_cancels_a_running_render()
    {
        _blender.Renderer.Gate = new TaskCompletionSource();
        var (render, status) = await ToolsAsync();
        var started = await render.StartRender(model: "out/cave.3d");
        await _blender.Renderer.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _jobs.Dispose();

        Assert.True(_blender.Renderer.SawCancellation);
        Assert.Equal("cancelled", (await status.RenderStatus(started.Data!.JobId)).Data!.State);
    }

    [Fact]
    public async Task A_failed_render_reports_why_and_where_the_log_is()
    {
        _blender.Renderer.Outcome = new RenderResult
        {
            FailureKind = RenderFailureKind.BlenderNotFound,
            ErrorMessage = "Blender was not found.",
            JobLogPath = "/tmp/job.log",
        };
        var (render, status) = await ToolsAsync();

        var done = await FinishedAsync(status, (await render.StartRender(model: "out/cave.3d")).Data!.JobId);

        Assert.Equal(("failed", "blenderNotFound", "Blender was not found."), (done.State, done.FailureKind, done.Error));
        Assert.Equal("/tmp/job.log", done.JobLog);
    }

    [Fact]
    public async Task No_model_to_render_fails_the_job_with_model_unavailable()
    {
        _blender.NoModel = true;
        var (render, status) = await ToolsAsync();

        var done = await FinishedAsync(status, (await render.StartRender()).Data!.JobId);

        Assert.Equal(("failed", "modelUnavailable"), (done.State, done.FailureKind));
        Assert.Contains("Build the project", done.Error);
        Assert.Null(_blender.Renderer.LastSpec);
    }

    // ---- refusals -------------------------------------------------------------------------------

    [Theory]
    [InlineData("../outside.3d", null, ToolErrorCodes.PathOutsideWorkspace)]
    [InlineData("/etc/passwd", null, ToolErrorCodes.PathOutsideWorkspace)]
    [InlineData("out/missing.3d", null, ToolErrorCodes.FileNotFound)]
    [InlineData("notes.txt", null, ToolErrorCodes.InvalidArgument)]
    [InlineData("out/cave.3d", "../renders", ToolErrorCodes.PathOutsideWorkspace)]
    [InlineData("out/cave.3d", ".thide/renders", ToolErrorCodes.ProtectedPath)]
    [InlineData("out/cave.3d", "notes.txt", ToolErrorCodes.InvalidArgument)]
    public async Task Paths_are_held_to_the_workspace(string model, string? outputDirectory, string code)
    {
        var (render, status) = await ToolsAsync();

        var result = await render.StartRender(model: model, outputDirectory: outputDirectory);

        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(_blender.Requests);
        Assert.Equal(ToolErrorCodes.JobNotFound, (await status.RenderStatus()).Error!.Code);
    }

    [Fact]
    public async Task An_unknown_preset_or_output_kind_names_the_choices()
    {
        var (render, _) = await ToolsAsync();

        var preset = await render.StartRender(preset: "Fancy");
        var kind = await render.StartRender(outputKind: "gif");

        Assert.Equal(ToolErrorCodes.InvalidArgument, preset.Error!.Code);
        Assert.Contains("\"Orbit showcase\"", preset.Error.Message);
        Assert.Equal(ToolErrorCodes.InvalidArgument, kind.Error!.Code);
        Assert.Contains("video, frames, still", kind.Error.Message);
    }

    [Fact]
    public async Task A_draft_and_a_builder_reach_the_renderer_and_the_status_says_what_built_the_scene()
    {
        var (render, status) = await ToolsAsync();
        _blender.Renderer.Outcome = new RenderResult
        {
            Succeeded = true, Builder = SceneBuilder.Generator, NotCarried = ["lighting.hdriPath"], LeftOut = ["labels.overlays.credits"],
        };

        var started = await render.StartRender(model: "out/cave.3d", draft: true, builder: "Generator");
        Assert.True(started.Ok, started.Error?.Message);
        Assert.True(started.Data!.Draft);
        Assert.Null(started.Data.Builder);
        var done = await FinishedAsync(status, started.Data.JobId);

        Assert.Equal(new RenderOptions { Builder = SceneBuilder.Generator, Draft = true }, _blender.Renderer.LastOptions);
        Assert.Equal(("generator", true), (done.Builder, done.Draft));
        Assert.Equal(["lighting.hdriPath"], done.NotCarried);
        Assert.Equal(["labels.overlays.credits"], done.LeftOut);

        // Left alone, the builder is chosen automatically and the render is the real one.
        _blender.Renderer.Outcome = null;
        started = await render.StartRender(model: "out/cave.3d");
        done = await FinishedAsync(status, started.Data!.JobId);
        Assert.Equal(new RenderOptions(), _blender.Renderer.LastOptions);
        Assert.Equal((null, false, null, null), (done.Builder, done.Draft, done.NotCarried, done.LeftOut));
    }

    [Fact]
    public async Task A_builder_that_does_not_exist_or_cannot_build_the_scene_is_refused_before_a_job_starts()
    {
        _blender.Saved.Add(new RenderPreset
        {
            Name = "Under a sky", Spec = new SceneSpec { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" } },
        });
        var (render, _) = await ToolsAsync();

        var unknown = await render.StartRender(builder: "blender");
        var refused = await render.StartRender(preset: "Under a sky", builder: "player");

        Assert.Equal(ToolErrorCodes.InvalidArgument, unknown.Error!.Code);
        Assert.Contains("auto, player, generator", unknown.Error.Message);
        Assert.Equal(ToolErrorCodes.InvalidArgument, refused.Error!.Code);
        Assert.Contains("does not carry lighting.hdriPath", refused.Error.Message);
        Assert.Empty(_blender.Requests);
        // The same preset is rendered when the builder is left to be chosen, or as a draft.
        Assert.True((await render.StartRender(preset: "Under a sky", model: "out/cave.3d", draft: true, builder: "player")).Ok);
    }

    [Fact]
    public async Task Texts_over_the_picture_are_added_to_the_presets()
    {
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(model: "out/cave.3d", title: " Peștera Inventată ", credits: "echipa",
            endCard: "Sfârșit", endCardSeconds: 2.5, captions: ["1-2.5:Sala Mare", " 3 - 4.5 : Sifonul: capăt"]);
        Assert.True(started.Ok, started.Error?.Message);
        await FinishedAsync(status, started.Data!.JobId);

        var overlays = _blender.Renderer.LastSpec!.Labels.Overlays;
        Assert.Equal(("Peștera Inventată", "echipa"), (overlays.Title, overlays.Credits));
        Assert.Equal(new EndCardSpec { Text = "Sfârșit", Seconds = 2.5 }, overlays.EndCard);
        Assert.Equal(
            [new CaptionSpec { Text = "Sala Mare", Start = 1, End = 2.5 }, new CaptionSpec { Text = "Sifonul: capăt", Start = 3, End = 4.5 }],
            overlays.Captions);
    }

    [Fact]
    public async Task Annotations_come_from_a_file_of_the_workspace_with_their_photos_named_from_where_it_lies()
    {
        Directory.CreateDirectory(_fixture.PathTo("trip", "pictures"));
        File.WriteAllBytes(_fixture.PathTo("trip", "pictures", "hall.png"), [1, 2, 3]);
        File.WriteAllText(_fixture.PathTo("trip", "notes.json"), """
            [
              { "id": "way-in", "kind": "callout", "station": "a0", "text": "Intrarea", "from": 1, "to": 4 },
              { "kind": "photo", "at": [10, 20, -5], "image": "pictures/hall.png", "within": 30 }
            ]
            """);
        var (render, status) = await ToolsAsync();

        var started = await render.StartRender(model: "out/cave.3d", annotations: "trip/notes.json");
        Assert.True(started.Ok, started.Error?.Message);
        await FinishedAsync(status, started.Data!.JobId);

        var annotations = _blender.Renderer.LastSpec!.Annotations;
        Assert.Equal(new AnnotationSpec { Id = "way-in", Kind = AnnotationKind.Callout, Station = "a0", Text = "Intrarea", From = 1, To = 4 }, annotations[0]);
        Assert.Equal((AnnotationKind.Photo, new CaveVector3(10, 20, -5), 30.0), (annotations[1].Kind, annotations[1].At!.Value, annotations[1].Within!.Value));
        Assert.Equal(WorkspacePaths.Canonicalize(_fixture.PathTo("trip", "pictures", "hall.png")), annotations[1].Image);
    }

    [Theory]
    [InlineData("../outside.json", "[]", ToolErrorCodes.PathOutsideWorkspace, "outside")]
    [InlineData("trip/none.json", null, ToolErrorCodes.FileNotFound, "No such file: trip/none.json")]
    [InlineData("trip/notes.json", "{ \"kind\": \"label\" }", ToolErrorCodes.InvalidArgument, "is not a list of annotations")]
    [InlineData("trip/notes.json", "[{ \"kind\": \"label\", \"staton\": \"a0\" }]", ToolErrorCodes.InvalidArgument, "staton")]
    [InlineData("trip/notes.json", "[{ \"kind\": \"label\", \"station\": \"a0\" }]", ToolErrorCodes.InvalidArgument, "annotations[0].text")]
    [InlineData("trip/notes.json", "[{ \"kind\": \"photo\", \"station\": \"a0\", \"image\": \"../../elsewhere/hall.png\" }]", ToolErrorCodes.PathOutsideWorkspace, "lies outside the workspace")]
    [InlineData("trip/notes.json", "[{ \"kind\": \"photo\", \"station\": \"a0\", \"image\": \"gone.png\" }]", ToolErrorCodes.FileNotFound, "No such file: trip/gone.png")]
    public async Task Annotations_that_cannot_be_used_are_refused_before_a_job_starts(string file, string? json, string code, string said)
    {
        Directory.CreateDirectory(_fixture.PathTo("trip"));
        if (json is not null && !file.StartsWith("..", StringComparison.Ordinal)) File.WriteAllText(_fixture.PathTo("trip", "notes.json"), json);
        var (render, _) = await ToolsAsync();

        var refused = await render.StartRender(model: "out/cave.3d", annotations: file);

        Assert.Equal(code, refused.Error!.Code);
        Assert.Contains(said, refused.Error.Message);
        Assert.Empty(_blender.Requests);
    }

    [Theory]
    [InlineData("Sala Mare")]
    [InlineData("4-2:backwards")]
    [InlineData("1-2:")]
    [InlineData("a-b:letters")]
    public async Task A_caption_that_cannot_be_read_is_refused_with_how_to_write_one(string caption)
    {
        var (render, _) = await ToolsAsync();

        var refused = await render.StartRender(captions: ["0-1:fine", caption]);

        Assert.Equal(ToolErrorCodes.InvalidArgument, refused.Error!.Code);
        Assert.Contains("<start>-<end>:<text>", refused.Error.Message);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await render.StartRender(endCardSeconds: 3)).Error!.Code);
        Assert.Empty(_blender.Requests);
    }

    [Fact]
    public async Task Settings_the_renderer_would_reject_are_refused_before_a_job_starts()
    {
        var (render, _) = await ToolsAsync();

        var odd = await render.StartRender(width: 641, outputKind: "video");
        var name = await render.StartRender(baseName: "../escape");

        Assert.Equal(ToolErrorCodes.InvalidArgument, odd.Error!.Code);
        Assert.Contains("output.width", odd.Error.Message);
        Assert.Equal(ToolErrorCodes.InvalidArgument, name.Error!.Code);
        Assert.Contains("output.baseName", name.Error.Message);
        Assert.Empty(_blender.Requests);
    }

    [Fact]
    public async Task Status_of_an_unknown_job_says_so()
    {
        var (render, status) = await ToolsAsync();

        Assert.Equal(ToolErrorCodes.JobNotFound, (await status.RenderStatus("nope")).Error!.Code);
        Assert.Equal(ToolErrorCodes.JobNotFound, (await render.CancelRender("nope")).Error!.Code);
    }

    // ---- export ---------------------------------------------------------------------------------

    [Fact]
    public async Task Exporting_writes_the_script_into_the_workspace_without_a_job()
    {
        var (render, status) = await ToolsAsync();

        var result = await render.ExportRenderScript("blender", model: "out/cave.3d", outputKind: "still");

        Assert.True(result.Ok, result.Error?.Message);
        Assert.Equal("blender/render.py", result.Data!.Script);
        Assert.Equal(("blender", "out/cave.3d", "externalFile", 1), (result.Data.Directory, result.Data.Model, result.Data.ModelKind, result.Data.Frames));
        Assert.True(File.Exists(_fixture.PathTo("blender", "render.py")));
        Assert.Equal(ToolErrorCodes.JobNotFound, (await status.RenderStatus()).Error!.Code);
    }

    [Theory]
    [InlineData("", ToolErrorCodes.InvalidArgument)]
    [InlineData("../scripts", ToolErrorCodes.PathOutsideWorkspace)]
    [InlineData(".thide", ToolErrorCodes.ProtectedPath)]
    public async Task Exporting_needs_a_folder_in_the_workspace(string directory, string code)
    {
        var (render, _) = await ToolsAsync();

        Assert.Equal(code, (await render.ExportRenderScript(directory, model: "out/cave.3d")).Error!.Code);
    }

    [Fact]
    public async Task Exporting_without_a_model_says_model_unavailable()
    {
        _blender.NoModel = true;
        var (render, _) = await ToolsAsync();

        Assert.Equal(ToolErrorCodes.ModelUnavailable, (await render.ExportRenderScript("blender")).Error!.Code);
    }

    // ---- registration ---------------------------------------------------------------------------

    [Fact]
    public void The_render_tools_come_only_with_a_blender_and_the_writers_only_in_the_full_profile()
    {
        Assert.DoesNotContain(typeof(BlenderStatusTools), TherionMcpCatalog.ToolTypes(McpProfile.Full, includeUi: true));
        Assert.DoesNotContain(typeof(BlenderRenderTools), TherionMcpCatalog.ToolTypes(McpProfile.Full, includeUi: true));

        var data = TherionMcpCatalog.ToolTypes(McpProfile.Data, includeUi: false, includeBlender: true);
        Assert.Contains(typeof(BlenderStatusTools), data);
        Assert.DoesNotContain(typeof(BlenderRenderTools), data);
        Assert.Contains(typeof(BlenderRenderTools), TherionMcpCatalog.ToolTypes(McpProfile.Full, includeUi: false, includeBlender: true));
    }

    [Fact]
    public async Task The_dispatcher_offers_the_render_tools_when_the_services_hold_a_blender()
    {
        var without = new ToolDispatcher(Services(blender: null));
        var with = new ToolDispatcher(Services(_blender));

        Assert.Null(without.Find("start_render"));
        Assert.True(with.Find("list_render_presets")!.ReadOnly);
        Assert.True(with.Find("render_status")!.ReadOnly);
        Assert.False(with.Find("start_render")!.ReadOnly);
        Assert.False(with.Find("cancel_render")!.ReadOnly);
        Assert.False(with.Find("export_render_script")!.ReadOnly);

        // Over the dispatcher's JSON binding, as a macro or `therion-mcp call` would call it.
        var call = await with.CallAsync("start_render", JsonSerializer.SerializeToElement(new { model = "out/cave.3d", width = 64, height = 64 }));
        Assert.True(call.Ok, call.Error);
        var jobId = call.Result.GetProperty("data").GetProperty("jobId").GetString();
        var status = await with.CallAsync("render_status", JsonSerializer.SerializeToElement(new { jobId, waitSeconds = 10 }));
        Assert.Equal("succeeded", status.Result.GetProperty("data").GetProperty("state").GetString());
    }

    private IServiceProvider Services(IBlenderRenderHost? blender)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(_fixture.Thconfig));
        // The stores are ours: the defaults would use the user's profile.
        services.AddSingleton<Workspace.IProjectMetadataStore>(new Workspace.ProjectMetadataStore(Path.Combine(_scratch, "metadata")));
        services.AddSingleton<Workspace.ILeadStatusStore>(new Workspace.LeadStatusStore(Path.Combine(_scratch, "leads")));
        if (blender is not null) services.AddSingleton(blender);
        services.AddToolServices();
        return services.BuildServiceProvider();
    }

    // ---- fakes ----------------------------------------------------------------------------------

    private sealed class FakeBlenderHost(string workspaceModel, string jobRoot) : IBlenderRenderHost
    {
        public FakeRenderer Renderer { get; } = new(jobRoot);
        IBlenderRenderService IBlenderRenderHost.Renderer => Renderer;
        public List<RenderPreset> Saved { get; } = [];
        public List<ModelSourceRequest> Requests { get; } = [];
        public bool NoModel { get; set; }
        public TaskCompletionSource? AcquireGate { get; set; }
        public bool PanelRendering { get; set; }
        public List<(string Name, bool? Succeeded, string? Error)> Ended { get; } = [];

        public bool TryBeginRender(string name) => !PanelRendering;

        public void EndRender(string name, RenderResult? result, string? error)
        {
            lock (Ended) Ended.Add((name, result?.Succeeded, error));
        }

        public IReadOnlyList<RenderPreset> UserPresets() => Saved;

        public async Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
        {
            lock (Requests) Requests.Add(request);
            if (AcquireGate is not null) await AcquireGate.Task.WaitAsync(ct);
            if (NoModel)
                throw new ModelSourceNotFoundException("No .lox/.3d model artifact was found for the workspace. Build the project (or enable re-export) first.");
            var model = request.ExternalFilePath is { } path
                ? new ResolvedModelSource { Path = path, Format = CaveSourceFormat.Survex3d, Kind = ModelSourceKind.ExternalFile }
                : new ResolvedModelSource
                {
                    Path = workspaceModel, Format = CaveSourceFormat.Survex3d, Kind = ModelSourceKind.WorkspaceArtifact,
                    IsFresh = false, StalenessReason = "The model is older than the sources.",
                };
            return new RenderSource(model);
        }
    }

    /// <summary>Writes one output where the spec says (or into a job folder), as Blender would.</summary>
    private sealed class FakeRenderer(string jobRoot) : IBlenderRenderService
    {
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RenderResult? Outcome { get; set; }
        public SceneSpec? LastSpec { get; private set; }
        public RenderOptions? LastOptions { get; private set; }
        public bool SawCancellation { get; private set; }

        public async Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, RenderOptions options,
            IProgress<RenderProgress>? progress = null, CancellationToken ct = default)
        {
            LastSpec = spec;
            LastOptions = options;
            progress?.Report(new RenderProgress(RenderPhase.Rendering, "Rendering", 0.5, 1, 2, Device: "CPU"));
            Reached.TrySetResult();
            if (Gate is not null)
            {
                try { await Gate.Task.WaitAsync(ct); }
                catch (OperationCanceledException)
                {
                    SawCancellation = true;
                    return new RenderResult { FailureKind = RenderFailureKind.Cancelled, ErrorMessage = "The render was cancelled." };
                }
            }
            if (Outcome is not null) return Outcome;

            var dir = spec.Output.OutputDirectory.Length > 0 ? spec.Output.OutputDirectory : Path.Combine(jobRoot, "job-1");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, spec.Output.BaseName + ".mp4");
            File.WriteAllBytes(file, [0]);
            return new RenderResult
            {
                Succeeded = true, OutputPaths = [file], Device = "CPU", FrameCount = 2, Warnings = ["a warning"],
            };
        }

        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir,
            IProgress<RenderProgress>? progress = null, CancellationToken ct = default, bool interactive = false)
        {
            Directory.CreateDirectory(outputDir);
            var script = Path.Combine(outputDir, "render.py");
            File.WriteAllText(script, "# render\n");
            return Task.FromResult(script);
        }
    }
}
