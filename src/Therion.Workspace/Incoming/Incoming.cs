// The incoming folder: where survey exports land (from a phone sync, an e-mail, a colleague). When
// the project turns it on, new files there are offered for import once they have finished arriving.

namespace Therion.Workspace.Incoming;

public enum IncomingRoute { Ignore, LinkSurvey, SketchOnly, ImportSurvey, ImportGpx }

/// <summary>The project's incoming settings (project.json in its config folder).</summary>
public sealed class IncomingSettings
{
    public string Folder { get; set; } = "incoming";
    public bool Enabled { get; set; }
}

public sealed class ProjectSettingsFile
{
    public int Version { get; set; } = 1;
    public IncomingSettings Incoming { get; set; } = new();
}

public static class IncomingFiles
{
    /// <summary>What an arriving file is offered for, by extension.</summary>
    public static IncomingRoute Route(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".th" => IncomingRoute.LinkSurvey,
        ".th2" => IncomingRoute.SketchOnly,
        ".svx" or ".dat" => IncomingRoute.ImportSurvey,
        ".gpx" => IncomingRoute.ImportGpx,
        _ => IncomingRoute.Ignore,
    };

    /// <summary>The incoming folder of a project, or null when it is not turned on.</summary>
    public static string? FolderFor(string? root, ProjectSettingsFile? settings) =>
        root is null || settings?.Incoming is not { Enabled: true } incoming ? null
        : Path.GetFullPath(Path.Combine(root, string.IsNullOrWhiteSpace(incoming.Folder) ? "incoming" : incoming.Folder));
}

/// <summary>
/// Tells when a file has finished arriving: its size and modification time unchanged across two
/// looks at least <see cref="Interval"/> apart (a slow copy or a phone sync writes in bursts).
/// </summary>
public sealed class StableFileTracker(Func<string, (long Size, DateTime Modified)?> probe, Func<DateTime> clock)
{
    private readonly Dictionary<string, (long Size, DateTime Modified, DateTime SeenAt)> _seen = new(StringComparer.Ordinal);

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>True once the file has stood still for the interval; false while it may still be growing.</summary>
    public bool IsStable(string path)
    {
        if (probe(path) is not { } now)
        {
            _seen.Remove(path);
            return false;
        }
        var t = clock();
        if (_seen.TryGetValue(path, out var before) && before.Size == now.Size && before.Modified == now.Modified)
            return t - before.SeenAt >= Interval;
        _seen[path] = (now.Size, now.Modified, t);
        return false;
    }

    public void Forget(string path) => _seen.Remove(path);

    public void Clear() => _seen.Clear();
}
