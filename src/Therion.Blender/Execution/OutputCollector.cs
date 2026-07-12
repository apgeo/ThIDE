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
    public static OutputCollection Collect(OutputSpec output, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(output);
        string dir = output.OutputDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return new OutputCollection([], false, "The output directory does not exist.");

        return output.Kind switch
        {
            OutputKind.Video => CollectGlob(dir, output.BaseName, ContainerExtension(output.Container), expected: 1),
            OutputKind.Still => CollectStill(dir, output.BaseName),
            OutputKind.FrameSequence => CollectGlob(dir, output.BaseName, ".png", expected: Math.Max(1, frameCount)),
            _ => OutputCollection.Empty,
        };
    }

    private static OutputCollection CollectStill(string dir, string baseName)
    {
        var path = Path.Combine(dir, baseName + ".png");
        var info = SafeInfo(path);
        if (info is { Exists: true, Length: > 0 })
            return new OutputCollection([new CollectedOutput(path, info.Length)], Verified: true);
        return new OutputCollection([], false, "The still image was not written.");
    }

    private static OutputCollection CollectGlob(string dir, string baseName, string extension, int expected)
    {
        // Blender may decorate the base name (e.g. a frame range on a video, the frame number
        // on a sequence), so match by prefix + extension rather than an exact name.
        IEnumerable<string> matches;
        try { matches = Directory.EnumerateFiles(dir, baseName + "*" + extension); }
        catch (IOException) { return new OutputCollection([], false, "The output directory could not be read."); }

        var files = new List<CollectedOutput>();
        bool sawEmpty = false;
        foreach (var path in matches.OrderBy(p => p, StringComparer.Ordinal))
        {
            var info = SafeInfo(path);
            if (info is null) continue;
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
