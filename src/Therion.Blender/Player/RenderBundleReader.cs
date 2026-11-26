// Takes a render bundle that came from elsewhere (a render worker's inbox) apart as data. A bundle
// is a scene plan or a film, its meshes and images, and the player with its run scripts; the
// player and the scripts are code, and none of it is used: the plans are played by the player of
// whoever reads the bundle. So only the plans and the files they name are unpacked, after they
// were checked: every path relative and inside the folder, no entry that leaves it and no link,
// no module folders and no feature outside the built-in ones, plans that the validators pass, and
// limits on sizes, triangles and work, all before any Blender starts.

using System.IO.Compression;
using System.Text;
using Therion.Blender.Plan;

namespace Therion.Blender.Player;

/// <summary>What a bundle from elsewhere may hold at most.</summary>
public sealed record RenderBundleLimits
{
    /// <summary>The zip file itself.</summary>
    public long MaxZipBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    /// <summary>What is unpacked of it: the plans and the files they name.</summary>
    public long MaxUnpackedBytes { get; init; } = 16L * 1024 * 1024 * 1024;

    /// <summary>Entries in the zip.</summary>
    public int MaxEntries { get; init; } = 20_000;

    /// <summary>Triangles of the meshes of one scene, read from their PLY headers.</summary>
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
public static class RenderBundleReader
{
    /// <summary>A Unix file mode's kind bits, and that of a symbolic link, as a zip's external
    /// attributes carry them in their upper half.</summary>
    private const int UnixKindMask = 0xF000;
    private const int UnixLink = 0xA000;

    /// <summary>
    /// Checks the bundle at <paramref name="zipPath"/> and unpacks its plans and the files they
    /// name into <paramref name="folder"/> (which is emptied first). Nothing else of the bundle
    /// is unpacked: its player, its scripts and anything a plan does not name stay in the zip.
    /// <paramref name="choice"/> says which plan is played (it is unpacked as
    /// <c>scene-plan.json</c>) and may set the movie's container.
    /// </summary>
    /// <exception cref="RenderBundleRefusedException">The bundle breaks a rule; the message says which.</exception>
    public static UnpackedBundle Unpack(string zipPath, string folder, RenderBundleLimits? limits = null, RenderBundleChoice? choice = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        limits ??= new RenderBundleLimits();
        choice ??= new RenderBundleChoice();
        if (choice.Plan is not (ScenePlanSerializer.FileName or RenderBundleWriter.DraftPlanFileName))
            throw new RenderBundleRefusedException($"'{choice.Plan}' is not a plan a bundle is played by.");
        var info = new FileInfo(zipPath);
        if (!info.Exists) throw new RenderBundleRefusedException("The bundle is not there.");
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null)
            throw new RenderBundleRefusedException("The bundle is a link; a bundle is a file.");
        if (info.Length > limits.MaxZipBytes)
            throw new RenderBundleRefusedException($"The bundle is {info.Length} bytes, more than the {limits.MaxZipBytes} allowed.");

        ZipArchive archive;
        try { archive = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException ex) { throw new RenderBundleRefusedException("The bundle is not a zip file: " + ex.Message); }
        using (archive)
        {
            if (archive.Entries.Count > limits.MaxEntries)
                throw new RenderBundleRefusedException($"The bundle has {archive.Entries.Count} entries, more than the {limits.MaxEntries} allowed.");
            var entries = Entries(archive);

            string Text(string path)
            {
                if (!entries.TryGetValue(path, out var entry))
                    throw new RenderBundleRefusedException($"The bundle has no {path}.");
                if (entry.Length > 64L * 1024 * 1024)
                    throw new RenderBundleRefusedException($"{path} is larger than a plan can be.");
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            }

            // The plans first: the movie's, or the scene's, and then each segment's.
            string top = WithContainer(Text(choice.Plan), choice.Container);
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
                        if (!ScenePlanValidator.IsPathBesideThePlan(segment.Plan) || segment.Plan.Contains('/'))
                            throw new RenderBundleRefusedException($"The plan of segment '{segment.Id}' is not a file beside the movie's plan.");
                        string text = Text(segment.Plan);
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
            catch (ScenePlanFormatException ex) { throw new RenderBundleRefusedException("A plan of the bundle cannot be read: " + ex.Message); }
            catch (ArgumentException ex) { throw new RenderBundleRefusedException(ex.Message.Split(" (Parameter", 2)[0]); }

            var scenes = movie is null ? [scene] : segments;
            // The files the plans name, each once, all of them in the bundle and within the limit.
            var named = scenes.SelectMany(PlayerWriter.FilesOf).Select(f => f.Path).Distinct(StringComparer.Ordinal).ToList();
            // The subtitles of the captions, which a bundle has where its output will be: text,
            // handed back beside the movie.
            var output = movie?.Output ?? scene.Output;
            foreach (string extension in new[] { Subtitles.SrtExtension, Subtitles.VttExtension })
            {
                string folderOfOutput = (output.Directory ?? "").Replace('\\', '/').Trim('/');
                string subtitles = (folderOfOutput is "" or "." ? "" : folderOfOutput + "/") + output.BaseName + extension;
                if (entries.ContainsKey(subtitles) && ScenePlanValidator.IsPathBesideThePlan(subtitles) && !named.Contains(subtitles))
                    named.Add(subtitles);
            }
            long total = texts.Values.Sum(t => (long)Encoding.UTF8.GetByteCount(t));
            foreach (string path in named)
            {
                if (!ScenePlanValidator.IsPathBesideThePlan(path))
                    throw new RenderBundleRefusedException($"'{path}' is not a path inside the bundle.");
                if (!entries.TryGetValue(path, out var entry))
                    throw new RenderBundleRefusedException($"The bundle has no {path}, which a plan names.");
                total += entry.Length;
            }
            if (total > limits.MaxUnpackedBytes)
                throw new RenderBundleRefusedException($"The plans and their files are {total} bytes, more than the {limits.MaxUnpackedBytes} allowed.");

            double work = scenes.Sum(s => (double)PlanOutputsFrames(s) * s.Look.Width * s.Look.Height * Math.Max(1, s.Look.Samples));
            if (work > limits.MaxWork)
                throw new RenderBundleRefusedException(
                    $"The render is {work:0.###e0} frames × pixels × samples, more than the {limits.MaxWork:0.###e0} allowed.");

            // Unpack: the plans as they were written, and the files they name; nothing else.
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            string root = Path.GetFullPath(folder);
            foreach (var (path, text) in texts)
                File.WriteAllText(Inside(root, path), text, new UTF8Encoding(false));
            long written = 0;
            foreach (string path in named)
                written += Extract(entries[path], Inside(root, path), limits.MaxUnpackedBytes - written);

            long triangles = 0;
            foreach (var s in scenes)
                triangles = Math.Max(triangles, s.Models.Sum(m => PlyFaces(Inside(root, m.Path))));
            if (triangles > limits.MaxTriangles)
                throw new RenderBundleRefusedException($"A scene has {triangles} triangles, more than the {limits.MaxTriangles} allowed.");

            return new UnpackedBundle(folder, scene, movie, segments, triangles, work);
        }
    }

    /// <summary>The entries of the zip by their path inside its one folder; refused when an
    /// entry is a link, its name leaves the folder, or two entries have one name.</summary>
    private static Dictionary<string, ZipArchiveEntry> Entries(ZipArchive archive)
    {
        var names = new List<(string[] Parts, ZipArchiveEntry Entry)>();
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName;
            if (((entry.ExternalAttributes >> 16) & UnixKindMask) == UnixLink)
                throw new RenderBundleRefusedException($"The bundle's entry '{name}' is a link; a bundle holds files only.");
            if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Contains('\0'))
                throw new RenderBundleRefusedException($"The bundle's entry '{name}' is not a plain path inside it.");
            var parts = name.Split('/');
            if (parts.Any(p => p is ".." or "."))
                throw new RenderBundleRefusedException($"The bundle's entry '{name}' leaves the bundle's folder.");
            if (name.EndsWith('/')) continue;  // a folder
            if (parts.Any(p => p.Length == 0))
                throw new RenderBundleRefusedException($"The bundle's entry '{name}' is not a plain path inside it.");
            names.Add((parts, entry));
        }
        // A bundle holds one folder (named after its zip); the paths are those inside it.
        bool oneFolder = names.Count > 0 && names.All(n => n.Parts.Length > 1 && n.Parts[0] == names[0].Parts[0]);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var (parts, entry) in names)
        {
            string path = string.Join('/', oneFolder ? parts[1..] : parts);
            if (!entries.TryAdd(path, entry))
                throw new RenderBundleRefusedException($"The bundle has two entries named '{path}'.");
        }
        return entries;
    }

    /// <summary>The plan's text with its video output in <paramref name="container"/>; as it is
    /// when it has no video output or no container is asked for.</summary>
    private static string WithContainer(string text, PlanContainer? container)
    {
        if (container is not { } wanted) return text;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(text) is not System.Text.Json.Nodes.JsonObject plan
                || plan["output"] is not System.Text.Json.Nodes.JsonObject output
                || output["kind"]?.GetValueKind() != System.Text.Json.JsonValueKind.String
                || (string?)output["kind"] != "video")
                return text;
            output["container"] = wanted.ToString().ToLowerInvariant();
            return plan.ToJsonString();
        }
        catch (System.Text.Json.JsonException) { return text; }
    }

    private static ScenePlan ReadScene(string text, string where)
    {
        var plan = ScenePlanSerializer.Read(text);
        if (plan.ModuleFolders.Count > 0)
            throw new RenderBundleRefusedException(
                $"{where}: the plan names module folders; a bundle's modules are code and are not loaded (the operator names extra module folders).");
        CheckFeatures(plan.Features, ScenePlan.PlayerFeatures, where);
        if (plan.Blend is not null)
            throw new RenderBundleRefusedException($"{where}: the plan saves a Blender file; a bundle's render writes its output alone.");
        return plan;
    }

    private static void CheckFeatures(IReadOnlyList<string> features, IReadOnlyList<string> known, string where)
    {
        foreach (string feature in features)
            if (!known.Contains(feature, StringComparer.Ordinal))
                throw new RenderBundleRefusedException($"{where}: the feature '{feature}' is not one of the player's own.");
    }

    private static int PlanOutputsFrames(ScenePlan plan) => plan.Output.Kind == PlanOutputKind.Still ? 1 : plan.Timeline.FrameCount;

    private static string Inside(string root, string path)
    {
        string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new RenderBundleRefusedException($"'{path}' leaves the bundle's folder.");
        return full;
    }

    /// <summary>Copies an entry to <paramref name="target"/>, no more bytes than it says it has
    /// and than <paramref name="allowed"/>: a zip that lies about its sizes is refused.</summary>
    private static long Extract(ZipArchiveEntry entry, string target, long allowed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        long limit = Math.Min(entry.Length, allowed);
        using var source = entry.Open();
        using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += read;
            if (copied > limit)
                throw new RenderBundleRefusedException($"The bundle's entry '{entry.FullName}' holds more than it says, or more than is allowed.");
            destination.Write(buffer, 0, read);
        }
        return copied;
    }

    /// <summary>The faces a PLY file declares in its header; 0 for another kind of file.</summary>
    public static long PlyFaces(string path)
    {
        if (!path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase)) return 0;
        using var stream = File.OpenRead(path);
        var line = new StringBuilder();
        long faces = 0;
        bool first = true;
        for (int lines = 0; lines < 200; )
        {
            int b = stream.ReadByte();
            if (b < 0) break;
            if (b != '\n') { if (line.Length < 256) line.Append((char)b); continue; }
            string text = line.ToString().TrimEnd('\r');
            line.Clear();
            lines++;
            if (first && text != "ply") return 0;
            first = false;
            if (text == "end_header") break;
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["element", "face", var count]
                && long.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n))
                faces = n;
        }
        return faces;
    }
}
