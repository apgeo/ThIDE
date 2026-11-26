// What a render will cost before it starts: frames, time, disk and memory. The time is frames ×
// seconds per frame, measured when a render has measured it, else assumed from the engine, the
// picture's size and its samples; the memory is Blender's peak for the heaviest scene, from what
// was measured on this module's own meshes and text objects. Every figure is rough and says so.

using System.Globalization;
using Therion.Blender.Plan;

namespace Therion.Blender.Execution;

/// <summary>The cost of a render, as far as it can be told before it starts.</summary>
public sealed record RenderEstimate
{
    /// <summary>Above this many triangles in one scene a render is warned of (a GPU's memory
    /// fills first).</summary>
    public const long WarnTriangles = 20_000_000;

    /// <summary>The frames the scenes render (the first stage of each, every segment of a film).</summary>
    public required int Frames { get; init; }

    /// <summary>Seconds a frame takes.</summary>
    public required double SecondsPerFrame { get; init; }

    /// <summary>True when <see cref="SecondsPerFrame"/> was measured, false when it is assumed.</summary>
    public required bool Measured { get; init; }

    /// <summary>Frames × seconds per frame.</summary>
    public double Seconds => Frames * SecondsPerFrame;

    /// <summary>What the render writes to disk: its frames (when it takes two stages) and its output.</summary>
    public required long DiskBytes { get; init; }

    /// <summary>Blender's peak memory for the heaviest scene.</summary>
    public required long MemoryBytes { get; init; }

    /// <summary>The triangles of the heaviest scene.</summary>
    public required long Triangles { get; init; }

    /// <summary>The text objects of the heaviest scene: labels, markers, annotations.</summary>
    public required int TextObjects { get; init; }

    /// <summary>More triangles in one scene than <see cref="WarnTriangles"/>.</summary>
    public bool TooManyTriangles => Triangles > WarnTriangles;

    // Measured on this module's own invented meshes (Blender 4.5, CPU): Blender's own share, and
    // what a million triangles and a text object add.
    private const double BaseMemory = 0.45e9;
    private const double MemoryPerMillionTriangles = 0.25e9;
    private const double MemoryPerTextObject = 0.45e6;

    // Seconds a frame takes at 1920×1080 on this kind of machine's CPU: the solid view of a
    // draft (measured), Cycles at 128 samples (measured on a flythrough), EEVEE (assumed).
    private const double FullHdPixels = 1920.0 * 1080.0;
    private const double WorkbenchSeconds = 0.3;
    private const double EeveeSeconds = 3.0;
    private const double CyclesSeconds = 57.0;
    private const int CyclesSamples = 128;

    /// <summary>The estimate for <paramref name="scenes"/> (one scene, or the segments of a
    /// film), whose output has <paramref name="outputFrames"/> frames.</summary>
    /// <param name="trianglesOf">The triangles of a model's mesh, by its plan path.</param>
    /// <param name="secondsPerFrame">Seconds per frame a render has measured; null assumes them.</param>
    public static RenderEstimate Of(
        IReadOnlyList<ScenePlan> scenes, int outputFrames, Func<string, long> trianglesOf, double? secondsPerFrame = null)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(trianglesOf);
        if (scenes.Count == 0) throw new ArgumentException("An estimate is of one scene or more.", nameof(scenes));
        int frames = scenes.Sum(PlanOutputs.StageFrameCount);
        double assumed = scenes.Sum(s => AssumedSecondsPerFrame(s.Look) * PlanOutputs.StageFrameCount(s)) / Math.Max(1, frames);
        long triangles = 0, memory = 0;
        int texts = 0;
        foreach (var scene in scenes)
        {
            long t = scene.Models.Sum(m => trianglesOf(m.Path));
            int n = (scene.Labels?.Texts?.Items.Count ?? 0) + (scene.Labels?.Markers?.Items.Count ?? 0) + (scene.Annotations?.Count ?? 0);
            long m = (long)(BaseMemory + MemoryPerMillionTriangles * t / 1e6 + MemoryPerTextObject * n);
            if (m > memory) (memory, triangles, texts) = (m, t, n);
        }
        var first = scenes[0];
        bool twoStages = scenes.Count > 1 || first.HasTexts;
        long disk = DiskPreflight.EstimateBytes(outputFrames, first.Look.Width, first.Look.Height, PlanOutputs.SpecOf(first, ".").Kind)
                    + (twoStages ? scenes.Sum(PlanOutputs.FrameBytes) : 0);
        return new RenderEstimate
        {
            Frames = frames,
            SecondsPerFrame = secondsPerFrame ?? assumed,
            Measured = secondsPerFrame is not null,
            DiskBytes = disk,
            MemoryBytes = memory,
            Triangles = triangles,
            TextObjects = texts,
        };
    }

    /// <summary>Seconds a frame of this look is assumed to take on a CPU.</summary>
    public static double AssumedSecondsPerFrame(PlanLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        double pixels = look.Width * (double)look.Height / FullHdPixels;
        return look.Engine switch
        {
            PlanEngine.Workbench => WorkbenchSeconds * pixels,
            PlanEngine.Eevee => EeveeSeconds * pixels,
            _ => CyclesSeconds * pixels * Math.Max(1, look.Samples) / CyclesSamples,
        };
    }

    /// <summary>One line in plain words.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"{Frames} frame(s) at {(Measured ? "" : "about ")}{SecondsPerFrame:0.##} s a frame{(Measured ? "" : " (assumed)")}: "
        + $"{Duration(Seconds)}; {DiskBytes / 1e9:0.##} GB of disk; {MemoryBytes / 1e9:0.##} GB of memory "
        + $"({Triangles / 1e6:0.##} M triangles, {TextObjects} text objects)");

    /// <summary>A time in hours and minutes, or in seconds below a minute.</summary>
    public static string Duration(double seconds) => seconds switch
    {
        < 60 => string.Create(CultureInfo.InvariantCulture, $"{seconds:0} s"),
        < 3600 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:0} min"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(seconds / 3600):0} h {seconds % 3600 / 60:00} min"),
    };

    /// <summary>The faces a PLY file declares in its header (this module writes triangles); 0
    /// when the file is not a PLY or cannot be read. Reads the header alone.</summary>
    public static long PlyFaces(string path)
    {
        try { return Player.RenderBundleReader.PlyFaces(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}

/// <summary>A render service that can say what a film will cost before it is rendered.</summary>
public interface IRenderEstimator
{
    /// <summary>What a render of <paramref name="storyboard"/> will cost: its frames, the time at
    /// the measured or assumed seconds a frame, the disk and the memory.</summary>
    /// <exception cref="ArgumentException">The storyboard is invalid, or cannot be planned for the model.</exception>
    Task<RenderEstimate> EstimateStoryboardAsync(Storyboard storyboard, RenderSource source, RenderOptions options, CancellationToken ct = default);
}
