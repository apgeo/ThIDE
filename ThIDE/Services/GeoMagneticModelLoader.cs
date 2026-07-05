// Phase 3b — locates and loads the default World Magnetic Model (WMM.COF) the same way the
// Declination calculator does: %AppData%/ThIDE, then next to the app, then Assets/. The
// .COF is a public-domain NOAA download the user supplies; absence is expected and handled gracefully.

using System;
using System.Collections.Generic;
using System.IO;
using Therion.Core;

namespace ThIDE.Services;

public static class GeoMagneticModelLoader
{
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

    private static IEnumerable<string> CandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) yield return Path.Combine(appData, "ThIDE", "WMM.COF");
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "WMM.COF");
        yield return Path.Combine(baseDir, "Assets", "WMM.COF");
    }
}
