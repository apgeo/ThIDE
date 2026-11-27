// Output collection + verification (BA-B11, doc 04). After a render, read back what actually
// landed on disk for the spec's output kind and check the files are non-empty. The script
// generator (BA-B5) sets the output paths; this is the independent read-back the runner uses
// to fill RenderResult.OutputPaths and to catch a "reported success but wrote nothing" case.

namespace Therion.Blender.Execution;

/// <summary>A produced output file and its size.</summary>
public readonly record struct CollectedOutput(string Path, long SizeBytes)
{
    public bool IsEmpty => SizeBytes <= 0;
}

/// <summary>The outcome of collecting a render's outputs.</summary>
public sealed record OutputCollection(
    IReadOnlyList<CollectedOutput> Files,
    bool Verified,
    string? Problem = null)
{
    /// <summary>At least one non-empty output file was found.</summary>
    public bool HasOutputs => Files.Count > 0;

    public static readonly OutputCollection Empty = new([], false, "No output files were found.");
}

/// <summary>Reads back and verifies a render's output files.</summary>
public static class OutputCollector
{
    /// <summary>
    /// Collects the non-empty output files the render should have written into
    /// <see cref="OutputSpec.OutputDirectory"/>. <paramref name="frameCount"/> is the expected
    /// frame count (a sequence is only <see cref="OutputCollection.Verified"/> when at least
    /// that many frames landed).
    /// </summary>
    public static OutputCollection Collect(OutputSpec output, int frameCount) => Collect(output, frameCount, null);

    /// <summary>
    /// <see cref="Collect(OutputSpec, int)"/>, leaving out a movie or a still that
    /// <paramref name="before"/> (a <see cref="Snapshot"/> taken before the render started) shows
    /// there already, unchanged: an earlier render's, which this one did not write. A frame that
    /// was there unchanged is left out only when its number is above <paramref name="frameCount"/>:
    /// the later frames of a longer earlier render in the same folder are not this one's, while
    /// the frames a render goes on with are.
    /// </summary>
    public static OutputCollection Collect(OutputSpec output, int frameCount, IReadOnlyDictionary<string, (long Size, DateTime WrittenUtc)>? before)
    {
        ArgumentNullException.ThrowIfNull(output);
        string dir = output.OutputDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new OutputCollection([], false, "The output directory does not exist.");

        return output.Kind switch
        {
            OutputKind.Video => CollectVideoOrFrames(dir, output.BaseName, ContainerExtension(output.Container), frameCount, before),
            OutputKind.Still => CollectStill(dir, output.BaseName, before),
            OutputKind.FrameSequence => CollectGlob(dir, output.BaseName, ".png", expected: Math.Max(1, frameCount), before, lastFrame: frameCount),
            _ => OutputCollection.Empty,
        };
    }

    /// <summary>The files <paramref name="output"/> could collect now, with their size and the
    /// time they were written: what a render that starts now finds there already.</summary>
    public static IReadOnlyDictionary<string, (long Size, DateTime WrittenUtc)> Snapshot(OutputSpec output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var found = new Dictionary<string, (long, DateTime)>(StringComparer.Ordinal);
        string dir = output.OutputDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return found;
        string[] extensions = output.Kind == OutputKind.Video ? [ContainerExtension(output.Container), ".png"] : [".png"];
        try
        {
            foreach (string extension in extensions)
                foreach (string path in Directory.EnumerateFiles(dir, output.BaseName + "*" + extension))
                    if (SafeInfo(path) is { } info) found[path] = (info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found;
    }

    private static OutputCollection CollectVideoOrFrames(
        string dir, string baseName, string extension, int frameCount, IReadOnlyDictionary<string, (long, DateTime)>? before)
    {
        var video = CollectGlob(dir, baseName, extension, expected: 1, before);
        if (video.HasOutputs) return video;
        // A Blender build without an FFMPEG encoder falls back to a PNG frame sequence
        // (ScriptGenerator.EmitVideoOutput); collect those so the render isn't reported as
        // "wrote no output". The script also emits a THIDE: warning explaining the fallback.
        var frames = CollectGlob(dir, baseName, ".png", expected: Math.Max(1, frameCount), before, lastFrame: frameCount);
        return frames.HasOutputs ? frames : video;
    }

    private static OutputCollection CollectStill(string dir, string baseName, IReadOnlyDictionary<string, (long, DateTime)>? before)
    {
        var path = Path.Combine(dir, baseName + ".png");
        var info = SafeInfo(path);
        if (info is { Exists: true, Length: > 0 } && !Unchanged(before, path, info))
            return new OutputCollection([new CollectedOutput(path, info.Length)], Verified: true);
        return new OutputCollection([], false, "The still image was not written.");
    }

    private static bool Unchanged(IReadOnlyDictionary<string, (long Size, DateTime WrittenUtc)>? before, string path, FileInfo info)
        => before is not null && before.TryGetValue(path, out var was) && was.Size == info.Length && was.WrittenUtc == info.LastWriteTimeUtc;

    /// <summary>The number a frame's name ends with, after the base name; null for none.</summary>
    private static long? FrameNumber(string name, int baseLength, int extensionLength)
    {
        var middle = name.AsSpan(baseLength, name.Length - baseLength - extensionLength);
        int end = middle.Length;
        int start = end;
        while (start > 0 && char.IsAsciiDigit(middle[start - 1])) start--;
        return start < end && long.TryParse(middle[start..end], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long n) ? n : null;
    }

    private static OutputCollection CollectGlob(
        string dir, string baseName, string extension, int expected, IReadOnlyDictionary<string, (long, DateTime)>? before, int lastFrame = 0)
    {
        // Blender may decorate the base name (e.g. a frame range on a video, the frame number
        // on a sequence), so match by prefix + extension rather than an exact name. What stands
        // between the two is then only digits, '_' and '-': a draft of the same render, written
        // beside it as <base name>-draft, is another render's output.
        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(dir, baseName + "*" + extension)
                .Where(path => Path.GetFileName(path) is { } name
                               && name.AsSpan(baseName.Length, name.Length - baseName.Length - extension.Length)
                                   .IndexOfAnyExcept("0123456789_-") < 0)
                .ToList();
        }
        catch (IOException) { return new OutputCollection([], false, "The output directory could not be read."); }

        var files = new List<CollectedOutput>();
        bool sawEmpty = false;
        foreach (var path in matches.OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = SafeInfo(path);
            if (info is null) continue;
            if (Unchanged(before, path, info)
                && (lastFrame <= 0 || FrameNumber(Path.GetFileName(path), baseName.Length, extension.Length) is not { } number || number > lastFrame))
                continue;
            if (info.Length > 0) files.Add(new CollectedOutput(path, info.Length));
            else sawEmpty = true;
        }

        if (files.Count == 0)
            return new OutputCollection([], false, sawEmpty ? "The output files are empty." : "No output files were found.");

        bool verified = files.Count >= expected;
        string? problem = verified ? null : $"Expected {expected} output files but found {files.Count}.";
        return new OutputCollection(files, verified, problem);
    }

    private static FileInfo? SafeInfo(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>File extension for a video container (matches the emitter's naming, BA-B5).</summary>
    public static string ContainerExtension(VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => ".mp4",
        VideoContainer.Mkv => ".mkv",
        VideoContainer.WebM => ".webm",
        _ => ".mp4",
    };
}
