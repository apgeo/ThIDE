using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Workspace.Flatten;

/// <summary>Diagnostic codes of the single-file export.</summary>
public static class FlattenDiagnosticCodes
{
    /// <summary>A file the project names could not be read.</summary>
    public const string FileMissing = "TH_FLAT_001";
    /// <summary>A station reference names a survey that does not exist.</summary>
    public const string UnknownSurvey = "TH_FLAT_002";
    /// <summary>A data row could not be read (a reading is missing or not a number).</summary>
    public const string BadReading = "TH_FLAT_003";
    /// <summary>Something the source holds is not carried into the merged file.</summary>
    public const string NotCarried = "TH_FLAT_004";
    /// <summary>The magnetic declination of a dated survey could not be worked out.</summary>
    public const string DeclinationUnknown = "TH_FLAT_005";
    /// <summary>How the declination was chosen, where Therion would warn about it too.</summary>
    public const string DeclinationNote = "TH_FLAT_006";
    /// <summary>An option value is not usable.</summary>
    public const string BadOption = "TH_FLAT_007";
}

/// <summary>Where a shot's declination came from.</summary>
public enum FlattenDeclinationSource
{
    /// <summary>No declination applies (or applying it was switched off).</summary>
    None,
    /// <summary>A <c>declination</c> or <c>grid-angle</c> command in the centreline.</summary>
    Centreline,
    /// <summary>The <c>-declination</c> option of an enclosing survey.</summary>
    Survey,
    /// <summary>The IGRF field at the centreline's date.</summary>
    Igrf,
    /// <summary>The IGRF field at the earliest date in the project, for an undated centreline (as Therion does).</summary>
    IgrfUndated,
}

/// <summary>The declination applied to the shots of one source centreline.</summary>
/// <param name="Survey">The survey the centreline is in, Therion-style (<c>inner.outer</c>), empty for the top level.</param>
/// <param name="Location">The <c>centreline</c> line.</param>
/// <param name="Date">The centreline's date as written, or null.</param>
/// <param name="DecimalYear">The year the declination was evaluated at (the middle of the date range), or null.</param>
/// <param name="Degrees">The declination added to the compass (east positive); for several values, the first.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Shots">How many shots it applies to.</param>
public sealed record FlattenDeclinationUse(
    string Survey,
    SourceSpan Location,
    string? Date,
    double? DecimalYear,
    double Degrees,
    FlattenDeclinationSource Source,
    int Shots);

/// <summary>A station of the source and the name it has in the merged file.</summary>
/// <param name="Original">Therion's full name, <c>point@inner.outer</c> (just <c>point</c> at the top level).</param>
/// <param name="Name">Its name in the merged centreline; equated stations share one.</param>
public sealed record FlattenStationName(string Original, string Name);

/// <summary>Counts of what went into the merged file.</summary>
public sealed record FlattenStatistics(
    int Files,
    int Centrelines,
    int Shots,
    int SplaysKept,
    int SplaysDropped,
    int Stations,
    int EquatesMerged,
    int FixedPoints,
    int StationComments,
    int Components);

/// <summary>The merged single-survey file and everything learnt while making it.</summary>
public sealed record FlattenResult(
    string Text,
    ImmutableArray<Diagnostic> Diagnostics,
    FlattenStatistics Statistics,
    ImmutableArray<FlattenStationName> StationNames,
    ImmutableArray<FlattenDeclinationUse> Declinations)
{
    /// <summary>The project configuration's output coordinate system (<c>cs</c>), which a .th cannot carry; null when none.</summary>
    public string? ConfigCs { get; init; }

    /// <summary>True when an error stopped the file from matching the project; <see cref="Text"/> is still written.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>The station renames as CSV (<c>original,name</c>), for a reader of the merged file.</summary>
    public string StationNamesCsv()
    {
        var sb = new System.Text.StringBuilder("original,name\n");
        foreach (var s in StationNames)
            sb.Append(Csv(s.Original)).Append(',').Append(Csv(s.Name)).Append('\n');
        return sb.ToString();

        static string Csv(string v) => v.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
