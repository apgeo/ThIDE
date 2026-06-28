// PUB-02 — station / shot data tables as plain (headers, rows) for export to CSV / Markdown /
// HTML / LaTeX. Pure projection of a WorkspaceSemanticModel; the formatting lives in the app's
// DataExport. Reused by the PUB-01 report generator.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Therion.Semantics;

public static class SurveyTables
{
    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
        StationsTable(WorkspaceSemanticModel model)
    {
        var headers = new[] { "Station", "Kind", "File", "Line" };
        var rows = new List<IReadOnlyList<string>>();
        foreach (var perFile in model.PerFile.Values)
            foreach (var st in perFile.Stations.Values)
                rows.Add(new[]
                {
                    st.Name.ToString(),
                    st.Kind.ToString(),
                    FileName(st.DeclarationSpan),
                    Line(st.DeclarationSpan),
                });
        rows.Sort((a, b) => string.CompareOrdinal(a[0], b[0]));
        return (headers, rows);
    }

    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
        ShotsTable(WorkspaceSemanticModel model)
    {
        var headers = new[] { "From", "To", "Length", "Compass", "Clino", "Flags", "File", "Line" };
        var rows = new List<(string File, int Line, IReadOnlyList<string> Cells)>();
        foreach (var perFile in model.PerFile.Values)
            foreach (var s in perFile.Shots)
                rows.Add((FileName(s.Span), s.Span.Start.Line, new[]
                {
                    s.From.ToString(),
                    s.To.ToString(),
                    Num(s.Length),
                    Num(s.Compass),
                    Num(s.Clino),
                    FlagsText(s.Flags),
                    FileName(s.Span),
                    Line(s.Span),
                }));
        // Source order (file, then line) reads like the original centreline.
        rows.Sort((a, b) => string.CompareOrdinal(a.File, b.File) is var c && c != 0 ? c : a.Line.CompareTo(b.Line));
        return (headers, rows.Select(r => r.Cells).ToList());
    }

    private static string FileName(Therion.Core.SourceSpan span) =>
        string.IsNullOrEmpty(span.FilePath) ? string.Empty : System.IO.Path.GetFileName(span.FilePath);

    private static string Line(Therion.Core.SourceSpan span) => span.Start.Line.ToString(CultureInfo.InvariantCulture);

    private static string Num(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FlagsText(ShotFlags flags)
    {
        if (flags == ShotFlags.None) return string.Empty;
        var parts = new List<string>();
        if ((flags & ShotFlags.Surface) != 0) parts.Add("surface");
        if ((flags & ShotFlags.Duplicate) != 0) parts.Add("duplicate");
        if ((flags & ShotFlags.Splay) != 0) parts.Add("splay");
        if ((flags & ShotFlags.Approximate) != 0) parts.Add("approximate");
        return string.Join(",", parts);
    }
}
