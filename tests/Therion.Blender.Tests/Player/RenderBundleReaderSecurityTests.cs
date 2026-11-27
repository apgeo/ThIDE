// What the reader of a bundle from elsewhere refuses, without Blender: an output folder outside the
// bundle, a GLB that names other files or uses extensions beyond materials, meshes whose triangles
// a header hides, segment plans named as Python modules, zips of very many entries or of entries
// that hold other than they say, names that are not the same file on every system, images that are
// not what a material may show. And the defences that held before: every zip shape that leaves
// the bundle is still refused. Invented data only: the invented small cave, and files made here.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Blender.Sources;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;
using Therion.Cave3D.Parsing;
using Therion.CaveGeometry;
using Therion.CaveGeometry.Writers;
using Xunit.Abstractions;
using static Therion.Blender.Tests.Player.HostileBundles;

namespace Therion.Blender.Tests.Player;

public sealed class RenderBundleReaderSecurityTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"bundle-security-{Guid.NewGuid():N}")).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    internal static SceneSpec Small => PlanFixtures.Preset("Orbit showcase") with
    {
        Labels = new LabelsSpec(),
        Engine = new EngineSpec { Kind = RenderEngineKind.Cycles, Samples = 1, Denoise = false, Gpu = GpuMode.CpuOnly },
        Output = new OutputSpec { Kind = OutputKind.Still, Width = 64, Height = 48, BaseName = "clip" },
        Animation = new AnimationSpec { Fps = 8, DurationSeconds = 1 },
    };

    internal static ResolvedModelSource SmallCave(string dir)
    {
        string lox = Path.Combine(dir, "small.lox");
        if (!File.Exists(lox)) LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), lox);
        return new ResolvedModelSource { Path = lox, Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile };
    }

    /// <summary>A bundle written by the library, its plan then changed as an attacker would, with
    /// files added, zipped in one folder as the writer zips it.</summary>
    internal static string Craft(string dir, string name, Action<JsonObject>? edit = null, IDictionary<string, byte[]>? extra = null, SceneSpec? spec = null)
    {
        var content = RenderBundleWriter.FromModel(SmallCave(dir), spec ?? Small, Path.Combine(dir, "work-" + name), draft: false);
        string folder = Path.Combine(dir, "craft", name);
        RenderBundleWriter.WriteFolder(content, folder);
        string planPath = Path.Combine(folder, "scene-plan.json");
        var plan = JsonNode.Parse(File.ReadAllText(planPath))!.AsObject();
        edit?.Invoke(plan);
        File.WriteAllText(planPath, plan.ToJsonString(), new UTF8Encoding(false));
        foreach (var (path, bytes) in extra ?? new Dictionary<string, byte[]>())
        {
            string target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        string zip = Path.Combine(dir, name + ".zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        return zip;
    }

    private string Craft(string name, Action<JsonObject>? edit = null, IDictionary<string, byte[]>? extra = null, SceneSpec? spec = null)
        => Craft(_dir, name, edit, extra, spec);

    private string Folder(string name) => Path.Combine(_dir, "unpacked-" + name);

    private RenderBundleRefusedException Refused(string zip, string name, RenderBundleLimits? limits = null)
    {
        var e = Record.Exception(() => RenderBundleReader.Unpack(zip, Folder(name), limits));
        output.WriteLine($"{name}: {e?.GetType().Name}: {e?.Message}");
        return Assert.IsType<RenderBundleRefusedException>(e);
    }

    private static Action<JsonObject> WithModel(string path) => p => p["models"]!.AsArray().Add(new JsonObject { ["id"] = "Extra", ["path"] = path });

    private (string Plan, byte[] Ply) GoodPlanAndPly()
    {
        var content = RenderBundleWriter.FromModel(SmallCave(_dir), Small, Path.Combine(_dir, "work-good"), draft: false);
        return (ScenePlanSerializer.Write(content.Plan), File.ReadAllBytes(content.Files["model.ply"]));
    }

    // ---- the output folder ----

    [Theory]
    [InlineData("absolute")]
    [InlineData("../../../outside")]
    [InlineData("out/../../outside")]
    [InlineData("..\\..\\outside")]
    [InlineData("C:/outside")]
    [InlineData("\\\\host\\share")]
    public void AnOutputFolderOutsideTheBundle_IsRefused(string directory)
    {
        string outside = directory == "absolute" ? Path.Combine(_dir, "outside") : directory;
        string zip = Craft("out-" + Guid.NewGuid().ToString("N")[..6], p => p["output"]!["directory"] = outside);
        Assert.Contains("does not lie inside the bundle", Refused(zip, "out").Message);
        Assert.False(Directory.Exists(Path.Combine(_dir, "outside")));
    }

    [Fact]
    public void AnOutputFolderInsideTheBundle_IsWrittenIntoTheBundlesOwnOutputFolder_WithItsSubtitles()
    {
        var spec = Small with
        {
            Output = Small.Output with { Kind = OutputKind.FrameSequence },
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Captions = [new CaptionSpec { Text = "Invented caption", Start = 0, End = 1 }] } },
        };
        var content = RenderBundleWriter.FromModel(SmallCave(_dir), spec, Path.Combine(_dir, "work-sub"), draft: false);
        string folder = Path.Combine(_dir, "craft", "sub");
        RenderBundleWriter.WriteFolder(content, folder);
        // The plan says its output goes to elsewhere/inner, and its subtitles are there.
        var plan = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")))!.AsObject();
        plan["output"]!["directory"] = "elsewhere/inner";
        File.WriteAllText(Path.Combine(folder, "scene-plan.json"), plan.ToJsonString());
        Directory.CreateDirectory(Path.Combine(folder, "elsewhere", "inner"));
        foreach (string extension in new[] { ".srt", ".vtt" })
            File.Move(Path.Combine(folder, "out", "clip" + extension), Path.Combine(folder, "elsewhere", "inner", "clip" + extension));
        string zip = Path.Combine(_dir, "sub.zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);

        var bundle = RenderBundleReader.Unpack(zip, Folder("sub"));
        Assert.Equal(RenderBundleWriter.OutputDirectory, bundle.Scene.Output.Directory);
        var unpacked = JsonNode.Parse(File.ReadAllText(Path.Combine(bundle.Folder, "scene-plan.json")))!;
        Assert.Equal(RenderBundleWriter.OutputDirectory, (string?)unpacked["output"]!["directory"]);
        Assert.True(File.Exists(Path.Combine(bundle.Folder, "out", "clip.srt")));
        Assert.True(File.Exists(Path.Combine(bundle.Folder, "out", "clip.vtt")));
        Assert.False(Directory.Exists(Path.Combine(bundle.Folder, "elsewhere")));
    }

    [Theory]
    [InlineData("movie")]
    [InlineData("segment")]
    public void AnOutputFolderOutsideTheBundle_OfAFilmOrOfOneOfItsShots_IsRefused(string which)
    {
        var storyboard = new Storyboard
        {
            Fps = 8,
            Shots = [new ShotSpec { Preset = "Orbit showcase", Seconds = 0.25 }, new ShotSpec { Preset = "Orbit showcase", Seconds = 0.25 }],
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 32, Height = 24, BaseName = "film" },
        };
        var content = RenderBundleWriter.FromStoryboard(SmallCave(_dir), storyboard, Path.Combine(_dir, "work-film-" + which));
        string folder = Path.Combine(_dir, "craft", "film-" + which);
        RenderBundleWriter.WriteFolder(content, folder);
        var movie = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")))!.AsObject();
        string target = which == "movie" ? "scene-plan.json" : (string)movie["segments"]![1]!["plan"]!;
        var plan = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, target)))!.AsObject();
        plan["output"]!["directory"] = Path.Combine(_dir, "outside");
        File.WriteAllText(Path.Combine(folder, target), plan.ToJsonString());
        string zip = Path.Combine(_dir, "film-" + which + ".zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);

        var refused = Refused(zip, "film-" + which);
        Assert.Contains("does not lie inside the bundle", refused.Message);
        Assert.Contains(target, refused.Message);
    }

    // ---- GLB ----

    public static TheoryData<string> GlbThatNameFiles() => new() { "absolute-image", "climbing-buffer", "data-image", "uri-in-an-extension" };

    [Theory]
    [MemberData(nameof(GlbThatNameFiles))]
    public void AGlbThatNamesAFile_IsRefused(string how)
    {
        string secret = Path.Combine(_dir, "secrets", "invented-photo.png");
        byte[] glb = how switch
        {
            "absolute-image" => Backdrop(secret, null),
            "climbing-buffer" => Backdrop(null, "../../../secrets/invented-secret.bin"),
            "data-image" => Backdrop("data:image/png;base64," + Convert.ToBase64String(MagentaPng(4)), null),
            _ => Grid(3, edit: g =>
            {
                g["extensionsUsed"] = new JsonArray("KHR_materials_emissive_strength");
                g["materials"] = new JsonArray(new JsonObject
                {
                    ["extensions"] = new JsonObject { ["KHR_materials_emissive_strength"] = new JsonObject { ["uri"] = secret } },
                });
            }),
        };
        string zip = Craft("glb-" + how, WithModel("backdrop.glb"), new Dictionary<string, byte[]> { ["backdrop.glb"] = glb });
        Assert.Contains("names a file by its URI", Refused(zip, "glb-" + how).Message);
    }

    [Theory]
    [InlineData("KHR_draco_mesh_compression", "extensionsRequired")]
    [InlineData("EXT_meshopt_compression", "extensionsUsed")]
    [InlineData("EXT_texture_webp", "extensionsUsed")]
    [InlineData("KHR_texture_basisu", "texture")]
    [InlineData("EXT_mesh_gpu_instancing", "node")]
    public void AGlbThatUsesAnExtensionBeyondMaterials_IsRefused(string extension, string where)
    {
        byte[] glb = Grid(3, edit: g =>
        {
            switch (where)
            {
                case "texture":
                    g["textures"] = new JsonArray(new JsonObject { ["extensions"] = new JsonObject { [extension] = new JsonObject { ["source"] = 0 } } });
                    break;
                case "node":
                    g["nodes"]![0]!["extensions"] = new JsonObject { [extension] = new JsonObject() };
                    break;
                default:
                    g[where] = new JsonArray(extension);
                    break;
            }
        });
        string zip = Craft("ext-" + extension, WithModel("grid.glb"), new Dictionary<string, byte[]> { ["grid.glb"] = glb });
        Assert.Contains($"the extension '{extension}'", Refused(zip, "ext-" + extension).Message);
    }

    [Fact]
    public void AGlbOfMaterialsAndAnEmbeddedPng_IsAccepted()
    {
        byte[] glb = Backdrop(null, null);
        string zip = Craft("glb-good", WithModel("backdrop.glb"), new Dictionary<string, byte[]> { ["backdrop.glb"] = glb });
        var bundle = RenderBundleReader.Unpack(zip, Folder("glb-good"));
        Assert.True(File.Exists(Path.Combine(bundle.Folder, "backdrop.glb")));
    }

    [Theory]
    [InlineData("image/webp")]
    [InlineData("image/jpeg")]
    public void AGlbImageThatIsNotAPngOrAJpegAsItSays_IsRefused(string mimeType)
    {
        byte[] glb = Backdrop(null, null, MagentaPng(4), mimeType);
        string zip = Craft("glb-mime-" + mimeType.Replace('/', '-'), WithModel("backdrop.glb"), new Dictionary<string, byte[]> { ["backdrop.glb"] = glb });
        Assert.Contains("image of the GLB file", Refused(zip, "glb-mime").Message);
    }

    [Fact]
    public void AGlbWhoseAccessorReadsPastItsData_IsRefused()
    {
        byte[] glb = Grid(11, edit: g => g["accessors"]![1]!["count"] = 1_000_000_000);
        string zip = Craft("glb-past", WithModel("grid.glb"), new Dictionary<string, byte[]> { ["grid.glb"] = glb });
        Assert.Contains("read more than the data it names", Refused(zip, "glb-past").Message);
    }

    [Fact]
    public void AGlbsTriangles_AreCountedFromItsAccessors_ForEveryNodeThatShowsIt()
    {
        // 2 (101 - 1)² = 20,000 triangles, shown by two nodes: 40,000.
        string zip = Craft("glb-count", WithModel("grid.glb"), new Dictionary<string, byte[]> { ["grid.glb"] = Grid(101, nodes: 2) });
        Assert.Contains("triangles, more than the 30000 allowed", Refused(zip, "glb-count", new RenderBundleLimits { MaxTriangles = 30_000 }).Message);
        var bundle = RenderBundleReader.Unpack(zip, Folder("glb-count-ok"), new RenderBundleLimits { MaxTriangles = 10_000_000 });
        long cave = RenderBundleReader.Triangles(Path.Combine(bundle.Folder, "model.ply"));
        Assert.Equal(cave + 40_000, bundle.Triangles);
    }

    [Fact]
    public void ThIDEsOwnGlb_IsAccepted_AndCountedAsItsMeshHasTriangles()
    {
        var mesh = new CaveMesh
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 1)],
            Triangles = [new CaveTriangle(0, 1, 2), new CaveTriangle(1, 3, 2)],
        };
        string path = Path.Combine(_dir, "own.glb");
        GlbWriter.WriteFile(mesh, path);
        Assert.Equal(2, RenderBundleReader.Triangles(path));
    }

    [Fact]
    public void AGlbWhoseNodesAreNotATree_IsRefused()
    {
        byte[] glb = Grid(3, nodes: 2, edit: g => { g["nodes"]![0]!["children"] = new JsonArray(1); g["nodes"]![1]!["children"] = new JsonArray(1); });
        string path = Path.Combine(_dir, "loop.glb");
        File.WriteAllBytes(path, glb);
        Assert.Contains("not a tree", Assert.Throws<RenderBundleRefusedException>(() => RenderBundleReader.Triangles(path)).Message);
    }

    // ---- PLY ----

    [Fact]
    public void APlyWithALongHeader_IsCountedFromItsFaces()
    {
        var (_, ply) = GoodPlanAndPly();
        long faces = RenderBundleReader.PlyFaces(Write("plain.ply", ply));
        Assert.True(faces > 10);
        string zip = Craft("ply-padded", extra: new Dictionary<string, byte[]> { ["model.ply"] = Padded(ply, 5000) });
        Assert.Contains($"{faces} triangles", Refused(zip, "ply-padded", new RenderBundleLimits { MaxTriangles = 10 }).Message);
        Assert.Equal(faces, RenderBundleReader.PlyFaces(Write("padded.ply", Padded(ply, 5000))));
    }

    [Theory]
    [InlineData(4, false, 2)]
    [InlineData(5, false, 3)]
    [InlineData(5, true, 3)]
    [InlineData(3, true, 1)]
    public void APlysFaceOfNCorners_CountsAsNMinus2Triangles(int corners, bool ascii, int each)
    {
        string path = Write($"poly-{corners}-{ascii}.ply", Polygons(faces: 7, corners, ascii));
        Assert.Equal(7L * each, RenderBundleReader.Triangles(path));
    }

    public static TheoryData<string, string> BadPlys() => new()
    {
        { "ply\nformat binary_little_endian 1.0\nelement face 3\nproperty list uchar int vertex_indices\n", "no end_header" },
        { "ply\nformat binary_little_endian 1.0\nelement face 30\nproperty list uchar int vertex_indices\nend_header\n\u0003\u0000\u0000", "ends before its data" },
        { "ply\nformat cbor 1.0\nend_header\n", "is not known" },
        { "ply\nformat ascii 1.0\nelement face 1\nproperty list float int vertex_indices\nend_header\n3 0 1 2\n", "not one PLY knows" },
        { "ply\nformat ascii 1.0\nelement face 1\nproperty list uchar int vertex_indices\nsomething else\nend_header\n3 0 1 2\n", "does not know" },
        { "not a ply\n", "not a PLY file" },
    };

    [Theory]
    [MemberData(nameof(BadPlys))]
    public void APlyWhoseFacesCannotBeCounted_IsRefused(string text, string message)
    {
        string path = Write("bad-" + Guid.NewGuid().ToString("N")[..6] + ".ply", Encoding.Latin1.GetBytes(text));
        Assert.Contains(message, Assert.Throws<RenderBundleRefusedException>(() => RenderBundleReader.Triangles(path)).Message);
    }

    [Fact]
    public void APlyHeaderLongerThanTheCap_IsRefused()
    {
        var (_, ply) = GoodPlanAndPly();
        string path = Write("huge-header.ply", Padded(ply, 50_000));
        Assert.Contains("longer than", Assert.Throws<RenderBundleRefusedException>(() => RenderBundleReader.Triangles(path)).Message);
    }

    // ---- segment plans ----

    [Theory]
    [InlineData("hashlib.py")]
    [InlineData("a&calc&.json")]
    [InlineData("scene-plan.s01.JSON")]
    [InlineData("scene-plan..json")]
    [InlineData("other.json")]
    public void ASegmentPlanThatIsNotNamedAsASegmentsPlan_IsRefused(string name)
    {
        var storyboard = new Storyboard
        {
            Fps = 8,
            Shots = [new ShotSpec { Preset = "Orbit showcase", Seconds = 0.25 }, new ShotSpec { Preset = "Orbit showcase", Seconds = 0.25 }],
            Output = new OutputSpec { Kind = OutputKind.FrameSequence, Width = 32, Height = 24, BaseName = "film" },
        };
        string key = Guid.NewGuid().ToString("N")[..6];
        var content = RenderBundleWriter.FromStoryboard(SmallCave(_dir), storyboard, Path.Combine(_dir, "work-seg-" + key));
        string folder = Path.Combine(_dir, "craft", "seg-" + key);
        RenderBundleWriter.WriteFolder(content, folder);
        var movie = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "scene-plan.json")))!.AsObject();
        string first = (string)movie["segments"]![0]!["plan"]!;
        File.Move(Path.Combine(folder, first), Path.Combine(folder, name));
        movie["segments"]![0]!["plan"] = name;
        File.WriteAllText(Path.Combine(folder, "scene-plan.json"), movie.ToJsonString());
        string zip = Path.Combine(_dir, "seg-" + key + ".zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        Assert.Contains("a segment's plan is scene-plan.<id>.json", Refused(zip, "seg-" + key).Message);
        Assert.True(RenderBundleReader.IsSegmentPlanName(MoviePlan.SegmentFileName("s01")));
        Assert.True(RenderBundleReader.IsSegmentPlanName(MoviePlan.SegmentFileName("s01", draft: true)));
    }

    // ---- the zip ----

    [Fact]
    public void ManyEntries_AreRefusedBeforeTheyCostMemory()
    {
        string zip = Path.Combine(_dir, "entries.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            for (int i = 0; i < 300_000; i++) archive.CreateEntry($"b/{i:x7}", CompressionLevel.NoCompression);
        GC.Collect();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var e = Refused(zip, "entries");
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        output.WriteLine($"allocated {allocated / 1_000_000} MB before the refusal");
        Assert.Contains("300000 entries, more than the 20000 allowed", e.Message);
        Assert.True(allocated < 16L * 1024 * 1024, $"allocated {allocated / 1_000_000} MB");
    }

    [Fact]
    public void ManyEntries_ThatTheEndOfTheZipDoesNotOwnUpTo_AreCountedAsDotNetReadsThem()
    {
        var (plan, ply) = GoodPlanAndPly();
        var entries = new List<(string, byte[], int?)> { ("b/scene-plan.json", Encoding.UTF8.GetBytes(plan), null), ("b/model.ply", ply, null) };
        for (int i = 0; i < 40; i++) entries.Add(($"b/x{i}.txt", [1], null));
        string zip = RawZip(Path.Combine(_dir, "many.zip"), [.. entries]);
        // The end record says 2 entries; the central directory holds 42.
        byte[] bytes = File.ReadAllBytes(zip);
        int end = bytes.Length - 22;
        Assert.Equal(0x06054b50u, BitConverter.ToUInt32(bytes, end));
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, end + 8);
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, end + 10);
        File.WriteAllBytes(zip, bytes);
        Assert.Contains("entries, more than the 10 allowed", Refused(zip, "many", new RenderBundleLimits { MaxEntries = 10 }).Message);
    }

    [Fact]
    public void AnEntryThatHoldsMoreThanItSays_IsRefused()
    {
        var (plan, ply) = GoodPlanAndPly();
        string zip = RawZip(Path.Combine(_dir, "lying-mesh.zip"), ("b/scene-plan.json", Encoding.UTF8.GetBytes(plan), null), ("b/model.ply", ply, null));
        Understate(zip, "b/model.ply", 1000);
        Assert.Contains("does not hold what the zip says", Refused(zip, "lying-mesh").Message);
    }

    [Fact]
    public void APlanThatHoldsMoreThanItSays_IsRefused_AndReadNoFurtherThanItSays()
    {
        var (plan, ply) = GoodPlanAndPly();
        // 80 MB of spaces after the plan: still JSON, more than the 64 MB a plan may be.
        byte[] text = [.. Encoding.UTF8.GetBytes(plan), .. Enumerable.Repeat((byte)' ', 80 * 1024 * 1024)];
        string zip = RawZip(Path.Combine(_dir, "lying-plan.zip"), ("b/scene-plan.json", text, null), ("b/model.ply", ply, null));
        Understate(zip, "b/scene-plan.json", (uint)Encoding.UTF8.GetByteCount(plan));
        long before = GC.GetTotalAllocatedBytes(precise: true);
        Assert.Contains("does not hold what the zip says", Refused(zip, "lying-plan").Message);
        Assert.True(GC.GetTotalAllocatedBytes(precise: true) - before < 64L * 1024 * 1024);
    }

    public static TheoryData<string> Shapes() => new() { "slip", "absolute", "backslash", "colon", "dot", "link", "duplicate", "nul", "drive" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AnEntryThatIsNotAPlainFileInsideTheBundle_IsRefused(string shape)
    {
        var (plan, ply) = GoodPlanAndPly();
        var entries = new List<(string, byte[], int?)> { ("b/scene-plan.json", Encoding.UTF8.GetBytes(plan), null), ("b/model.ply", ply, null) };
        entries.Add(shape switch
        {
            "slip" => ("b/../../x.txt", [1], null),
            "absolute" => ("/tmp/x.txt", [1], null),
            "backslash" => ("b\\..\\x.txt", [1], null),
            "colon" => ("b/model.ply:stream", [1], null),
            "dot" => ("b/./model.ply", [1], null),
            "link" => ("b/link.ply", Encoding.ASCII.GetBytes("/etc/hostname"), 0xA1FF),
            "duplicate" => ("b/model.ply", ply, null),
            "nul" => ("b/x\0.ply", [1], null),
            _ => ("C:/x.txt", [1], null),
        });
        Refused(RawZip(Path.Combine(_dir, "shape-" + shape + ".zip"), [.. entries]), "shape-" + shape);
    }

    [Theory]
    [InlineData("b/con.png")]
    [InlineData("b/NUL.ply")]
    [InlineData("b/photos/com1.jpg")]
    [InlineData("b/aux/x.png")]
    [InlineData("b/x.png.")]
    [InlineData("b/x.png ")]
    [InlineData("b/a|b.png")]
    [InlineData("b/a?.png")]
    [InlineData("b/MODEL.PLY")]
    public void AnEntryWhoseNameIsNotTheSameFileEverywhere_IsRefused(string name)
    {
        var (plan, ply) = GoodPlanAndPly();
        string zip = RawZip(Path.Combine(_dir, "name-" + Guid.NewGuid().ToString("N")[..6] + ".zip"),
            ("b/scene-plan.json", Encoding.UTF8.GetBytes(plan), null), ("b/model.ply", ply, null), (name, [1], null));
        var refused = Refused(zip, "name");
        Assert.True(refused.Message.Contains("not the same file everywhere") || refused.Message.Contains("differ only by their case"), refused.Message);
    }

    [Fact]
    public void AnEntryNameWithTerminalEscapes_IsQuotedOnOneLine()
    {
        var (plan, ply) = GoodPlanAndPly();
        string evil = "b/\u001b]0;invented title\u0007\u001b[2J\ninvented: succeeded/../x.txt";
        string zip = RawZip(Path.Combine(_dir, "escapes.zip"), ("b/scene-plan.json", Encoding.UTF8.GetBytes(plan), null), ("b/model.ply", ply, null), (evil, [1], null));
        var refused = Refused(zip, "escapes");
        Assert.DoesNotContain(refused.Message, char.IsControl);
        Assert.Contains("\\u001b", refused.Message);
    }

    // ---- images ----

    [Fact]
    public void AMaterialsImageThatIsNotAPngOrAJpeg_IsRefused()
    {
        // A TIFF's head under a .png name: Blender picks its decoder by what a file holds.
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0, 0, 0, 0, 0];
        string zip = Craft("material-tiff", p =>
        {
            p["features"]!.AsArray().Insert(p["features"]!.AsArray().Count - 1, ScenePlan.TexturedMaterialsFeature);
            p["texturedMaterials"] = new JsonArray(new JsonObject { ["id"] = "Painted", ["image"] = "paint.png" });
        }, new Dictionary<string, byte[]> { ["paint.png"] = tiff });
        Assert.Contains("neither a PNG nor a JPEG", Refused(zip, "material-tiff").Message);
    }

    // ---- names ----

    [Theory]
    [InlineData("model.ply", true)]
    [InlineData("my photo.jpg", true)]
    [InlineData("con", false)]
    [InlineData("Con.tar.gz", false)]
    [InlineData("lpt9.png", false)]
    [InlineData("COM¹", false)]
    [InlineData("console.png", true)]
    [InlineData("x.", false)]
    [InlineData("x ", false)]
    [InlineData("a*b", false)]
    [InlineData("a\u0001b", false)]
    [InlineData("..", false)]
    public void APortableName_IsOneEverySystemReadsAlike(string name, bool portable)
        => Assert.Equal(portable, PortableNames.IsPortable(name));

    [Fact]
    public void TextFromElsewhere_IsPrintedOnOneLine_AndCut()
    {
        Assert.Equal("a\\u001b[2J\\u000ab\\u202e", PortableNames.Printable("a\u001b[2J\nb\u202e"));
        Assert.Equal("abc…", PortableNames.Printable("abcdef", 3));
        Assert.Equal("cave 🦇", PortableNames.Printable("cave 🦇"));
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("con.1")]
    [InlineData("job.")]
    public void AJobIdThatWindowsTakesForADevice_IsNotAJobId(string id) => Assert.False(RenderNodeProtocol.IsJobId(id));

    [Theory]
    [InlineData("clip*")]
    [InlineData("clip?")]
    [InlineData("con")]
    [InlineData("a<b")]
    public void ABaseNameWithAWildcardOrADeviceName_IsRefused(string name)
    {
        var spec = Small with { Output = Small.Output with { BaseName = name, OutputDirectory = _dir } };
        Assert.Contains(SceneSpecValidator.Validate(spec), e => e.Path == "output.baseName");
        Assert.False(SceneSpecValidator.IsPlainFileName(name));
    }

    [Fact]
    public void APhotoNamedForAWindowsDevice_TravelsUnderAnotherName()
    {
        string photo = Path.Combine(_dir, "con.png");
        File.WriteAllBytes(photo, MagentaPng(4));
        var files = AnnotationPlanner.Files([new AnnotationSpec { Kind = AnnotationKind.Photo, Image = photo, Station = "1" }]);
        string path = Assert.Single(files.Keys);
        Assert.True(PortableNames.IsPortablePath(path), path);
    }

    private string Write(string name, byte[] bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
