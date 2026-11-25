// Render bundles without Blender: what the zip holds, that it is the same bytes every time, that
// its plans are valid and its paths stay inside it, what the run scripts pass to Blender (through a
// stand-in for Blender), and what the README says. RenderBundleBlenderTests renders one.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;

namespace Therion.Blender.Tests.Player;

public class RenderBundleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"thide-bundle-{Guid.NewGuid():N}");

    public RenderBundleTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static SceneSpec Orbit => PlanFixtures.Preset("Orbit showcase") with
    {
        Name = "Invented Small Cave",
        Output = new OutputSpec { Kind = OutputKind.Video, Width = 320, Height = 180, BaseName = "clip" },
    };

    /// <summary>The invented small cave written as a .lox file, as a model source.</summary>
    private ResolvedModelSource SmallCave()
    {
        string lox = Path.Combine(_dir, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
    }

    private RenderBundleContent Content(SceneSpec? spec = null, bool draft = true, string work = "work")
        => RenderBundleWriter.FromModel(SmallCave(), spec ?? Orbit, Path.Combine(_dir, work), draft);

    /// <summary>The entries of a zip, by their paths inside its one folder, which is named after
    /// the zip.</summary>
    private static Dictionary<string, byte[]> Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        string top = Path.GetFileNameWithoutExtension(zipPath) + "/";
        Assert.All(zip.Entries, e => Assert.StartsWith(top, e.FullName, StringComparison.Ordinal));
        return zip.Entries.ToDictionary(e => e.FullName[top.Length..], e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    private static string Text(Dictionary<string, byte[]> entries, string path) => Encoding.UTF8.GetString(entries[path]);

    // ---- what is in it ----

    [Fact]
    public void TheZip_HoldsThePlans_TheMesh_ThePlayer_TheScripts_AndTheReadme_InOrdinalOrder_InOneFolder()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        var written = RenderBundleWriter.WriteZip(Content(), zipPath);

        string[] inside =
        [
            "LICENSE", "NOTICE", "README.txt", "model.ply", "render.cmd", "render.py", "render.sh", "scene-plan.draft.json", "scene-plan.json",
            .. PlayerFiles.JobFiles.Select(f => f.Path).Where(p => p.StartsWith("thide_player/", StringComparison.Ordinal)),
        ];
        Array.Sort(inside, StringComparer.Ordinal);
        string[] expected = [.. inside.Select(p => "cave/" + p)];
        Assert.Equal(expected, written);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(expected, zip.Entries.Select(e => e.FullName));

        // The player and the mesh as they are.
        var entries = Read(zipPath);
        Assert.All(PlayerFiles.JobFiles, f => Assert.Equal(f.Text, Text(entries, f.Path)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, "work", "model.ply")), entries["model.ply"]);
    }

    [Fact]
    public void TheZip_CarriesThePlayersLicence_AndANoticeOfWhereItsSourceIs()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(draft: false), zipPath);
        var entries = Read(zipPath);

        // The licence is the text of the Mozilla Public License 2.0, as the library ships it.
        string licence = Text(entries, RenderBundleWriter.LicenseFileName);
        Assert.StartsWith("Mozilla Public License Version 2.0\n", licence);
        Assert.Contains("Exhibit A - Source Code Form License Notice", licence);
        Assert.Contains("Exhibit B - \"Incompatible With Secondary Licenses\" Notice", licence);
        Assert.DoesNotContain('\r', licence);
        Assert.EndsWith("  defined by the Mozilla Public License, v. 2.0.\n", licence);

        string notice = Text(entries, RenderBundleWriter.NoticeFileName);
        Assert.Contains("render.py and the folder thide_player/ are the ThIDE player", notice);
        Assert.Contains("Mozilla Public License, v. 2.0: see LICENSE beside this file", notice);
        Assert.Contains("https://github.com/apgeo/ThIDE", notice);
        Assert.DoesNotContain('\r', notice);

        string readme = Text(entries, RenderBundleWriter.ReadmeFileName);
        Assert.Contains("  LICENSE, NOTICE ", readme);
        Assert.Contains("the player's licence (MPL-2.0), and where its source is", readme);
        Assert.Contains("Mozilla Public License 2.0 (LICENSE)", readme);
        Assert.Contains("source is in the ThIDE repository: https://github.com/apgeo/ThIDE\n", readme);
    }

    [Fact]
    public void WithoutADraft_ThereIsNoDraftPlan()
    {
        var content = Content(draft: false);
        string zipPath = Path.Combine(_dir, "cave.zip");
        var written = RenderBundleWriter.WriteZip(content, zipPath);

        Assert.Null(content.Draft);
        Assert.DoesNotContain("cave/" + RenderBundleWriter.DraftPlanFileName, written);
        Assert.Contains("cave/" + ScenePlanSerializer.FileName, written);
        Assert.DoesNotContain("--draft", Text(Read(zipPath), "README.txt"));
    }

    [Fact]
    public void TheSameInputUnderTheSameName_GivesTheSameBytes()
    {
        // Two conversions into two folders, two zips of one name in two folders, a pause between them.
        string first = Path.Combine(_dir, "a", "cave.zip"), second = Path.Combine(_dir, "b", "cave.zip");
        RenderBundleWriter.WriteZip(Content(work: "a"), first);
        Thread.Sleep(1100);
        RenderBundleWriter.WriteZip(Content(work: "b"), second);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));

        using var stream = new MemoryStream();
        RenderBundleWriter.WriteZip(Content(work: "c"), stream, "cave");
        Assert.Equal(File.ReadAllBytes(first), stream.ToArray());

        using var zip = ZipFile.OpenRead(first);
        Assert.All(zip.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));

        // Another name is another folder, and the same files in it.
        string other = Path.Combine(_dir, "other.zip");
        RenderBundleWriter.WriteZip(Content(work: "d"), other);
        Assert.NotEqual(File.ReadAllBytes(first), File.ReadAllBytes(other));
        var (one, two) = (Read(first), Read(other));
        Assert.Equal(one.Keys, two.Keys);
        Assert.All(one, e => Assert.Equal(e.Value, two[e.Key]));
    }

    [Theory]
    [InlineData("cave-bundle.zip", "cave-bundle")]
    [InlineData("/somewhere/Peștera Mare 2026.zip", "Peștera Mare 2026")]
    [InlineData("a<b>c:d\"e|f?g*h.zip", "a_b_c_d_e_f_g_h")]
    [InlineData("tabs\tand\nlines.zip", "tabs_and_lines")]
    [InlineData("trailing dots... .zip", "trailing dots")]
    [InlineData("con.zip", "con_")]
    [InlineData("LPT1.final.zip", "LPT1_.final")]
    [InlineData("Console.zip", "Console")]
    [InlineData("....zip", "render-bundle")]
    [InlineData(".zip", "render-bundle")]
    public void TheFolder_IsNamedAfterTheZip_AsEveryFileSystemTakesIt(string zipPath, string folder)
        => Assert.Equal(folder, RenderBundleWriter.FolderName(zipPath));

    [Fact]
    public void ThePlans_AreValid_AndRenderIntoTheBundle()
    {
        var content = Content();
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(content, zipPath);
        var entries = Read(zipPath);

        foreach (string name in new[] { ScenePlanSerializer.FileName, RenderBundleWriter.DraftPlanFileName })
        {
            string json = Text(entries, name);
            Assert.Empty(JsonSchemaSubset.Validate(ScenePlanSchema.Text, json));
            var plan = ScenePlanSerializer.Read(json);
            Assert.Empty(ScenePlanValidator.Validate(plan));
            Assert.Equal(RenderBundleWriter.OutputDirectory, plan.Output.Directory);
            Assert.All(plan.Models, m => Assert.Contains(m.Path, entries.Keys));
        }

        var full = ScenePlanSerializer.Read(Text(entries, ScenePlanSerializer.FileName));
        var draft = ScenePlanSerializer.Read(Text(entries, RenderBundleWriter.DraftPlanFileName));
        Assert.Equal(ScenePlanSerializer.Write(content.Plan), Text(entries, ScenePlanSerializer.FileName));
        Assert.Equal("Invented Small Cave", full.Name);
        Assert.Equal("clip", full.Output.BaseName);
        Assert.Equal(PlanEngine.Workbench, draft.Look.Engine);
        Assert.Equal("clip-draft", draft.Output.BaseName);
        Assert.Equal((160, 90), (draft.Look.Width, draft.Look.Height));
        Assert.Equal(full.Camera.Track, draft.Camera.Track);
    }

    [Fact]
    public void EveryPath_StaysInsideTheBundle()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.All(zip.Entries, e =>
        {
            Assert.False(Path.IsPathRooted(e.FullName), e.FullName);
            Assert.DoesNotContain('\\', e.FullName);
            Assert.DoesNotContain(':', e.FullName);
            Assert.DoesNotContain("..", e.FullName.Split('/'));
            Assert.DoesNotContain(".", e.FullName.Split('/'));
        });
    }

    // ---- what is refused ----

    [Fact]
    public void AFileOutsideTheBundle_OrInThePlaceOfItsOwnFiles_IsRefused()
    {
        var content = Content(draft: false);
        string mesh = content.Files["model.ply"];
        using var sink = new MemoryStream();

        foreach (string path in new[] { "../model.ply", "/abs/model.ply", "C:/model.ply", "render.py", "README.TXT", "LICENSE", "notice", "thide_player/core.py" })
        {
            var bad = content with { Files = new Dictionary<string, string>(content.Files) { [path] = mesh } };
            Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(bad, sink, "cave"));
        }
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(
            content with { Files = new Dictionary<string, string> { ["model.ply"] = mesh, ["./model.ply"] = mesh } }, sink, "cave"));
    }

    [Fact]
    public void ABundleThatWouldNotRenderOnItsOwn_IsRefused()
    {
        var content = Content(draft: false);
        using var sink = new MemoryStream();

        // The mesh is not given.
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(content with { Files = new Dictionary<string, string>() }, sink, "cave"));
        // The given mesh is not there.
        Assert.Throws<FileNotFoundException>(() => RenderBundleWriter.WriteZip(
            content with { Files = new Dictionary<string, string> { ["model.ply"] = Path.Combine(_dir, "nowhere.ply") } }, sink, "cave"));
        // The render would write outside the bundle's folder.
        var outside = content with { Plan = content.Plan with { Output = content.Plan.Output with { Directory = "/tmp/renders" } } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(outside, sink, "cave"));
        // A module folder the plan names is not in it.
        var modules = content with { Plan = content.Plan with { ModuleFolders = ["extra"] } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(modules, sink, "cave"));
        // An invalid draft.
        var draft = content with { Draft = ScenePlanBuilder.Draft(content.Plan) with { Features = [] } };
        Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(draft, sink, "cave"));
        Assert.Equal(0, sink.Length);
    }

    [Fact]
    public void FilesOfAModuleFolder_TravelWithThePlan()
    {
        var content = Content(draft: false);
        string module = Path.Combine(_dir, "marker_cube.py");
        File.WriteAllText(module, "def build(ctx, section):\n    pass\n");
        var withModule = content with
        {
            Plan = content.Plan with { ModuleFolders = ["extra"] },
            Files = new Dictionary<string, string>(content.Files) { ["extra/marker_cube.py"] = module },
        };
        using var sink = new MemoryStream();
        Assert.Contains("cave/extra/marker_cube.py", RenderBundleWriter.WriteZip(withModule, sink, "cave"));
    }

    // ---- the run scripts ----

    [Fact]
    public void TheRunScripts_StartBlenderWithThePlayer_InTheirPlatformsLineEndings()
    {
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        var entries = Read(zipPath);

        string sh = Text(entries, "render.sh");
        Assert.StartsWith("#!/bin/sh\n", sh);
        Assert.DoesNotContain('\r', sh);
        Assert.Contains("-b --factory-startup --python-exit-code 64 --python \"$HERE/render.py\" -- --plan \"$PLAN\" --stage \"$STAGE\" \"$@\"", sh);
        Assert.Contains("$BLENDER", sh);

        string cmd = Text(entries, "render.cmd");
        Assert.StartsWith("@echo off\r\n", cmd);
        Assert.Equal(cmd.Split('\n').Length - 1, cmd.Split("\r\n").Length - 1); // no bare LF
        Assert.Contains("-b --factory-startup --python-exit-code 64 --python \"%HERE%render.py\" -- --plan \"%PLAN%\" --stage %STAGE% %ARGS%", cmd);
        Assert.Contains("--compose", cmd);
        Assert.Contains("scene-plan.draft.json", cmd);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.All(zip.Entries, e => Assert.Equal(e.FullName == "cave/render.sh" ? 0x81ED : 0x81A4, e.ExternalAttributes >>> 16));
    }

    [Fact]
    public async Task RenderSh_PassesThePlanAndTheOptionsToTheBlenderItIsGiven()
    {
        if (OperatingSystem.IsWindows()) return; // a POSIX shell script
        string unpacked = Path.Combine(_dir, "unpacked");
        string zipPath = Path.Combine(_dir, "cave.zip");
        RenderBundleWriter.WriteZip(Content(), zipPath);
        ZipFile.ExtractToDirectory(zipPath, unpacked);
        string folder = Assert.Single(Directory.GetDirectories(unpacked));
        Assert.Equal(Path.Combine(unpacked, "cave"), folder);
        Assert.Empty(Directory.GetFiles(unpacked));

        // A stand-in for Blender that prints the arguments it was given, one per line.
        string fake = Path.Combine(_dir, "fake blender");
        File.WriteAllText(fake, "#!/bin/sh\nfor a in \"$@\"; do echo \"$a\"; done\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var (code, output) = await RunScript(folder, fake, "--build-only");
        Assert.Equal(0, code);
        Assert.Equal(
            ["-b", "--factory-startup", "--python-exit-code", "64", "--python", Path.Combine(folder, "render.py"),
             "--", "--plan", Path.Combine(folder, "scene-plan.json"), "--stage", "all", "--build-only"],
            output.TrimEnd('\n').Split('\n'));

        (code, output) = await RunScript(folder, fake, "--draft");
        Assert.Equal(0, code);
        Assert.Equal([Path.Combine(folder, "scene-plan.draft.json"), "--stage", "all"], output.TrimEnd('\n').Split('\n')[^3..]);

        // The output again from the frames that are there, of the plan or of its draft, with
        // whatever else the player is to be told.
        (code, output) = await RunScript(folder, fake, "--compose");
        Assert.Equal(0, code);
        Assert.Equal(["--plan", Path.Combine(folder, "scene-plan.json"), "--stage", "compose"], output.TrimEnd('\n').Split('\n')[^4..]);
        (code, output) = await RunScript(folder, fake, "--compose", more: ["--draft", "--keep-frames"]);
        Assert.Equal(0, code);
        Assert.Equal(
            ["--plan", Path.Combine(folder, "scene-plan.draft.json"), "--stage", "compose", "--keep-frames"],
            output.TrimEnd('\n').Split('\n')[^5..]);

        File.Delete(Path.Combine(folder, RenderBundleWriter.DraftPlanFileName));
        (code, output) = await RunScript(folder, fake, "--draft");
        Assert.Equal(1, code);
        Assert.Contains("no draft", output);
    }

    [Fact]
    public async Task RenderSh_SaysSo_WhenThereIsNoBlender()
    {
        if (OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(_dir, "unpacked");
        RenderBundleWriter.WriteFolder(Content(), folder);
        var (code, output) = await RunScript(folder, blender: null, path: "/nonexistent");
        Assert.Equal(1, code);
        Assert.Contains("Blender was not found", output);
    }

    /// <summary>Runs render.sh as a user would, by its executable bit, and returns its exit code
    /// and its standard output and error together.</summary>
    private static async Task<(int, string)> RunScript(
        string folder, string? blender, string? argument = null, string? path = null, string[]? more = null)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, "render.sh"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(), // the script finds its folder itself
        };
        if (argument is not null) start.ArgumentList.Add(argument);
        foreach (string another in more ?? []) start.ArgumentList.Add(another);
        if (blender is null) start.Environment.Remove("BLENDER");
        else start.Environment["BLENDER"] = blender;
        if (path is not null) start.Environment["PATH"] = path;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }

    // ---- a folder ----

    [Fact]
    public void AFolder_HoldsWhatTheZipHolds()
    {
        var content = Content();
        string zipPath = Path.Combine(_dir, "cave.zip"), folder = Path.Combine(_dir, "folder");
        RenderBundleWriter.WriteZip(content, zipPath);
        Directory.CreateDirectory(Path.Combine(folder, "thide_player", "modules"));
        File.WriteAllText(Path.Combine(folder, "thide_player", "modules", "stale.py"), "");

        var written = RenderBundleWriter.WriteFolder(content, folder);

        var entries = Read(zipPath);
        Assert.Equal(entries.Keys.Order(StringComparer.Ordinal), written);
        Assert.All(entries, e => Assert.Equal(e.Value, File.ReadAllBytes(Path.Combine(folder, e.Key))));
        Assert.False(File.Exists(Path.Combine(folder, "thide_player", "modules", "stale.py")));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(folder, "render.sh")).HasFlag(UnixFileMode.UserExecute));
    }

    // ---- photos ----

    private SceneSpec Annotated(string image) => Orbit with
    {
        Annotations =
        [
            new AnnotationSpec { Id = "view", Kind = AnnotationKind.Photo, Station = "main6", Image = image, Framed = true },
            new AnnotationSpec { Kind = AnnotationKind.Label, Station = "main0", Text = "Intrarea", From = 1, To = 3 },
        ],
    };

    [Fact]
    public void ABundleOfASceneWithAnnotations_CarriesTheirPhotos_InBothPlans_AndItsReadmeNamesThem()
    {
        string picture = TestImages.Write(Path.Combine(_dir, "trip"), "the view.jpg", TestImages.FlagJpeg());
        var content = Content(Annotated(picture));

        Assert.Equal(picture, content.Files["photos/the_view.jpg"]);
        Assert.Empty(content.NotCarried);
        foreach (var plan in new[] { content.Plan, content.Draft! })
        {
            Assert.Empty(ScenePlanValidator.Validate(plan));
            Assert.Equal(["view", "annotation-2"], plan.Annotations!.Select(a => a.Id));
            Assert.Equal("photos/the_view.jpg", plan.Annotations![0].Image);
        }

        string zipPath = Path.Combine(_dir, "annotated.zip");
        RenderBundleWriter.WriteZip(content, zipPath);
        var entries = Read(zipPath);
        Assert.Equal(TestImages.FlagJpeg(), entries["photos/the_view.jpg"]);
        Assert.Contains("thide_player/modules/annotations.py", entries.Keys);
        string readme = Text(entries, "README.txt");
        Assert.Contains("  photos/the_view.jpg ", readme);
        Assert.Contains("a photo shown in the scene", readme);
        string romanian = RenderBundleWriter.Readme(content with { Language = "ro" });
        Assert.Contains("o fotografie afișată în scenă", romanian);

        // In a folder it lies where the plan names it.
        string folder = Path.Combine(_dir, "annotated");
        RenderBundleWriter.WriteFolder(content, folder);
        Assert.Equal(TestImages.FlagJpeg(), File.ReadAllBytes(Path.Combine(folder, "photos", "the_view.jpg")));
    }

    [Fact]
    public void APhotoThatIsNoImage_OrIsTooLarge_OrIsMissing_IsRefused_WhereverItComesFrom()
    {
        string picture = TestImages.Write(Path.Combine(_dir, "trip"), "view.png", TestImages.FlagPng());
        var good = Content(Annotated(picture));
        string zipPath = Path.Combine(_dir, "refused.zip");

        // From a file.
        string notes = TestImages.Write(Path.Combine(_dir, "trip"), "notes.png", "no picture at all"u8.ToArray());
        var ex = Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(Content(Annotated(notes), work: "work2"), zipPath));
        Assert.StartsWith("The file of photo 'view' (photos/notes.png) cannot travel with the plan: the file is neither a PNG nor a JPEG image.", ex.Message);
        Assert.False(File.Exists(zipPath));

        // From whoever makes the bundle, as a stream: a picture of a size no render can use.
        var files = good.Files.Where(f => f.Key != "photos/view.png").ToDictionary(f => f.Key, f => f.Value);
        byte[] vast = TestImages.FlagPng(8, 8);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(vast.AsSpan(16), 20_000);
        int opened = 0;
        var streamed = good with
        {
            Files = files,
            Streams = new Dictionary<string, Func<Stream>> { ["photos/view.png"] = () => { opened++; return new MemoryStream(vast); } },
        };
        ex = Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(streamed, new MemoryStream(), "refused"));
        Assert.Contains("the image is 20000 x 8 pixels; a side is at most 16384", ex.Message);
        Assert.Equal(1, opened);   // the draft names the same file: it is checked once

        // A good one from a stream is opened to be checked and again to be written.
        opened = 0;
        byte[] flag = TestImages.FlagPng();
        var fine = streamed with { Streams = new Dictionary<string, Func<Stream>> { ["photos/view.png"] = () => { opened++; return new MemoryStream(flag); } } };
        var written = new MemoryStream();
        Assert.Contains("fine/photos/view.png", RenderBundleWriter.WriteZip(fine, written, "fine"));
        Assert.Equal(2, opened);

        // Not among the files at all.
        ex = Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(good with { Files = files }, new MemoryStream(), "refused"));
        Assert.Contains("The file of photo 'view' (photos/view.png) is not among the bundle's files.", ex.Message);
    }

    [Fact]
    public void ASingleFile_CarriesAPhoto_AfterTheSameCheck()
    {
        var plan = Content(Annotated(TestImages.Write(Path.Combine(_dir, "trip"), "view.png", TestImages.FlagPng())), draft: false).Plan;
        byte[] mesh = File.ReadAllBytes(Path.Combine(_dir, "work", "model.ply"));

        string script = PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]> { ["model.ply"] = mesh, ["photos/view.png"] = TestImages.FlagPng() });

        Assert.Contains("\"photos/view.png\": \"" + Convert.ToBase64String(TestImages.FlagPng()) + "\"", script);
        var ex = Assert.Throws<ArgumentException>(() => PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]> { ["photos/view.png"] = [1, 2, 3] }));
        Assert.Contains("(photos/view.png) cannot travel with the plan", ex.Message);
        // A file the plan does not name is still refused, with the photos among what it could be.
        ex = Assert.Throws<ArgumentException>(() => PlayerWriter.SingleFile(plan, new Dictionary<string, byte[]> { ["photos/other.png"] = TestImages.FlagPng() }));
        Assert.Contains("No model, material or photo of the plan has the path 'photos/other.png'.", ex.Message);
    }

    // ---- the README and what the plan leaves out ----

    [Fact]
    public void TheReadme_SaysHowToRender_WhatItWrites_AndWhatTheFilesAre()
    {
        string readme = RenderBundleWriter.Readme(Content());

        Assert.StartsWith("Invented Small Cave\n===", readme);
        Assert.Contains("Blender 4.2 or newer", readme);
        Assert.Contains("sh render.sh\n", readme);
        Assert.Contains("render.cmd\n", readme);
        Assert.Contains("sh render.sh --draft", readme);
        Assert.Contains("BLENDER=/path/to/blender sh render.sh", readme);
        Assert.Contains("--python render.py -- --plan scene-plan.draft.json", readme);
        Assert.Contains("out/clip.mp4, a video of", readme);
        Assert.Contains("320x180 pixels, Cycles", readme);
        Assert.Contains("The draft: out/clip-draft.mp4", readme);
        Assert.Contains("160x90 pixels, Workbench", readme);
        Assert.Contains("  model.ply ", readme);
        Assert.Contains("thide_player/", readme);
        Assert.Contains("version " + PlayerFiles.Version, readme);
        Assert.DoesNotContain("NOT IN THIS SCENE", readme);
        Assert.DoesNotContain('\r', readme);
    }

    [Fact]
    public void WhatThePlanLeavesOut_IsReportedToTheCaller_AndInTheReadme()
    {
        var spec = Orbit with
        {
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "sky.hdr" },
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Invented" } },
            Surface = new SurfaceSpec { Mode = SurfaceMode.Solid },
        };
        var content = Content(spec);

        Assert.Equal(["lighting.hdriPath"], content.NotCarried);
        // The cave of this bundle has no terrain to draw: that is said, not left out in silence.
        Assert.Equal(["The model has no terrain surface; rendering without one."], content.Notices);
        string readme = RenderBundleWriter.Readme(content);
        Assert.Contains("NOT IN THIS SCENE", readme);
        Assert.Contains("the HDRI image lighting", readme);
        Assert.DoesNotContain("terrain", readme);
    }

    [Fact]
    public void ABundleOfACaveWithATerrain_CarriesTheTerrainAndItsImage()
    {
        string lox = Path.Combine(_dir, "terrain.lox");
        LoxWriter.WriteFile(InventedTerrain.Add(InventedCave.Build(InventedCaveSize.Small)), lox);
        var source = new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
        var spec = Orbit with { Surface = new SurfaceSpec { Mode = SurfaceMode.Textured } };

        var content = RenderBundleWriter.FromModel(source, spec, Path.Combine(_dir, "work-terrain"), draft: true);

        Assert.Empty(content.NotCarried);
        Assert.Empty(content.Notices);
        Assert.Equal(["model.ply", "surface.ply", "surface.png"], content.Files.Keys);
        Assert.Equal("surface.png", Assert.Single(content.Plan.TexturedMaterials!).Image);
        Assert.Equal(content.Plan.TexturedMaterials, content.Draft!.TexturedMaterials);

        string zipPath = Path.Combine(_dir, "terrain.zip");
        var entries = RenderBundleWriter.WriteZip(content, zipPath);
        Assert.Contains("terrain/surface.ply", entries);
        Assert.Contains("terrain/surface.png", entries);
        Assert.Contains("terrain/thide_player/modules/textured_materials.py", entries);
        var read = Read(zipPath);
        Assert.Equal(File.ReadAllBytes(content.Files["surface.png"]), read["surface.png"]);
        string readme = Text(read, "README.txt");
        Assert.Contains("surface.ply", readme);
        Assert.Contains("the image of a material", readme);
        Assert.DoesNotContain("NOT IN THIS SCENE", readme);

        // Without its image the bundle would not render on its own.
        var incomplete = content with { Files = content.Files.Where(f => f.Key != "surface.png").ToDictionary() };
        var refused = Assert.Throws<ArgumentException>(() => RenderBundleWriter.WriteZip(incomplete, Path.Combine(_dir, "incomplete.zip")));
        Assert.Contains("material 'Terrain' (surface.png)", refused.Message);
    }

    [Fact]
    public void TheReadme_OfFramesAStill_AndASavedScene()
    {
        var content = Content(draft: false);
        var plan = content.Plan;

        string frames = RenderBundleWriter.Readme(content with { Plan = plan with { Output = plan.Output with { Kind = PlanOutputKind.Frames } } });
        Assert.Contains($"out/clip_0001.png to out/clip_{plan.Timeline.FrameCount:0000}.png", frames);

        string still = RenderBundleWriter.Readme(content with { Plan = plan with { Output = plan.Output with { Kind = PlanOutputKind.Still, Directory = "." } } });
        Assert.Contains("clip.png, one picture", still);

        string saved = RenderBundleWriter.Readme(content with { Plan = ScenePlanBuilder.SaveBlend(plan, render: false) });
        Assert.Contains("scene.blend: the built scene", saved);
        Assert.Contains("nothing is rendered", saved);
    }
}
