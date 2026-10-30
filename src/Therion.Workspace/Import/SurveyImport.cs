// One entry point for "turn another program's file into Therion": format from the extension or by
// name, then the matching importer. Shared by `therion-cli import` and the import_survey tool.

namespace Therion.Workspace.Import;

public enum SurveyImportFormat
{
    /// <summary>Compass <c>.dat</c>.</summary>
    Compass,
    /// <summary>Survex <c>.svx</c>.</summary>
    Survex,
    /// <summary>GPS waypoints, <c>.gpx</c> — becomes a survey of fixed stations.</summary>
    Gpx,
    /// <summary>A table of shots (<c>.csv</c>, <c>.tsv</c>, a PocketTopo/DistoX text dump) — see <see cref="ShotTableReader"/>.</summary>
    Csv,
}

public static class SurveyImport
{
    private static readonly Dictionary<string, SurveyImportFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dat"] = SurveyImportFormat.Compass,
        [".svx"] = SurveyImportFormat.Survex,
        [".gpx"] = SurveyImportFormat.Gpx,
        [".csv"] = SurveyImportFormat.Csv,
        [".tsv"] = SurveyImportFormat.Csv,
    };

    public static string Names => string.Join(", ", Enum.GetNames<SurveyImportFormat>().Select(n => n.ToLowerInvariant()));

    /// <summary>The format named by <paramref name="format"/>, or (when null) the one <paramref name="sourcePath"/>'s extension implies.</summary>
    public static bool TryResolveFormat(string? format, string sourcePath, out SurveyImportFormat resolved, out string error)
    {
        error = "";
        if (!string.IsNullOrWhiteSpace(format))
        {
            if (Enum.TryParse(format.Trim(), ignoreCase: true, out resolved) && Enum.IsDefined(resolved)) return true;
            error = $"Unknown format '{format}'. Use one of: {Names}.";
            return false;
        }
        var extension = Path.GetExtension(sourcePath);
        if (ByExtension.TryGetValue(extension, out resolved)) return true;
        error = $"Cannot tell what '{extension}' is. Name the format: {Names}.";
        return false;
    }

    /// <summary>
    /// The Therion source for <paramref name="text"/>. <paramref name="surveyName"/> names the survey
    /// for GPX and shot tables (the others carry their own names). Throws on input the importer cannot
    /// read; <paramref name="issues"/> gets a shot table's warnings.
    /// </summary>
    public static string Translate(SurveyImportFormat format, string text, string surveyName, ShotTableOptions? shots = null, List<ShotIssue>? issues = null)
    {
        switch (format)
        {
            case SurveyImportFormat.Compass: return CompassImporter.Import(text);
            case SurveyImportFormat.Survex: return SurvexImporter.Import(text);
            case SurveyImportFormat.Gpx: return GpxImporter.ToTherion(text, surveyName);
            case SurveyImportFormat.Csv:
                var table = ShotTableReader.Read(text, shots);
                issues?.AddRange(table.Issues);
                return table.Rows.Count == 0 ? "" : ShotTableReader.ToSurvey(table, surveyName);
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }
}
