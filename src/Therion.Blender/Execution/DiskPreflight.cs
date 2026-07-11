// Disk preflight (BA-B10, doc 04). A rough output-size estimate + a free-space check so a
// long frame-sequence render fails fast with "not enough disk" instead of dying mid-way.
// Pure estimate (unit-tested); the free-space probe is separable so the runner is testable.

namespace Therion.Blender.Execution;

/// <summary>Estimates render output size and checks free disk space.</summary>
public static class DiskPreflight
{
    /// <summary>Extra headroom multiplier over the estimate before a render is allowed.</summary>
    public const double SafetyFactor = 1.5;

    /// <summary>
    /// A rough upper estimate of bytes the render will write: a frame sequence keeps every
    /// PNG (heavy), a video is FFmpeg-compressed (light), a single still is one image.
    /// Deliberately generous — the point is to catch "nowhere near enough", not to be exact.
    /// </summary>
    public static long EstimateBytes(int frameCount, int width, int height, OutputKind kind)
    {
        long pixels = Math.Max(1L, width) * Math.Max(1L, height);
        long frames = Math.Max(1, frameCount);
        double perFrame = kind switch
        {
            OutputKind.FrameSequence => pixels * 4 * 0.6, // compressed RGBA PNG
            OutputKind.Still => pixels * 4 * 0.6,
            OutputKind.Video => pixels * 0.2,             // H.264-ish per frame
            _ => pixels * 0.5,
        };
        return (long)(perFrame * frames);
    }

    /// <summary>True when <paramref name="freeBytes"/> comfortably covers the estimate.</summary>
    public static bool IsSufficient(long estimatedBytes, long freeBytes, double safetyFactor = SafetyFactor)
        => freeBytes >= (long)(estimatedBytes * safetyFactor);

    /// <summary>Free bytes on the volume that holds <paramref name="path"/>, or null when it
    /// can't be determined (network path, permissions) — in which case the runner proceeds.</summary>
    public static long? TryGetFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
