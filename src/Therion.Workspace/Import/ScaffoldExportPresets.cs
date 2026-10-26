// Named export targets for callers that choose outputs by name (a server's request, a CLI flag)
// rather than by building ExportItems. Every output name a catalogue entry produces is distinct
// from every other entry's, so any combination can go into one thconfig.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Therion.Workspace.Import;

public static class ScaffoldExportPresets
{
    public const string Lox = "lox";
    public const string Survex3d = "3d";
    public const string PlanPdf = "plan-pdf";
    public const string ExtendedPdf = "extended-pdf";
    public const string PlanSvg = "plan-svg";
    public const string ExtendedSvg = "extended-svg";
    public const string Kml = "kml";
    public const string Dxf = "dxf";
    public const string Sql = "sql";
    public const string Csv = "csv";
    public const string SurveyListHtml = "survey-list-html";
    public const string ContinuationListHtml = "continuation-list-html";
    public const string Shapefile = "shp";

    private static readonly (string Name, ExportItem[] Items)[] Entries =
    {
        (Lox,          new[] { new ExportItem(ExportKind.Model, "loch", ".lox") }),
        (Survex3d,     new[] { new ExportItem(ExportKind.Model, "survex", ".3d") }),
        (PlanPdf,      new[] { new ExportItem(ExportKind.Map, "pdf", ".pdf", Projection: "plan", UseLayout: true) }),
        (ExtendedPdf,  new[] { new ExportItem(ExportKind.Map, "pdf", ".pdf", Projection: "extended", UseLayout: true) }),
        (PlanSvg,      new[] { new ExportItem(ExportKind.Map, "svg", ".svg", Projection: "plan", UseLayout: true) }),
        (ExtendedSvg,  new[] { new ExportItem(ExportKind.Map, "svg", ".svg", Projection: "extended", UseLayout: true) }),
        // Needs a georeferenced survey: Therion skips it ("data not georeferenced") otherwise.
        (Kml,          new[] { new ExportItem(ExportKind.Model, "kml", ".kml") }),
        (Dxf,          new[] { new ExportItem(ExportKind.Model, "dxf", ".dxf") }),
        (Sql,          new[] { new ExportItem(ExportKind.Database, "sql", ".sql") }),
        (Csv,          new[] { new ExportItem(ExportKind.Database, "csv", ".csv") }),
        (SurveyListHtml,       new[] { new ExportItem(ExportKind.SurveyList, "html", "-surveys.html") }),
        (ContinuationListHtml, new[] { new ExportItem(ExportKind.ContinuationList, "html", "-continuations.html") }),
        // Therion's shapefile export writes a directory of .shp/.shx/.dbf (and .prj when georeferenced).
        (Shapefile,    new[] { new ExportItem(ExportKind.Model, "esri", "-shp") }),
    };

    /// <summary>Every preset name, in catalogue order.</summary>
    public static IReadOnlyList<string> Names { get; } = Entries.Select(e => e.Name).ToArray();

    /// <summary>Preset name → the export(s) it adds. Names are matched without regard to case.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ExportItem>> Catalogue { get; } =
        Entries.ToDictionary(e => e.Name, e => (IReadOnlyList<ExportItem>)e.Items, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The exports for <paramref name="names"/>, in the order given, each preset once. Returns false
    /// (with the names it did not know in <paramref name="unknown"/>) when a name is not in the
    /// catalogue; <paramref name="exports"/> then holds only the known ones.
    /// </summary>
    public static bool TryResolve(IEnumerable<string> names, out IReadOnlyList<ExportItem> exports, out IReadOnlyList<string> unknown)
    {
        var items = new List<ExportItem>();
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (!seen.Add(name)) continue;
            if (Catalogue.TryGetValue(name, out var found)) items.AddRange(found);
            else missing.Add(raw);
        }
        exports = items;
        unknown = missing;
        return missing.Count == 0;
    }

    /// <summary>Like <see cref="TryResolve"/>, throwing <see cref="ArgumentException"/> on an unknown name.</summary>
    public static IReadOnlyList<ExportItem> Resolve(IEnumerable<string> names)
    {
        if (!TryResolve(names, out var exports, out var unknown))
            throw new ArgumentException(
                $"Unknown export preset(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", Names)}.", nameof(names));
        return exports;
    }
}
