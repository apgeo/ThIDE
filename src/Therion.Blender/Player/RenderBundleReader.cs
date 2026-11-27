// Takes a render bundle that came from elsewhere (a render worker's inbox) apart as data. A bundle
// is a scene plan or a film, its meshes and images, and the player with its run scripts; the
// player and the scripts are code, and none of it is used: the plans are played by the player of
// whoever reads the bundle. So only the plans and the files they name are unpacked, after they
// were checked: every path relative and inside the folder, no entry that leaves it and no link,
// no module folders and no feature outside the built-in ones, plans that the validators pass, and
// limits on sizes, triangles and work, all before any Blender starts.
//
// The reader is the one boundary: the player opens and writes whatever path a plan gives it. So
// every path Blender will open or write is held here, the output folder too, and the output is
// written into the bundle's own folder whatever the plan says (RenderBundleWriter.OutputDirectory).
// Inside a mesh nothing may name another file (a GLB's URIs), and its triangles are counted from
// its data. Names are those every file system reads alike, entries hold what the zip says they
// hold (their CRC-32), and the names a refusal quotes are made fit to print.

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Therion.Blender.Plan;

namespace Therion.Blender.Player;

/// <summary>What a bundle from elsewhere may hold at most.</summary>
public sealed record RenderBundleLimits
{
    /// <summary>The zip file itself.</summary>
    public long MaxZipBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>What is unpacked of it: the plans and the files they name.</summary>
    public long MaxUnpackedBytes { get; init; } = 16L * 1024 * 1024 * 1024;

    /// <summary>Entries in the zip, counted before the zip is opened.</summary>
    public int MaxEntries { get; init; } = 20_000;

    /// <summary>Triangles of the meshes of one scene, counted from their data (a face of n
    /// corners is n − 2 triangles; a GLB's every node that shows a mesh counts).</summary>
    public long MaxTriangles { get; init; } = 50_000_000;

    /// <summary>Frames × pixels × samples of every scene together: what the render costs.</summary>
    public double MaxWork { get; init; } = 1e14;
}

/// <summary>What of a bundle is played: which of its plans, and in which container its movie is
/// written whatever the plan says.</summary>
public sealed record RenderBundleChoice
{
    /// <summary>The plan that is played: <c>scene-plan.json</c>, or the draft's
    /// (<see cref="RenderBundleWriter.DraftPlanFileName"/>).</summary>
    public string Plan { get; init; } = ScenePlanSerializer.FileName;

    /// <summary>The container of the movie, in place of the plan's; null keeps the plan's. A plan
    /// that writes frames or a still keeps them.</summary>
    public PlanContainer? Container { get; init; }

    /// <summary>The choice of the draft plan, or of the full one.</summary>
    public static RenderBundleChoice Of(bool draft, PlanContainer? container = null) => new()
    {
        Plan = draft ? RenderBundleWriter.DraftPlanFileName : ScenePlanSerializer.FileName,
        Container = container,
    };
}

/// <summary>A bundle that is refused, with why.</summary>
public sealed class RenderBundleRefusedException(string message) : Exception(message);

/// <summary>A bundle unpacked and checked: its folder and its plans.</summary>
/// <param name="Folder">Where the plans and their files are.</param>
/// <param name="Scene">The plan of a scene, or the first segment's of a film.</param>
/// <param name="Movie">The movie's plan of a film; null for one scene.</param>
/// <param name="Segments">The plans of a film's segments, in its order; empty for one scene.</param>
/// <param name="Triangles">The triangles of the heaviest scene.</param>
/// <param name="Work">Frames × pixels × samples of every scene together.</param>
public sealed record UnpackedBundle(
    string Folder, ScenePlan Scene, MoviePlan? Movie, IReadOnlyList<ScenePlan> Segments, long Triangles, double Work)
{
    /// <summary>The scenes the render plays: the segments of a film, or the one scene.</summary>
    public IReadOnlyList<ScenePlan> Scenes => Movie is null ? [Scene] : Segments;
}

/// <summary>Unpacks and checks a render bundle that came from elsewhere.</summary>
public static partial class RenderBundleReader
{
    /// <summary>A Unix file mode's kind bits, and that of a symbolic link, as a zip's external
    /// attributes carry them in their upper half.</summary>
    private const int UnixKindMask = 0xF000;
    private const int UnixLink = 0xA000;

    /// <summary>The largest plan.</summary>
    private const long MaxPlanBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Checks the bundle at <paramref name="zipPath"/> and unpacks its plans and the files they
    /// name into <paramref name="folder"/> (which is emptied first). Nothing else of the bundle
    /// is unpacked: its player, its scripts and anything a plan does not name stay in the zip.
    /// <paramref name="choice"/> says which plan is played (it is unpacked as
    /// <c>scene-plan.json</c>) and may set the movie's container. The output of every plan is
    /// written into <see cref="RenderBundleWriter.OutputDirectory"/> of the folder, whatever
    /// folder inside the bundle the plan names; a plan whose output folder is not inside it is refused.
    /// </summary>
    /// <exception cref="RenderBundleRefusedException">The bundle breaks a rule; the message says which.</exception>
    public static UnpackedBundle Unpack(string zipPath, string folder, RenderBundleLimits? limits = null, RenderBundleChoice? choice = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        limits ??= new RenderBundleLimits();
        choice ??= new RenderBundleChoice();
        if (choice.Plan is not (ScenePlanSerializer.FileName or RenderBundleWriter.DraftPlanFileName))
            throw new RenderBundleRefusedException($"'{Shown(choice.Plan)}' is not a plan a bundle is played by.");
        var info = new FileInfo(zipPath);
        if (!info.Exists) throw new RenderBundleRefusedException("The bundle is not there.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new RenderBundleRefusedException("The bundle is a link; a bundle is a file.");
        if (info.Length > limits.MaxZipBytes)
            throw new RenderBundleRefusedException($"The bundle is {info.Length} bytes, more than the {limits.MaxZipBytes} allowed.");
        // Counted before .NET reads the central directory into memory.
        long count = BundleZip.CountEntries(zipPath, limits.MaxEntries);
        if (count > limits.MaxEntries)
            throw new RenderBundleRefusedException($"The bundle has {count} entries, more than the {limits.MaxEntries} allowed.");

        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException ex) { throw new RenderBundleRefusedException("The bundle is not a zip file: " + ex.Message); }
        using (archive)
        {
            try { return Unpack(archive, folder, limits, choice); }
            catch (InvalidDataException ex) { throw new RenderBundleRefusedException("An entry of the bundle cannot be read: " + ex.Message); }
        }
    }

    private static UnpackedBundle Unpack(ZipArchive archive, string folder, RenderBundleLimits limits, RenderBundleChoice choice)
    {
        if (archive.Entries.Count > limits.MaxEntries)
            throw new RenderBundleRefusedException($"The bundle has {archive.Entries.Count} entries, more than the {limits.MaxEntries} allowed.");
        var entries = Entries(archive);

        string Text(string path)
        {
            if (!entries.TryGetValue(path, out var entry))
                throw new RenderBundleRefusedException($"The bundle has no {Shown(path)}.");
            if (entry.Length > MaxPlanBytes)
                throw new RenderBundleRefusedException($"{Shown(path)} is larger than a plan can be.");
            var bytes = new byte[entry.Length];
            using (var stream = entry.Open())
                if (stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) != bytes.Length || stream.ReadByte() >= 0)
                    throw Lying(entry);
            if (BundleZip.Crc32(0, bytes) != entry.Crc32) throw Lying(entry);
            return new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);
        }

        // The plans first: the movie's, or the scene's, and then each segment's. Each is unpacked
        // with its output in the bundle's own output folder; where it said it was is kept for
        // the subtitles that lie there.
        var (top, said) = Played(Text(choice.Plan), choice.Container, choice.Plan);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal) { [ScenePlanSerializer.FileName] = top };
        MoviePlan? movie = null;
        var segments = new List<ScenePlan>();
        ScenePlan scene;
        try
        {
            if (ScenePlanSerializer.IsMovie(top))
            {
                movie = ScenePlanSerializer.ReadMovie(top);
                CheckFeatures(movie.Features, MoviePlan.MovieFeatures, ScenePlanSerializer.FileName);
                foreach (var segment in movie.Segments)
                {
                    if (!IsSegmentPlanName(segment.Plan))
                        throw new RenderBundleRefusedException(
                            $"The plan of segment '{Shown(segment.Id)}' is named '{Shown(segment.Plan)}'; a segment's plan is scene-plan.<id>.json beside the movie's plan.");
                    if (texts.ContainsKey(segment.Plan))
                        throw new RenderBundleRefusedException($"Two segments name the plan '{Shown(segment.Plan)}'.");
                    string text = Played(Text(segment.Plan), null, segment.Plan).Text;
                    texts[segment.Plan] = text;
                    segments.Add(ReadScene(text, segment.Plan));
                }
                if (segments.Count == 0) throw new RenderBundleRefusedException("The movie has no segments.");
                PlayerWriter.Check(movie, segments);
                scene = segments[0];
            }
            else
            {
                scene = ReadScene(top, ScenePlanSerializer.FileName);
                PlayerWriter.Check(scene);
            }
        }
        catch (ScenePlanFormatException ex) { throw new RenderBundleRefusedException("A plan of the bundle cannot be read: " + Shown(ex.Message)); }
        catch (ArgumentException ex) { throw new RenderBundleRefusedException(Shown(ex.Message.Split(" (Parameter", 2)[0], 2000)); }

        var scenes = movie is null ? [scene] : segments;
        // The files the plans name, each once, all of them in the bundle and within the limit:
        // each unpacked where the plan names it.
        var named = scenes.SelectMany(PlayerWriter.FilesOf).Select(f => f.Path).Distinct(StringComparer.Ordinal)
            .Select(path => (Entry: path, Target: path)).ToList();
        // The subtitles of the captions, which a bundle has where its output was to be: text,
        // handed back beside the movie, from the bundle's own output folder.
        var output = movie?.Output ?? scene.Output;
        foreach (string extension in new[] { Subtitles.SrtExtension, Subtitles.VttExtension })
        {
            string folderOfOutput = said.Replace('\\', '/').Trim('/');
            string subtitles = (folderOfOutput is "" or "." ? "" : folderOfOutput + "/") + output.BaseName + extension;
            string target = RenderBundleWriter.OutputDirectory + "/" + output.BaseName + extension;
            if (entries.ContainsKey(subtitles) && ScenePlanValidator.IsPathBesideThePlan(subtitles) && named.All(n => n.Target != target))
                named.Add((subtitles, target));
        }
        long total = texts.Values.Sum(t => (long)Encoding.UTF8.GetByteCount(t));
        foreach (var (path, _) in named)
        {
            if (!ScenePlanValidator.IsPathBesideThePlan(path))
                throw new RenderBundleRefusedException($"'{Shown(path)}' is not a path inside the bundle.");
            if (!entries.TryGetValue(path, out var entry))
                throw new RenderBundleRefusedException($"The bundle has no {Shown(path)}, which a plan names.");
            total += entry.Length;
        }
        if (total > limits.MaxUnpackedBytes)
            throw new RenderBundleRefusedException($"The plans and their files are {total} bytes, more than the {limits.MaxUnpackedBytes} allowed.");

        double work = scenes.Sum(s => (double)PlanOutputsFrames(s) * s.Look.Width * s.Look.Height * Math.Max(1, s.Look.Samples));
        if (work > limits.MaxWork)
            throw new RenderBundleRefusedException(
                $"The render is {work:0.###e0} frames × pixels × samples, more than the {limits.MaxWork:0.###e0} allowed.");

        // Unpack: the plans as they were written (their output in the bundle's own folder), and
        // the files they name; nothing else.
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        string root = Path.GetFullPath(folder);
        foreach (var (path, text) in texts)
            File.WriteAllText(Inside(root, path), text, new UTF8Encoding(false));
        long written = 0;
        foreach (var (path, target) in named)
            written += Extract(entries[path], Inside(root, target), limits.MaxUnpackedBytes - written);

        // What the files hold, as Blender will read it: images by their content, meshes by their data.
        foreach (var s in scenes) CheckImages(s, root);
        long triangles = 0;
        foreach (var s in scenes)
        {
            long sum = 0;
            foreach (var model in s.Models)
            {
                try { sum = checked(sum + BundleMeshes.Triangles(Inside(root, model.Path))); }
                catch (MeshFileException ex) { throw new RenderBundleRefusedException($"The mesh of model '{Shown(model.Id)}' ({Shown(model.Path)}) is refused: {ex.Message}."); }
                catch (OverflowException) { sum = long.MaxValue; }
            }
            triangles = Math.Max(triangles, sum);
        }
        if (triangles > limits.MaxTriangles)
            throw new RenderBundleRefusedException($"A scene has {triangles} triangles, more than the {limits.MaxTriangles} allowed.");

        return new UnpackedBundle(folder, scene, movie, segments, triangles, work);
    }

    /// <summary>Whether <paramref name="name"/> is the name a movie gives a segment's plan:
    /// <c>scene-plan.&lt;id&gt;.json</c> or its draft's (<see cref="MoviePlan.SegmentFileName"/>),
    /// the id of letters, digits, '_' and '-'. Such a name can never be a module Python imports.</summary>
    public static bool IsSegmentPlanName(string? name) => name is not null && SegmentPlanName().IsMatch(name);

    [GeneratedRegex(@"^scene-plan\.[A-Za-z0-9_-]{1,64}(\.draft)?\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPlanName();

    /// <summary>The entries of the zip by their path inside its one folder; refused when an
    /// entry is a link, its name leaves the folder or is not one every file system reads alike,
    /// or two entries have one name (whatever its case).</summary>
    private static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive archive)
    {
        var names = new List<(string[] Parts, ZipArchiveEntry Entry)>();
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (((entry.ExternalAttributes >> 16) & UnixKindMask) == UnixLink)
                throw new RenderBundleRefusedException($"The bundle's entry '{Shown(name)}' is a link; a bundle holds files only.");
            if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Contains('\0'))
                throw new RenderBundleRefusedException($"The bundle's entry '{Shown(name)}' is not a plain path inside it.");
            var parts = name.Split('/');
            if (parts.Any(p => p is ".." or "."))
                throw new RenderBundleRefusedException($"The bundle's entry '{Shown(name)}' leaves the bundle's folder.");
            if (name.EndsWith('/')) parts = parts[..^1];  // a folder
            if (parts.Any(p => p.Length == 0))
                throw new RenderBundleRefusedException($"The bundle's entry '{Shown(name)}' is not a plain path inside it.");
            if (!parts.All(PortableNames.IsPortable))
                throw new RenderBundleRefusedException(
                    $"The bundle's entry '{Shown(name)}' has a name that is not the same file everywhere (a device name of Windows, a trailing dot or space, or a character Windows refuses).");
            if (!name.EndsWith('/')) names.Add((parts, entry));
        }
        // A bundle holds one folder (named after its zip); the paths are those inside it.
        bool oneFolder = names.Count > 0 && names.All(n => n.Parts.Length > 1 && n.Parts[0] == names[0].Parts[0]);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (parts, entry) in names)
        {
            string path = string.Join('/', oneFolder ? parts[1..] : parts);
            if (!entries.TryAdd(path, entry))
                throw new RenderBundleRefusedException($"The bundle has two entries named '{Shown(path)}'.");
            if (!folded.Add(path))
                throw new RenderBundleRefusedException($"The bundle has two entries whose names differ only by their case ('{Shown(path)}').");
        }
        return entries;
    }

    /// <summary>
    /// The plan's text as it is played: its video output in <paramref name="container"/> (when
    /// one is asked for), and its output folder the bundle's own, with the folder the plan said
    /// (for what lies there in the bundle). A plan that is not JSON of a plan is left as it is,
    /// for the serializer to refuse.
    /// </summary>
    /// <exception cref="RenderBundleRefusedException">The plan's output folder is not inside the bundle.</exception>
    private static (string Text, string Said) Played(string text, PlanContainer? container, string where)
    {
        JsonObject? plan;
        try { plan = JsonNode.Parse(text) as JsonObject; }
        catch (System.Text.Json.JsonException) { return (text, ""); }
        if (plan?["output"] is not JsonObject output) return (text, "");
        string said = "";
        if (output["directory"] is { } directory)
        {
            if (directory.GetValueKind() != System.Text.Json.JsonValueKind.String) return (text, "");
            said = (string)directory!;
            if (!ScenePlanValidator.IsPathBesideThePlan(said))
                throw new RenderBundleRefusedException(
                    $"{Shown(where)}: the output directory '{Shown(said)}' does not lie inside the bundle; a bundle renders into its own folder.");
        }
        output["directory"] = RenderBundleWriter.OutputDirectory;
        if (container is { } wanted && output["kind"]?.GetValueKind() == System.Text.Json.JsonValueKind.String && (string?)output["kind"] == "video")
            output["container"] = wanted.ToString().ToLowerInvariant();
        return (plan.ToJsonString(), said);
    }

    private static ScenePlan ReadScene(string text, string where)
    {
        var plan = ScenePlanSerializer.Read(text);
        if (plan.ModuleFolders.Count > 0)
            throw new RenderBundleRefusedException(
                $"{Shown(where)}: the plan names module folders; a bundle's modules are code and are not loaded (the operator names extra module folders).");
        CheckFeatures(plan.Features, ScenePlan.PlayerFeatures, where);
        if (plan.Blend is not null)
            throw new RenderBundleRefusedException($"{Shown(where)}: the plan saves a Blender file; a bundle's render writes its output alone.");
        return plan;
    }

    private static void CheckFeatures(IReadOnlyList<string> features, IReadOnlyList<string> known, string where)
    {
        foreach (string feature in features)
            if (!known.Contains(feature, StringComparer.Ordinal))
                throw new RenderBundleRefusedException($"{Shown(where)}: the feature '{Shown(feature)}' is not one of the player's own.");
    }

    /// <summary>Every image of a scene by what it holds, as Blender will pick its decoder: the
    /// photos and the images over the picture as a plan's photos are checked, the images of the
    /// materials as PNG or JPEG, and the world's as one of the images a world takes.</summary>
    private static void CheckImages(ScenePlan plan, string root)
    {
        var photos = PlayerWriter.PhotosOf(plan);
        var materials = (plan.TexturedMaterials ?? []).Select(m => m.Image).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (path, what) in PlayerWriter.FilesOf(plan))
        {
            string file = Inside(root, path);
            try
            {
                if (photos.Contains(path)) PlanImage.Check(file);
                else if (path == plan.Look.World?.Image) PlanEnvironmentImage.Check(file);
                else if (materials.Contains(path))
                {
                    using var stream = File.OpenRead(file);
                    PlanImage.Identify(stream);
                }
            }
            catch (PlanImageException ex)
            {
                throw new RenderBundleRefusedException($"The file of {Shown(what)} ({Shown(path)}) is refused: {ex.Message}.");
            }
        }
    }

    private static int PlanOutputsFrames(ScenePlan plan) => plan.Output.Kind == PlanOutputKind.Still ? 1 : plan.Timeline.FrameCount;

    private static string Inside(string root, string path)
    {
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new RenderBundleRefusedException($"'{Shown(path)}' leaves the bundle's folder.");
        return full;
    }

    /// <summary>Copies an entry to <paramref name="target"/>, no more bytes than it says it has
    /// and than <paramref name="allowed"/>: an entry that holds more or less than it says, or
    /// whose CRC-32 is not the one it says, is refused.</summary>
    private static long Extract(ZipArchiveEntry entry, string target, long allowed)
    {
        if (entry.Length > allowed)
            throw new RenderBundleRefusedException($"The bundle's entry '{Shown(entry.FullName)}' holds more than is allowed.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var source = entry.Open();
        using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[81920];
        long copied = 0;
        uint crc = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > entry.Length) throw Lying(entry);
            crc = BundleZip.Crc32(crc, buffer.AsSpan(0, read));
            destination.Write(buffer, 0, read);
        }
        if (copied != entry.Length || crc != entry.Crc32) throw Lying(entry);
        return copied;
    }

    private static RenderBundleRefusedException Lying(ZipArchiveEntry entry)
        => new($"The bundle's entry '{Shown(entry.FullName)}' does not hold what the zip says it holds (its size or its CRC-32).");

    /// <summary>Text of the bundle, as a refusal quotes it.</summary>
    private static string Shown(string? text, int max = 200) => PortableNames.Printable(text, max);

    /// <summary>The faces a PLY file declares in its header; 0 for another kind of file, or a
    /// header that cannot be read. Reads the header alone, for an estimate; a bundle's limit is
    /// held on the faces counted from the data.</summary>
    public static long PlyFaces(string path)
    {
        if (!path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase)) return 0;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        return BundleMeshes.PlyDeclaredFaces(stream) ?? 0;
    }

    /// <summary>The triangles the mesh file at <paramref name="path"/> makes when Blender imports
    /// it, counted from its data: a PLY's faces (a face of n corners is n − 2), a GLB's
    /// primitives for every node that shows them.</summary>
    /// <exception cref="RenderBundleRefusedException">They cannot be counted, or the file is not
    /// one a bundle may hold (a GLB that names other files, or uses an extension that is not
    /// about materials).</exception>
    public static long Triangles(string path)
    {
        try { return BundleMeshes.Triangles(path); }
        catch (MeshFileException ex) { throw new RenderBundleRefusedException($"The mesh {Shown(Path.GetFileName(path))} is refused: {ex.Message}."); }
    }
}
