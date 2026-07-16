// Blender version + installation model (BA-B10). The locator probes `blender --version`
// (which prints e.g. "Blender 4.5.1"), parses it here, and gates on the ≥4.2 minimum (D-02).

using System.Globalization;

namespace Therion.Blender.Execution;

/// <summary>A parsed Blender version, comparable so "newest wins".</summary>
public readonly record struct BlenderVersion(int Major, int Minor, int Patch) : IComparable<BlenderVersion>
{
    public int CompareTo(BlenderVersion other)
    {
        int m = Major.CompareTo(other.Major);
        if (m != 0) return m;
        int n = Minor.CompareTo(other.Minor);
        return n != 0 ? n : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(BlenderVersion a, BlenderVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(BlenderVersion a, BlenderVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(BlenderVersion a, BlenderVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BlenderVersion a, BlenderVersion b) => a.CompareTo(b) >= 0;

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    /// <summary>
    /// Parses the first "Blender X.Y[.Z]" (or a bare "X.Y[.Z]") out of
    /// <paramref name="versionOutput"/> — the first line of <c>blender --version</c>.
    /// Missing patch defaults to 0. Culture-invariant.
    /// </summary>
    public static bool TryParse(string? versionOutput, out BlenderVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(versionOutput)) return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            versionOutput, @"(\d+)\.(\d+)(?:\.(\d+))?", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) return false;

        int major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        version = new BlenderVersion(major, minor, patch);
        return true;
    }
}

/// <summary>A located, version-probed Blender executable.</summary>
public sealed record BlenderInstallation(string Path, BlenderVersion Version);

/// <summary>Outcome category of <see cref="BlenderLocator.Locate"/>.</summary>
public enum BlenderLocateStatus
{
    /// <summary>A usable Blender (≥ the minimum version) was found.</summary>
    Found,
    /// <summary>No Blender executable was found anywhere.</summary>
    NotFound,
    /// <summary>A Blender was found but it is older than the minimum supported version.</summary>
    TooOld,
}

/// <summary>The result of locating Blender.</summary>
public sealed record BlenderLocateResult(
    BlenderLocateStatus Status,
    BlenderInstallation? Installation,
    string? Detail = null)
{
    public bool IsUsable => Status == BlenderLocateStatus.Found && Installation is not null;
}

/// <summary>Runs <c>&lt;path&gt; --version</c> and returns the parsed version, or null when the
/// path is not a runnable Blender. Injected so the locator is testable without a real Blender.</summary>
public interface IBlenderProbe
{
    BlenderVersion? Probe(string executablePath);
}
