// Writes what Blender needs to play a scene plan: either a job folder (the plan, the player and
// the meshes as files) or one script that carries all of it. Both are started the same way:
//   blender -b --factory-startup --python render.py
// and report progress with the THIDE: lines the generated scripts print, so whatever runs a
// generated script runs these.

using System.Text;
using Therion.Blender.Emit;
using Therion.Blender.Plan;

namespace Therion.Blender.Player;

/// <summary>A job folder as <see cref="PlayerWriter.WriteJob"/> left it.</summary>
/// <param name="Folder">The folder; the plan's relative paths start here.</param>
/// <param name="ScriptPath">The script to start Blender with.</param>
/// <param name="PlanPath">The plan, <c>scene-plan.json</c>.</param>
public sealed record PlayerJob(string Folder, string ScriptPath, string PlanPath);

/// <summary>Puts a <see cref="ScenePlan"/> and the player together, ready to run.</summary>
public static class PlayerWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes a job folder: <c>scene-plan.json</c>, <c>render.py</c> and the player's package
    /// <c>thide_player/</c>, which replaces one that is already there.
    /// </summary>
    /// <param name="files">Files to copy in first, as plan path → the file to copy: the meshes,
    /// the images of the materials, the photos and the world's image, unless they are in the
    /// folder already.</param>
    /// <exception cref="ArgumentException">The plan is invalid, a path of
    /// <paramref name="files"/> does not lie in the folder, a photo's file is not an image a
    /// plan carries (<see cref="PlanImage"/>), or the world's is not one
    /// (<see cref="PlanEnvironmentImage"/>).</exception>
    /// <exception cref="FileNotFoundException">A file the plan names is not in the folder, or a
    /// file to copy is not there.</exception>
    public static PlayerJob WriteJob(ScenePlan plan, string folder, IReadOnlyDictionary<string, string>? files = null)
    {
        ThrowIfInvalid(plan);
        WriteFilesAndPlayer([plan], folder, files);
        string planFile = Path.Combine(folder, ScenePlanSerializer.FileName);
        ScenePlanSerializer.WriteFile(plan, planFile);
        return new PlayerJob(folder, Path.Combine(folder, PlayerFiles.EntryFileName), planFile);
    }

    /// <summary>
    /// Writes the job folder of a film of several scenes: the movie's plan as
    /// <c>scene-plan.json</c>, the plan of every segment beside it under the name the movie gives
    /// it (<c>scene-plan.s01.json</c> onwards), <c>render.py</c> and the player's package. The
    /// segments share the files they name: one mesh serves them all.
    /// </summary>
    /// <param name="files">As for <see cref="WriteJob(ScenePlan, string, IReadOnlyDictionary{string, string}?)"/>.</param>
    /// <exception cref="ArgumentException">The movie's plan or a segment's is invalid, or a file
    /// is not one a plan carries.</exception>
    /// <exception cref="FileNotFoundException">A file a plan names is not in the folder.</exception>
    public static PlayerJob WriteJob(StoryboardPlan film, string folder, IReadOnlyDictionary<string, string>? files = null)
    {
        ThrowIfInvalid(film);
        WriteFilesAndPlayer(film.Segments, folder, files);
        for (int i = 0; i < film.Segments.Count; i++)
            ScenePlanSerializer.WriteFile(film.Segments[i], InFolder(folder, film.Movie.Segments[i].Plan));
        string planFile = Path.Combine(folder, ScenePlanSerializer.FileName);
        ScenePlanSerializer.WriteFile(film.Movie, planFile);
        return new PlayerJob(folder, Path.Combine(folder, PlayerFiles.EntryFileName), planFile);
    }

    /// <summary>
    /// Writes the player (<c>render.py</c> and <c>thide_player/</c>, replacing what is there)
    /// into a folder that holds <paramref name="plans"/> and the files they name already, and
    /// leaves the plans' files as they are.
    /// </summary>
    /// <exception cref="FileNotFoundException">A file a plan names is not in the folder.</exception>
    /// <exception cref="ArgumentException">A photo's file is not an image a plan carries, or the
    /// world's is not one.</exception>
    public static void WritePlayer(IReadOnlyList<ScenePlan> plans, string folder) => WriteFilesAndPlayer(plans, folder, null);

    /// <summary>Checks a movie and the plans of its segments as a film's job is checked.</summary>
    /// <exception cref="ArgumentException">They are invalid; the message says each problem.</exception>
    public static void Check(MoviePlan movie, IReadOnlyList<ScenePlan> segments)
        => ThrowIfInvalid(new StoryboardPlan { Movie = movie, Segments = segments });

    /// <summary>Checks a scene plan as a job's is checked.</summary>
    /// <exception cref="ArgumentException">It is invalid.</exception>
    public static void Check(ScenePlan plan) => ThrowIfInvalid(plan);

    private static void WriteFilesAndPlayer(IReadOnlyList<ScenePlan> plans, string folder, IReadOnlyDictionary<string, string>? files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);

        foreach (var (planPath, source) in files ?? new Dictionary<string, string>())
        {
            if (!ScenePlanValidator.IsPathBesideThePlan(planPath))
                throw new ArgumentException($"'{planPath}' is not a path inside the job folder.", nameof(files));
            if (!File.Exists(source))
                throw new FileNotFoundException($"The file for '{planPath}' does not exist: {source}", source);
            string target = InFolder(folder, planPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            var photos = PhotosOf(plan);
            foreach (var (planPath, what) in FilesOf(plan))
            {
                if (!seen.Add(planPath)) continue;
                string path = InFolder(folder, planPath);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"The file of {what} is not in the job folder: {path}", path);
                if (photos.Contains(planPath)) CheckPhoto(planPath, what, () => File.OpenRead(path));
                if (planPath == plan.Look.World?.Image) CheckEnvironment(planPath, what, () => File.OpenRead(path));
            }
        }

        string package = Path.Combine(folder, PlayerFiles.PackageName);
        if (Directory.Exists(package)) Directory.Delete(package, recursive: true);
        foreach (var file in PlayerFiles.JobFiles)
        {
            string target = InFolder(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, file.Text, Utf8NoBom);
        }
    }

    /// <summary>
    /// The plan and the player as one Python script. The files of the plan (meshes, images)
    /// that are given in <paramref name="files"/> travel inside the script; the others are
    /// looked for beside it.
    /// </summary>
    /// <param name="files">Files to carry, as plan path → content.</param>
    /// <exception cref="ArgumentException">The plan is invalid, or a path of
    /// <paramref name="files"/> is not one the plan names.</exception>
    public static string SingleFile(ScenePlan plan, IReadOnlyDictionary<string, byte[]>? files = null)
    {
        ThrowIfInvalid(plan);
        files ??= new Dictionary<string, byte[]>();
        var named = FilesOf(plan).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (string path in files.Keys)
            if (!named.Contains(path))
                throw new ArgumentException($"No model, material, photo, image or world of the plan has the path '{path}'.", nameof(files));
        var photos = PhotosOf(plan);
        foreach (var (path, what) in FilesOf(plan))
        {
            if (!files.TryGetValue(path, out byte[]? content)) continue;
            if (photos.Contains(path)) CheckPhoto(path, what, () => new MemoryStream(content, writable: false));
            if (path == plan.Look.World?.Image) CheckEnvironment(path, what, () => new MemoryStream(content, writable: false));
        }

        var text = new StringBuilder();
        text.Append("# A scene plan and the ThIDE player that renders it, in one file.\n");
        text.Append("#\n");
        text.Append("#   blender -b --factory-startup --python <this file>\n");
        text.Append("#\n");
        text.Append("# PLAN is the plan (JSON), FILES the files it names that travel with it (base64), and\n");
        text.Append("# SOURCES the player: one text per Python module, loaded by the code at the end.\n");
        text.Append("#\n");
        text.Append("# The player is part of ThIDE (https://github.com/apgeo/ThIDE). Its code, in SOURCES and at\n");
        text.Append("# the end, is subject to the Mozilla Public License, v. 2.0: https://mozilla.org/MPL/2.0/.\n");
        text.Append('\n');

        // In a raw string nothing is an escape, so the JSON arrives as it is. An apostrophe can
        // stand only inside a JSON string, where \u0027 says the same and cannot end the quotes.
        text.Append("PLAN = r'''\n").Append(ScenePlanSerializer.Write(plan).Replace("'", "\\u0027")).Append("'''\n\n");

        text.Append("FILES = {\n");
        foreach (var (path, content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            text.Append("    ").Append(PyWriter.Str(path)).Append(": \"").Append(Convert.ToBase64String(content)).Append("\",\n");
        text.Append("}\n\n");

        text.Append("SOURCES = {\n");
        foreach (var (module, source) in PlayerFiles.PackageModules)
        {
            if (source.Contains("'''", StringComparison.Ordinal))
                throw new InvalidOperationException($"The player module {module} cannot be carried in a script: it contains '''.");
            text.Append("    ").Append(PyWriter.Str(module)).Append(": r'''\n").Append(source).Append("''',\n");
        }
        text.Append("}\n\n");

        text.Append(PlayerFiles.SingleFileStart);
        return text.ToString();
    }

    /// <summary>The files a plan names, each with what it is the file of: every model's mesh,
    /// every material's image, every photo's and the world's.</summary>
    public static IReadOnlyList<(string Path, string What)> FilesOf(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var files = plan.Models.Select(m => (m.Path, $"model '{m.Id}'")).ToList();
        foreach (var material in plan.TexturedMaterials ?? [])
            if (material.Image is { } image && !files.Any(f => f.Path == image))
                files.Add((image, $"material '{material.Id}'"));
        foreach (var annotation in plan.Annotations ?? [])
            if (annotation?.Image is { } image && !files.Any(f => f.Path == image))
                files.Add((image, $"photo '{annotation.Id}'"));
        if (plan.Look?.World?.Image is { } world && !files.Any(f => f.Path == world))
            files.Add((world, "the world"));
        foreach (var item in plan.Texts?.Items ?? [])
            if (item?.Image is { } image && !files.Any(f => f.Path == image))
                files.Add((image, $"image '{item.Id}'"));
        return files;
    }

    /// <summary>The plan paths of the images that are photos: the files that are checked
    /// before they travel with a plan.</summary>
    internal static HashSet<string> PhotosOf(ScenePlan plan)
        => (plan.Annotations ?? []).Where(a => a?.Image is not null).Select(a => a.Image!)
            .Concat((plan.Texts?.Items ?? []).Where(i => i?.Image is not null).Select(i => i.Image!))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Checks that a photo's file is an image a plan carries.</summary>
    /// <exception cref="ArgumentException">It is not; the message names the plan path.</exception>
    internal static void CheckPhoto(string planPath, string what, Func<Stream> open)
    {
        try
        {
            using var stream = open();
            PlanImage.Check(stream);
        }
        catch (PlanImageException ex)
        {
            throw new ArgumentException($"The file of {what} ({planPath}) cannot travel with the plan: {ex.Message}.", nameof(planPath));
        }
    }

    /// <summary>Checks that the file of the world's image is one a plan carries.</summary>
    /// <exception cref="ArgumentException">It is not; the message names the plan path.</exception>
    internal static void CheckEnvironment(string planPath, string what, Func<Stream> open)
    {
        try
        {
            using var stream = open();
            PlanEnvironmentImage.Check(stream);
        }
        catch (PlanImageException ex)
        {
            throw new ArgumentException($"The file of {what} ({planPath}) cannot travel with the plan: {ex.Message}.", nameof(planPath));
        }
    }

    /// <summary>Checks the plan of a film and the plans of its segments: each valid, one for
    /// every segment, with the frames and the size the movie takes them to have.</summary>
    /// <exception cref="ArgumentException">One of them is not; the message names it.</exception>
    internal static void ThrowIfInvalid(StoryboardPlan film)
    {
        ArgumentNullException.ThrowIfNull(film);
        var errors = MoviePlanValidator.Validate(film.Movie).Select(e => $"{e.Path}: {e.Message}").ToList();
        if (film.Segments.Count != film.Movie.Segments.Count)
            errors.Add($"segments: the movie has {film.Movie.Segments.Count} and there are plans for {film.Segments.Count}");
        else
            for (int i = 0; i < film.Segments.Count; i++)
            {
                var plan = film.Segments[i];
                string name = film.Movie.Segments[i].Plan;
                errors.AddRange(ScenePlanValidator.Validate(plan).Select(e => $"{name}: {e.Path}: {e.Message}"));
                if (plan.Timeline.Fps != film.Movie.Timeline.Fps || plan.Timeline.FrameCount != film.Movie.Segments[i].FrameCount)
                    errors.Add($"{name}: its timeline is not the one its segment has in the movie");
                if (plan.Look.Width != film.Width || plan.Look.Height != film.Height)
                    errors.Add($"{name}: its picture is not the size of the first segment's; a movie has one size");
            }
        if (errors.Count > 0)
            throw new ArgumentException("The film's plans are invalid: " + string.Join("; ", errors), nameof(film));
    }

    private static void ThrowIfInvalid(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var errors = ScenePlanValidator.Validate(plan);
        if (errors.Count > 0)
            throw new ArgumentException(
                "The scene plan is invalid: " + string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")),
                nameof(plan));
    }

    private static string InFolder(string folder, string planPath)
        => Path.Combine(folder, planPath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
}
