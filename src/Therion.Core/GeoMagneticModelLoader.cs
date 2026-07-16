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
            try { if (File.Exists(path)) return GeoMagneticModel.FromCof(File.ReadAllText(path)); }
            catch { /* try the next candidate */ }
        }
        return null;
    }

    /// <summary>Loads a specific .COF file, or null when it is missing or unparseable.</summary>
    public static GeoMagneticModel? TryLoadFrom(string cofPath)
    {
        try { return File.Exists(cofPath) ? GeoMagneticModel.FromCof(File.ReadAllText(cofPath)) : null; }
        catch { return null; }
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
