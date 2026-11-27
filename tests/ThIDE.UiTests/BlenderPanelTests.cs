using System.Diagnostics;

namespace ThIDE.UiTests;

/// <summary>
/// The Blender Animation panel, driven like a user drives it: open it from the Tools menu, pick
/// the workspace's model, set the output folder, render, and see the result. A stub Blender
/// (tests/ui-fixtures/stub-blender) stands in for the real one, so the flow runs wherever
/// Therion does; it keeps the scene plans and scripts it was given so the test can check what the
/// app made. The real-Blender variant runs when THIDE_BLENDER_SMOKE=1 as well.
/// </summary>
public class BlenderPanelTests
{
    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(d => File.Exists(Path.Combine(d, OperatingSystem.IsWindows() ? tool + ".exe" : tool)));

    /// <summary>The demo cave with a continuation lead on station 4.</summary>
    private static string DemoCave()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        var th = Path.Combine(dir, "demo.th");
        File.WriteAllText(th, File.ReadAllText(th).Replace(
            "  endcentreline", "    station 4 \"draught in the boulders\" continuation\n  endcentreline"));
        return dir;
    }

    /// <summary><see cref="DemoCave"/>, built with Therion.</summary>
    private static async Task<string> BuiltDemoCaveAsync()
    {
        var dir = DemoCave();
        var psi = new ProcessStartInfo("therion", "demo.thconfig")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var therion = Process.Start(psi)!;
        await therion.WaitForExitAsync();
        Assert.True(File.Exists(Path.Combine(dir, "demo.lox")), "Therion did not write demo.lox");
        return dir;
    }

    private static string StubBlenderDirectory()
    {
        var dir = ThideApp.CopyFixture("stub-blender"); // copying drops the executable bit
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(dir, "blender"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    /// <summary>Opens the panel, lets it pick the workspace's model, sends the output to
    /// <paramref name="renders"/> and shows the lead markers.</summary>
    private static async Task OpenPanelAsync(ThideApp app, string renders)
    {
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
        await app.MenuAsync("Tools > Blender Animation…");
        await app.WaitAsync("#Blender.Render");
        await app.InvokeAsync("#Blender.ReloadArtifacts");
        // Never the default folder, which is in the real home.
        await app.SetTextAsync("#Blender.RenderDirectory", renders);
        if (!await app.ExistsAsync("#Blender.LeadMarkers;state:checked"))
            await app.InvokeAsync("#Blender.LeadMarkers", "toggle");
        await app.WaitAsync("#Blender.LeadMarkers;state:checked");
        await app.WaitAsync("#Blender.Render;state:enabled");
    }

    [UiFact]
    public async Task Render_with_a_stub_Blender_writes_the_output_and_marks_the_workspace_lead()
    {
        if (!OnPath("therion")) return; // the panel needs a built model
        var dir = await BuiltDemoCaveAsync();
        var renders = Path.Combine(dir, "renders");
        var scripts = Path.Combine(dir, "stub-scripts");
        var environment = new Dictionary<string, string>
        {
            // The stub reports version 99, so it wins over any real Blender on the PATH.
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, renders);
        await app.InvokeAsync("#Blender.Render");

        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 60_000);
        var written = Directory.GetFiles(renders);
        Assert.Single(written);
        await app.WaitAsync("#Blender.Outputs >> role:Text;name:" + written[0]);

        // The scene was planned for the player, and the lead flagged in demo.th is in the plan,
        // named by its station. The preset the panel opens on writes the cave's name over the
        // picture: the name the model has, in place of the preset's placeholder, which makes it
        // a render in two stages (the frames, then the movie from them), each given the plan.
        var plans = Directory.GetFiles(scripts);
        Assert.Equal(2, plans.Length);
        Assert.All(plans, path =>
        {
            var plan = File.ReadAllText(path);
            Assert.Contains("\"features\": [", plan);
            Assert.Contains("\"id\": \"lead:demo.4\", \"role\": \"lead-marker\"", plan);
            Assert.Contains("\"role\": \"title\", \"kind\": \"text\"", plan);
            Assert.Contains("\"text\": \"Invented demo cave\"", plan);
            Assert.DoesNotContain("{cave}", plan);
        });
    }

    [UiFact]
    public async Task A_failed_render_keeps_its_log_in_the_temp_folder_the_test_gave_the_app()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_FAIL"] = "1",
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, Path.Combine(dir, "renders"));
        await app.InvokeAsync("#Blender.Render");
        await app.WaitAsync("role:Text;name:Render failed.", timeoutMs: 60_000);

        // The app's own account of the job. Its folder is kept, for the log, and is under the temp
        // folder this app was given: not under the machine's, where a ThIDE the developer has open
        // keeps its jobs (and clears out old ones).
        var jobRoot = Path.Combine(app.TempDirectory, "ThIDE", "blender-jobs");
        var jobs = (await app.GetAsync("#Blender.Builder", "DataContext.Blender.Jobs"))
            .GetProperty("properties").GetProperty("DataContext.Blender.Jobs").GetString()!;
        var log = System.Text.RegularExpressions.Regex.Match(jobs, @"JobLogPath = (.+?), Duration = ").Groups[1].Value;
        Assert.True(log.StartsWith(Path.Combine(jobRoot, "job-"), StringComparison.Ordinal), jobs);
        Assert.True(File.Exists(log), log);
    }

    [UiFact]
    public async Task A_title_and_captions_typed_into_the_panel_are_written_over_the_movie_and_as_subtitles()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var renders = Path.Combine(dir, "renders");
        var scripts = Path.Combine(dir, "stub-scripts");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, renders);

        // The preset's title is the cave's name, by its placeholder; no captions, and no strip.
        Assert.Equal("{cave}", await app.TextOfAsync("#Blender.Title"));
        Assert.False(await app.ExistsAsync("#Blender.Caption.1.Text"));
        await app.SetTextAsync("#Blender.Title", "Demo: {cave}");

        // Three captions, each after the one before; the second is taken out again.
        await app.InvokeAsync("#Blender.AddCaption");
        await app.WaitAsync("#Blender.Caption.1.Text");
        await app.SetTextAsync("#Blender.Caption.1.Text", "The entrance of {cave}");
        await app.SetTextAsync("#Blender.Caption.1.End", "2");
        await app.InvokeAsync("#Blender.AddCaption");
        await app.WaitAsync("#Blender.Caption.2.Text");
        await app.SetTextAsync("#Blender.Caption.2.Text", "not this one");
        await app.InvokeAsync("#Blender.AddCaption");
        await app.WaitAsync("#Blender.Caption.3.Text");
        await app.SetTextAsync("#Blender.Caption.3.Text", "The pitch");
        await app.SetTextAsync("#Blender.Caption.3.Start", "4");
        await app.SetTextAsync("#Blender.Caption.3.End", "6");
        await app.WaitAsync("#Blender.CaptionStrip");
        await app.InvokeAsync("#Blender.Caption.2.Remove");
        await app.WaitAsync("#Blender.Caption.3.Text", gone: true);
        Assert.Equal("The pitch", await app.TextOfAsync("#Blender.Caption.2.Text"));
        Assert.Equal("4", await app.TextOfAsync("#Blender.Caption.2.Start"));
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "blender-panel-captions.png"), await app.ScreenshotAsync("#Blender.Captions"));
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "blender-panel-caption-strip.png"), await app.ScreenshotAsync("#Blender.CaptionStrip"));

        await app.WaitAsync("#Blender.Render;state:enabled");
        await app.InvokeAsync("#Blender.Render");
        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 60_000);

        // The movie, and its captions as subtitles beside it, with the cave's name filled in.
        Assert.Equal(
            ["cave-render.mp4", "cave-render.srt", "cave-render.vtt"],
            Directory.GetFiles(renders).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            "1\n00:00:00,000 --> 00:00:02,000\nThe entrance of Invented demo cave\n\n2\n00:00:04,000 --> 00:00:06,000\nThe pitch\n\n",
            File.ReadAllText(Path.Combine(renders, "cave-render.srt")));
        // And in the plan both stages were given: the title, and each caption for its time.
        var plans = Directory.GetFiles(scripts);
        Assert.Equal(2, plans.Length);
        Assert.All(plans, path =>
        {
            var plan = File.ReadAllText(path);
            Assert.Contains("\"text\": \"Demo: Invented demo cave\"", plan);
            Assert.Contains("\"role\": \"caption\"", plan);
            Assert.Contains("\"text\": \"The entrance of Invented demo cave\"", plan);
            Assert.Contains("\"text\": \"The pitch\"", plan);
            Assert.DoesNotContain("not this one", plan);
        });
    }

    [UiFact]
    public async Task Preview_writes_a_draft_and_the_script_generator_can_still_be_chosen()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var renders = Path.Combine(dir, "renders");
        var scripts = Path.Combine(dir, "stub-scripts");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, renders);
        await app.WaitAsync("#Blender.Preview;state:enabled");

        // A preview is a draft of the same scene, under its own name beside where the render goes.
        await app.SetTextAsync("#Blender.Credits", "Survey: the demo team");
        await app.InvokeAsync("#Blender.Preview");
        await app.WaitAsync("role:Text;name:Preview complete.", timeoutMs: 60_000);
        var draft = Assert.Single(Directory.GetFiles(renders));
        Assert.Equal("cave-render-draft.mp4", Path.GetFileName(draft));
        // The credits line makes it a render in two stages: the frames, then the movie from them.
        var plans = Directory.GetFiles(scripts).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, plans.Length);
        Assert.All(plans, p =>
        {
            var plan = File.ReadAllText(p);
            Assert.Contains("\"engine\": \"workbench\"", plan);
            Assert.Contains("\"text\": \"Survey: the demo team\"", plan);
        });

        // The earlier builder, chosen by name, is given a generated script.
        await app.KeyAsync("#Blender.Builder", "Down", "Down");
        Assert.Equal("Generator", (await app.GetAsync("#Blender.Builder", "DataContext.Blender.Builder")).GetProperty("properties").GetProperty("DataContext.Blender.Builder").ToString());
        await app.InvokeAsync("#Blender.Render");
        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 60_000);
        Assert.Contains(Path.Combine(renders, "cave-render.mp4"), Directory.GetFiles(renders));
        var script = File.ReadAllText(Assert.Single(Directory.GetFiles(scripts, "render-*.py")));
        // The generated script draws the title in the scene: the cave's name, not the placeholder.
        Assert.Contains("\"Invented demo cave\"", script);
        Assert.DoesNotContain("{cave}", script);
        Assert.Contains("LEAD_MARKERS = [", script);
        Assert.Contains("\"draught in the boulders\"", script);
        Assert.Contains("\"demo.4\"", script);
    }

    [UiFact]
    public async Task The_script_generators_self_contained_script_carries_the_meshes_only_when_asked_to()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var exports = Path.Combine(dir, "exports");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, Path.Combine(dir, "renders"));

        // The meshes can go into the script only when it is self-contained, and only the script
        // generator's: the player's one file always carries them.
        await app.WaitAsync("#Blender.EmbedMesh");
        Assert.False(await app.ExistsAsync("#Blender.SelfContained;state:checked"));
        Assert.False(await app.ExistsAsync("#Blender.EmbedMesh;state:enabled"));
        await app.InvokeAsync("#Blender.SelfContained", "toggle");
        await app.WaitAsync("#Blender.SelfContained;state:checked");
        Assert.False(await app.ExistsAsync("#Blender.EmbedMesh;state:enabled"));
        await app.KeyAsync("#Blender.Builder", "Down", "Down");
        Assert.Equal("Generator", (await app.GetAsync("#Blender.Builder", "DataContext.Blender.Builder")).GetProperty("properties").GetProperty("DataContext.Blender.Builder").ToString());
        await app.WaitAsync("#Blender.EmbedMesh;state:enabled");
        Assert.False(await app.ExistsAsync("#Blender.EmbedMesh;state:checked"));

        async Task<string> ExportAsync(string name)
        {
            string folder = Path.Combine(exports, name);
            await app.SetTextAsync("#Blender.ExportDirectory", folder);
            await app.WaitAsync("#Blender.ExportScript;state:enabled");
            await app.InvokeAsync("#Blender.ExportScript");
            await app.WaitAsync("#Blender.Outputs >> role:Text;name:" + Path.Combine(folder, "render.py"), timeoutMs: 60_000);
            return File.ReadAllText(Path.Combine(folder, "render.py"));
        }

        // Self-contained, as before: the scene's data inside, the mesh read from beside it.
        string beside = await ExportAsync("beside");
        Assert.Contains("SCENE_META_JSON = ", beside);
        Assert.DoesNotContain("PLY_BASE64 = ", beside);

        // The meshes inside as well.
        await app.InvokeAsync("#Blender.EmbedMesh", "toggle");
        await app.WaitAsync("#Blender.EmbedMesh;state:checked");
        string inside = await ExportAsync("inside");
        Assert.Contains("SCENE_META_JSON = ", inside);
        Assert.Contains("PLY_BASE64 = ", inside);

        // Not self-contained: the second option goes off with the first.
        await app.InvokeAsync("#Blender.SelfContained", "toggle");
        for (int tries = 0; await app.ExistsAsync("#Blender.EmbedMesh;state:enabled") && tries < 50; tries++)
            await Task.Delay(100);
        Assert.False(await app.ExistsAsync("#Blender.EmbedMesh;state:enabled"));
        Assert.False(await app.ExistsAsync("#Blender.EmbedMesh;state:checked"));
        string plain = await ExportAsync("plain");
        Assert.DoesNotContain("SCENE_META_JSON = ", plain);
        Assert.DoesNotContain("PLY_BASE64 = ", plain);
    }

    /// <summary>A Radiance picture of 4 x 2 pixels of one color: the smallest image a scene
    /// can be lit by.</summary>
    private static byte[] TinySky()
    {
        var head = System.Text.Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 4\n");
        return [.. head, .. Enumerable.Repeat(new byte[] { 128, 128, 128, 129 }, 8).SelectMany(pixel => pixel)];
    }

    [UiFact]
    public async Task The_light_the_reveal_the_annotations_and_the_texts_set_in_the_panel_are_in_the_scene_that_is_rendered()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var renders = Path.Combine(dir, "renders");
        var scripts = Path.Combine(dir, "stub-scripts");
        Directory.CreateDirectory(Path.Combine(dir, "extras"));
        var sky = Path.Combine(dir, "extras", "evening sky.hdr");
        File.WriteAllBytes(sky, TinySky());
        var notes = Path.Combine(dir, "extras", "notes.json");
        File.WriteAllText(notes, """
            [
              { "id": "way-in", "kind": "callout", "station": "0", "text": "The way in" },
              { "kind": "marker", "station": "5" }
            ]
            """);
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        await OpenPanelAsync(app, renders);

        // The light: an image all round the scene, turned a quarter. Its file can be named
        // only once its rig is chosen (the last in the list).
        await app.WaitAsync("#Blender.HdriRotation");
        Assert.False(await app.ExistsAsync("#Blender.HdriPath;state:enabled"));
        await app.KeyAsync("#Blender.LightRig", "Down", "Down", "Down");
        Assert.Equal("HdriFile", (await app.GetAsync("#Blender.LightRig", "DataContext.Blender.LightRig")).GetProperty("properties").GetProperty("DataContext.Blender.LightRig").ToString());
        await app.WaitAsync("#Blender.HdriPath;state:enabled");
        // No image yet: the render waits for one.
        for (int tries = 0; await app.ExistsAsync("#Blender.Render;state:enabled") && tries < 50; tries++)
            await Task.Delay(100);
        Assert.False(await app.ExistsAsync("#Blender.Render;state:enabled"));
        await app.SetTextAsync("#Blender.HdriPath", sky);
        await app.SetTextAsync("#Blender.HdriRotation", "90");
        await app.SetTextAsync("#Blender.LightStrength", "1.5");
        await app.WaitAsync("#Blender.Render;state:enabled");

        // The reveal: off until one is chosen, then its fields are there.
        Assert.False(await app.ExistsAsync("#Blender.RevealFrom"));
        await app.KeyAsync("#Blender.Reveal", "Down");
        await app.WaitAsync("#Blender.RevealFrom;state:enabled");
        Assert.False(await app.ExistsAsync("#Blender.RevealDates;state:enabled"));
        await app.SetTextAsync("#Blender.RevealFrom", "0");
        await app.SetTextAsync("#Blender.RevealStart", "1");
        Assert.False(await app.ExistsAsync("#Blender.RevealEnd;state:enabled"));
        await app.InvokeAsync("#Blender.RevealToClipEnd", "toggle");
        await app.WaitAsync("#Blender.RevealEnd;state:enabled");
        await app.SetTextAsync("#Blender.RevealEnd", "6");
        await app.SetTextAsync("#Blender.RevealEdge", "0.1");

        // The annotations: none, then those of the file, counted; a file that is not one says so.
        Assert.Equal("No annotations.", await app.TextOfAsync("#Blender.AnnotationsSummary"));
        Assert.False(await app.ExistsAsync("#Blender.ClearAnnotations;state:enabled"));
        await app.SetTextAsync("#Blender.AnnotationsPath", notes);
        await app.InvokeAsync("#Blender.LoadAnnotations");
        await app.WaitAsync("#Blender.ClearAnnotations;state:enabled");
        Assert.Equal("2 annotations: labels 0, callouts 1, markers 1, photos 0", await app.TextOfAsync("#Blender.AnnotationsSummary"));
        await app.SetTextAsync("#Blender.AnnotationsPath", Path.Combine(dir, "demo.th"));
        await app.InvokeAsync("#Blender.LoadAnnotations");
        await app.WaitAsync("#Blender.AnnotationProblems >> role:Text");
        Assert.Equal("2 annotations: labels 0, callouts 1, markers 1, photos 0", await app.TextOfAsync("#Blender.AnnotationsSummary"));

        // The texts that are switched on, and a subtitle under the preset's title.
        await app.SetTextAsync("#Blender.Subtitle", "surveyed in a test");
        await app.InvokeAsync("#Blender.ScaleBar", "toggle");
        await app.InvokeAsync("#Blender.NorthArrow", "toggle");
        await app.WaitAsync("#Blender.NorthArrow;state:checked");
        await app.SetTextAsync("#Blender.FadeFromBlack", "0.5");
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "blender-panel-scene-controls.png"), await app.ScreenshotAsync("#Blender.Panel"));

        await app.WaitAsync("#Blender.Render;state:enabled");
        await app.InvokeAsync("#Blender.Render");
        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 60_000);

        // All of it is in the plan both stages were given.
        var plans = Directory.GetFiles(scripts);
        Assert.Equal(2, plans.Length);
        Assert.All(plans, path =>
        {
            using var plan = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var root = plan.RootElement;
            var world = root.GetProperty("look").GetProperty("world");
            Assert.Equal(("hdri/evening_sky.hdr", 1.5, 90.0),
                (world.GetProperty("image").GetString(), world.GetProperty("strength").GetDouble(), world.GetProperty("rotation").GetDouble()));
            Assert.Equal(["way-in", "annotation-2"], root.GetProperty("annotations").EnumerateArray().Select(a => a.GetProperty("id").GetString()));
            Assert.Contains(root.GetProperty("changes").EnumerateArray(), change => change.TryGetProperty("reveal", out _));
            var texts = root.GetProperty("texts").GetProperty("items").EnumerateArray().ToList();
            Assert.Contains(texts, t => t.TryGetProperty("text", out var text) && text.GetString() == "surveyed in a test");
            Assert.Contains(texts, t => t.GetProperty("role").GetString() == "scale-bar");
            Assert.Contains(texts, t => t.GetProperty("role").GetString() == "north-arrow");
            Assert.Equal(0.5, root.GetProperty("texts").GetProperty("fromBlack").GetDouble());
        });
    }

    [UiFact]
    public async Task Export_and_Open_in_Blender_give_the_scene_that_Render_shows()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        var exports = Path.Combine(dir, "exports");
        var scripts = Path.Combine(dir, "stub-scripts");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };

        await using var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig");
        var renders = Path.Combine(dir, "renders");
        await OpenPanelAsync(app, renders);
        await app.SetTextAsync("#Blender.Credits", "Survey: the demo team");

        async Task<string> ExportAsync(string name)
        {
            string folder = Path.Combine(exports, name);
            await app.SetTextAsync("#Blender.ExportDirectory", folder);
            await app.WaitAsync("#Blender.ExportScript;state:enabled");
            await app.InvokeAsync("#Blender.ExportScript");
            await app.WaitAsync("#Blender.Outputs >> role:Text;name:" + Path.Combine(folder, "render.py"), timeoutMs: 60_000);
            return folder;
        }

        // The export is the player's folder: the scene as a plan, with the texts only the
        // player writes and the lead of the workspace, and everything to render it with.
        string folder = await ExportAsync("folder");
        foreach (string file in new[] { "README.txt", "render.sh", "render.cmd", "render.py", "scene-plan.json", "model.ply", "LICENSE", Path.Combine("thide_player", "core.py") })
            Assert.True(File.Exists(Path.Combine(folder, file)), file);
        string exported = File.ReadAllText(Path.Combine(folder, "scene-plan.json"));
        Assert.Contains("\"text\": \"Invented demo cave\"", exported);
        Assert.Contains("\"text\": \"Survey: the demo team\"", exported);
        Assert.Contains("\"id\": \"lead:demo.4\", \"role\": \"lead-marker\"", exported);
        Assert.Contains("\"directory\": \"out\"", exported);

        // One file: the same plan inside one script, and nothing beside it.
        await app.InvokeAsync("#Blender.SelfContained", "toggle");
        await app.WaitAsync("#Blender.SelfContained;state:checked");
        string one = await ExportAsync("one");
        Assert.Equal(["render.py"], Directory.GetFileSystemEntries(one).Select(Path.GetFileName));
        string script = File.ReadAllText(Path.Combine(one, "render.py"));
        Assert.Contains("PLAN = r'''", script);
        Assert.Contains("\"model.ply\": \"", script);
        Assert.Contains("\"text\": \"Survey: the demo team\"", script);
        // Exporting starts no Blender.
        Assert.False(Directory.Exists(scripts));

        // "Open in Blender" starts Blender on the same scene, to build it and stop; it says
        // that the texts are not in the window, once.
        await app.InvokeAsync("#Blender.OpenInBlender");
        await app.WaitAsync("role:Text;name:Opened in Blender.", timeoutMs: 60_000);
        await app.WaitAsync("#Blender.Warnings >> role:Text;name:The texts over the picture are not in Blender's window: the player writes them over the finished frames, after the scene is rendered.");
        for (int tries = 0; !Directory.Exists(scripts) && tries < 100; tries++) await Task.Delay(100);
        string opened = File.ReadAllText(Assert.Single(Directory.GetFiles(scripts)));
        Assert.StartsWith("built-plan-", Path.GetFileName(Directory.GetFiles(scripts)[0]));
        Assert.Contains("\"text\": \"Survey: the demo team\"", opened);
        Assert.Contains("\"id\": \"lead:demo.4\", \"role\": \"lead-marker\"", opened);
        // A render started in Blender's window would write where "Render" does; nothing was rendered.
        Assert.Contains(System.Text.Json.JsonSerializer.Serialize(renders), opened);
        Assert.False(Directory.Exists(renders) && Directory.GetFiles(renders).Length > 0);

        // The scene is the one "Render" gives Blender: the same plan but for its output folder.
        await app.InvokeAsync("#Blender.Render");
        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 60_000);
        string rendered = File.ReadAllText(Directory.GetFiles(scripts, "scene-plan-*.json").First());
        Assert.Equal(rendered, opened);
        static string WithoutFolder(string plan) => System.Text.RegularExpressions.Regex.Replace(plan, "\"directory\": \"[^\"]*\"", "\"directory\": \"\"");
        Assert.Equal(WithoutFolder(rendered), WithoutFolder(exported));
    }

    [UiFact]
    public async Task Render_of_a_project_never_built_exports_the_model_outside_the_project()
    {
        if (!OnPath("therion")) return;
        var dir = DemoCave();
        var before = Directory.GetFileSystemEntries(dir).Order(StringComparer.Ordinal).ToArray();
        var renders = ThideApp.RemoveAtExit(Path.Combine(Path.GetTempPath(), "thide-ui-reexport-" + Guid.NewGuid().ToString("N")));
        var scripts = Path.Combine(renders, "stub-scripts");
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = StubBlenderDirectory() + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["THIDE_STUB_BLENDER_SCRIPTS"] = scripts,
        };
        try
        {
            await using (var app = await ThideApp.StartAsync(environment, dir, "demo.thconfig"))
            {
                await OpenPanelAsync(app, Path.Combine(renders, "out"));
                await app.InvokeAsync("#Blender.Render");
                await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 120_000);
            }

            // The model came from a re-export: the lead is marked, and the project folder holds
            // nothing it did not hold before (no model, no temporary thconfig, no log).
            // (Two stages, for the cave's name over the picture: the same plan for each.)
            var plan = File.ReadAllText(Directory.GetFiles(scripts).Order(StringComparer.Ordinal).First());
            Assert.Contains("\"lead:demo.4\"", plan);
            var after = Directory.GetFileSystemEntries(dir).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(before, after);
        }
        finally
        {
            try { Directory.Delete(renders, recursive: true); } catch { }
        }
    }

    [UiFact]
    public async Task The_storyboard_tab_lists_the_shots_of_a_composite_moves_and_removes_them()
    {
        var dir = DemoCave();
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
        await app.MenuAsync("Tools > Blender Animation…");
        await app.WaitAsync("#Blender.Render");
        await app.SetTextAsync("#Blender.RenderDirectory", Path.Combine(dir, "renders"));
        await app.SetTextAsync("#Blender.Title", "Invented scene");

        // The scene's own buttons give way to the film's.
        await app.InvokeAsync("#Blender.Tab.Storyboard", "select");
        await app.WaitAsync("#Blender.AddComposite");
        Assert.False(await app.ExistsAsync("#Blender.Render;state:showing"));
        // With no shot selected, what a shot holds cannot be edited; the film's settings can.
        await app.WaitAsync("#Blender.ChooseShotHint;state:showing");
        Assert.False(await app.ExistsAsync("#Blender.Samples;state:enabled"));
        Assert.True(await app.ExistsAsync("#Blender.Width;state:enabled"));
        await app.InvokeAsync("#Blender.AddComposite");
        await app.WaitAsync("#Blender.Shot.3.Name");
        Assert.Equal(["Outside", "Main passage", "Way out"],
            [await app.TextOfAsync("#Blender.Shot.1.Name"), await app.TextOfAsync("#Blender.Shot.2.Name"), await app.TextOfAsync("#Blender.Shot.3.Name")]);
        await app.WaitAsync("#Blender.ShotStrip");

        // Selected, a shot is what the settings edit: the helix of "Way out".
        await app.InvokeAsync("#Blender.Shot.3.Select");
        await app.WaitAsync("#Blender.DraftShot;state:enabled");
        Assert.Contains("Helix", await app.TextOfAsync("#Blender.CameraTemplate"), StringComparison.Ordinal);
        await app.WaitAsync("#Blender.Samples;state:enabled");
        Assert.NotEqual("Invented scene", await app.TextOfAsync("#Blender.Title"));

        await app.InvokeAsync("#Blender.Shot.3.Up");
        await app.WaitAsync("#Blender.Shot.2.Name");
        Assert.Equal("Way out", await app.TextOfAsync("#Blender.Shot.2.Name"));
        await app.InvokeAsync("#Blender.Shot.1.Remove");
        await app.WaitAsync("#Blender.Shot.3.Name", gone: true);
        Assert.Equal("Way out", await app.TextOfAsync("#Blender.Shot.1.Name"));
        Assert.True(await app.ExistsAsync("#Blender.DraftAll"));
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "blender-panel-storyboard.png"), await app.ScreenshotAsync("#Blender.Storyboard"));

        await app.InvokeAsync("#Blender.Tab.Scene", "select");
        await app.WaitAsync("#Blender.Render;state:showing");
        // The scene is as it was left: the film did not change it.
        Assert.Equal("Invented scene", await app.TextOfAsync("#Blender.Title"));
        Assert.True(await app.ExistsAsync("#Blender.Samples;state:enabled"));
    }

    [UiFact]
    public async Task The_storyboard_tab_edits_the_films_texts_saves_a_composite_and_lists_a_tours_branches()
    {
        if (!OnPath("therion")) return;
        var dir = await BuiltDemoCaveAsync();
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await OpenPanelAsync(app, Path.Combine(dir, "renders"));
        await app.InvokeAsync("#Blender.Tab.Storyboard", "select");
        await app.WaitAsync("#Blender.AddComposite");

        // The Grand tour: an orbit, then a tour of the branches.
        // The built-in composites come first: Showcase, then the Grand tour.
        await app.KeyAsync("#Blender.Composite", "Down");
        Assert.Contains("Grand tour", await app.TextOfAsync("#Blender.Composite"), StringComparison.Ordinal);
        await app.InvokeAsync("#Blender.AddComposite");
        await app.WaitAsync("#Blender.Shot.2.Name");
        Assert.Equal("{cave}", await app.TextOfAsync("#Blender.FilmTitle"));

        // The tour shot lists its side passages, worked out from the model.
        await app.InvokeAsync("#Blender.Shot.2.Select");
        await app.WaitAsync("#Blender.ListBranches;state:enabled");
        await app.InvokeAsync("#Blender.ListBranches");
        await app.WaitAsync("#Blender.Branch.1.Visit");
        Assert.False(string.IsNullOrEmpty(await app.TextOfAsync("#Blender.TourSummary")));
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "blender-panel-branches.png"), await app.ScreenshotAsync("#Blender.Storyboard"));

        // The film's texts, and the film kept as a composite that the list then offers.
        await app.SetTextAsync("#Blender.FilmTitle", "Invented title");
        await app.SetTextAsync("#Blender.FilmEndCard", "Invented end card");
        await app.SetTextAsync("#Blender.CompositeName", "Invented film");
        await app.WaitAsync("#Blender.SaveComposite;state:enabled");
        await app.InvokeAsync("#Blender.SaveComposite");
        await app.WaitAsync("role:Text;name~:Invented film");
        Assert.Contains("Invented film", await app.TextOfAsync("#Blender.Composite"), StringComparison.Ordinal);
        Assert.Equal("", await app.TextOfAsync("#Blender.CompositeName"));
    }

    [UiFact]
    public async Task Render_with_the_real_Blender_writes_a_video()
    {
        if (!OnPath("therion") || !OnPath("blender")) return;
        if (Environment.GetEnvironmentVariable("THIDE_BLENDER_SMOKE") != "1") return; // seconds of CPU rendering
        var dir = await BuiltDemoCaveAsync();
        var renders = Path.Combine(dir, "renders");

        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await OpenPanelAsync(app, renders);
        await app.SetTextAsync("#Blender.Width", "96");
        await app.SetTextAsync("#Blender.Height", "64");
        await app.SetTextAsync("#Blender.Samples", "1");
        await app.SetTextAsync("#Blender.Fps", "4");
        await app.SetTextAsync("#Blender.Duration", "0.5");
        await app.InvokeAsync("#Blender.Render");

        await app.WaitAsync("role:Text;name:Render complete.", timeoutMs: 300_000);
        var video = Assert.Single(Directory.GetFiles(renders, "*.mp4"));
        Assert.True(new FileInfo(video).Length > 0);
    }
}
