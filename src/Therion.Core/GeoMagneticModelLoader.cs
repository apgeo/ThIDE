// Locates and loads the default World Magnetic Model (WMM.COF): %AppData%/ThIDE, then next to the
// binary, then Assets/. The .COF is a public-domain NOAA download the user supplies — nothing ships
// it — so absence is expected and every caller must handle null.
//
// The I/O lives here, next to GeoMagneticModel, rather than in the app, so the desktop app, the CLI
// and the MCP server all look in the same places for the same file
// (.claude/mcp-integration/DECISIONS.md D-017). GeoMagneticModel itself stays pure: it parses text.

using System;
using System.Collections.Generic;
using System.IO;

namespace Therion.Core;

public static class GeoMagneticModelLoader
{
    /// <summary>Filename of the coefficient file, as NOAA distributes it.</summary>
    public const string CofFileName = "WMM.COF";

    /// <summary>The first loadable WMM/IGRF .COF among the well-known locations, or null if none.</summary>
    public static GeoMagneticModel? TryLoadDefault()
    {
        foreach (var path in CandidatePaths())
        {
            try { if (IsCofCandidate(path)) return GeoMagneticModel.FromCof(ReadBounded(path)); }
            catch { /* try the next candidate */ }
        }
        return null;
    }

    /// <summary>Loads a specific .COF file, or null when it is missing, not a plausible .COF file, or unparseable.</summary>
    public static GeoMagneticModel? TryLoadFrom(string cofPath)
    {
        try { return IsCofCandidate(cofPath) ? GeoMagneticModel.FromCof(ReadBounded(cofPath)) : null; }
        catch { return null; }
    }

    /// <summary>A .COF file is a few tens of KB; anything past this is not one.</summary>
    public const long MaxCofBytes = 1024 * 1024;

    /// <summary>
    /// True when <paramref name="path"/> is a local regular file (symlinks followed) of 1 byte to
    /// <see cref="MaxCofBytes"/>. Decided from metadata alone, without opening it: opening a named pipe
    /// blocks, a device such as /dev/zero reads without end, and a UNC path makes Windows authenticate to
    /// whatever host it names. Pipes and devices report a length of 0, so the size bound excludes them.
    /// </summary>
    public static bool IsCofCandidate(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (IsNetworkPath(path)) return false;

            var info = new FileInfo(Path.GetFullPath(path));
            if (info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target) info = target;
            return info.Exists && info.Length is > 0 and <= MaxCofBytes;
        }
        catch { return false; }
    }

    /// <summary>True for a UNC path (<c>\\host\share</c>), which must not even be probed.</summary>
    public static bool IsNetworkPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.StartsWith(@"\\", StringComparison.Ordinal) || full.StartsWith("//", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>Reads at most <see cref="MaxCofBytes"/>, so a file that grows after the check cannot run unbounded.</summary>
    private static string ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxCofBytes + 1];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        if (read > MaxCofBytes) throw new InvalidDataException("Coefficient file is too large.");
        return new string(buffer, 0, read);
    }

    /// <summary>Where <see cref="TryLoadDefault"/> looks, in order. Useful for telling a user where to put the file.</summary>
    public static IEnumerable<string> CandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) yield return Path.Combine(appData, "ThIDE", CofFileName);
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, CofFileName);
        yield return Path.Combine(baseDir, "Assets", CofFileName);
    }
}
