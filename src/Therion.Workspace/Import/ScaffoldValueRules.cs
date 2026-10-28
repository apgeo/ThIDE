// What a value may look like before it is written into a generated Therion file. Shared by the
// TopoDroid project scaffold and project templates, so neither can slip a line break, a quote or a
// path outside the project into a thconfig.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Therion.Workspace.Import;

public static class ScaffoldValueRules
{
    // Therion's keyword (survey names) and extended keyword (station names); a station reference
    // may name the survey it is in, innermost first: 5@sub or 5@deeper.sub.
    private const string Keyword = "[A-Za-z0-9_/][A-Za-z0-9_/-]*";
    private const string ExtKeyword = "[A-Za-z0-9_/][A-Za-z0-9_/+*.,'-]*";
    private static readonly Regex SurveyNameRx = new("^" + Keyword + "$", RegexOptions.CultureInvariant);
    private static readonly Regex StationRefRx = new("^" + ExtKeyword + "(@" + Keyword + @"(\." + Keyword + ")*)?$", RegexOptions.CultureInvariant);
    private static readonly Regex EncodingRx = new("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex CoordinateSystemRx = new("^[A-Za-z0-9:._+-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex BaseNameRx = new("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex OptionTokenRx = new("^[A-Za-z0-9_-]*$", RegexOptions.CultureInvariant);
    private static readonly Regex ExtensionRx = new("^[A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant);

    public static bool IsSurveyName(string value) => SurveyNameRx.IsMatch(value);
    public static bool IsStationRef(string value) => StationRefRx.IsMatch(value);
    public static bool IsEncoding(string value) => EncodingRx.IsMatch(value);
    public static bool IsCoordinateSystem(string value) => CoordinateSystemRx.IsMatch(value);
    public static bool IsBaseName(string value) => BaseNameRx.IsMatch(value);
    public static bool IsOptionToken(string value) => OptionTokenRx.IsMatch(value);
    public static bool IsExtension(string value) => ExtensionRx.IsMatch(value);

    /// <summary>No control characters (a line break would start a new command).</summary>
    public static bool IsSingleLine(string? value) => value is not null && !value.Any(char.IsControl);

    public static bool IsNumber(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// A directory under the project root: relative, forward slashes only, and no segment that is
    /// empty, '.', '..', or that could read as a drive (':') or end a quoted path ('"').
    /// </summary>
    public static bool IsProjectRelativeDir(string dir, bool allowDot)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        if (allowDot && dir == ".") return true;
        if (Path.IsPathRooted(dir) || dir.IndexOfAny(new[] { '\\', ':', '"' }) >= 0) return false;
        return dir.Split('/').All(s => s.Length > 0 && s is not "." and not ".." && s.Trim().Length == s.Length);
    }
}
