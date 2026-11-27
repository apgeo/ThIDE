// A 3-D model (STL, PLY, OBJ, glb, XYZ) turned into a derived Therion survey, for the app, the CLI
// and the MCP tools: read the guide (JSON, a Therion centreline, or a .lox/.3d survey), run the
// pipeline, and write the .th with its report (and, when asked, the debug layers) beside it.

using Therion.Cave3D.Parsing;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Workspace.Import;

/// <summary>A finished derived survey: its Therion text, report and the objects behind them.</summary>
public sealed record MeshSurveyImportResult(
    string Therion,
    string ReportJson,
    DerivedSurvey Survey,
    MeshSurveySession Session,
    PreparedCave Cave,
    IReadOnlyList<string> GuideWarnings);

public static class MeshSurveyImport
{
    /// <summary>File extensions of the models the importer reads.</summary>
    public static readonly string[] ModelExtensions = [".stl", ".ply", ".obj", ".glb", ".xyz", ".pts"];

    public static bool IsModel(string path) =>
        ModelExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a guide: <c>.json</c> (the guide schema), <c>.th</c> (a centreline with a fix in the
    /// model's coordinates), <c>.lox</c> or <c>.3d</c> (a surveyed model, all its legs given).</summary>
    public static (SurveyGuide Guide, IReadOnlyList<string> Warnings) ReadGuide(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".json":
                return (MeshSurveyJson.ReadGuide(File.ReadAllText(path)), []);
            case ".lox":
            case ".3d":
                return (SurveyGuides.FromCaveModel(CaveModelReader.ReadFile(path)), []);
            default:
                return TherionGuideReader.Read(path, File.ReadAllText(path));
        }
    }

    public static MeshSurveyImportResult Run(string modelPath, MeshSurveyOptions options, SurveyGuide? guide = null,
        IProgress<MeshSurveyProgress>? progress = null, CancellationToken ct = default, IReadOnlyList<string>? guideWarnings = null)
    {
        var session = MeshSurveySession.Open(modelPath, options.Input);
        var cave = session.Prepare(options, guide, progress, ct);
        return Plan(session, cave, options, guide, ct, guideWarnings);
    }

    /// <summary>Plans again on an already prepared model (the app's quick re-run after a setting changes).</summary>
    public static MeshSurveyImportResult Plan(MeshSurveySession session, PreparedCave cave, MeshSurveyOptions options, SurveyGuide? guide = null,
        CancellationToken ct = default, IReadOnlyList<string>? guideWarnings = null)
    {
        var survey = session.Plan(cave, options, guide, null, ct);
        var therion = session.WriteTherion(survey, options);
        var report = MeshSurveyJson.Serialize(new { settings = options, report = survey.Report });
        return new MeshSurveyImportResult(therion, report, survey, session, cave, guideWarnings ?? []);
    }

    /// <summary>The report's path for a target .th: <c>cave.th</c> → <c>cave.mesh-survey.json</c>.</summary>
    public static string ReportPath(string thPath) => Path.ChangeExtension(thPath, ".mesh-survey.json");

    /// <summary>Writes the debug layers beside the target (<c>cave.skeleton.ply</c>, <c>cave.uncovered.ply</c>,
    /// <c>cave.preview.lox</c>); returns their paths.</summary>
    public static IReadOnlyList<string> WriteDebugLayers(MeshSurveyImportResult result, string thPath)
    {
        string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(thPath)) ?? ".", Path.GetFileNameWithoutExtension(thPath));
        var written = new List<string>();
        string skeleton = stem + ".skeleton.ply";
        File.WriteAllText(skeleton, PreviewModel.SkeletonPly(result.Cave.Skeleton.Graph, result.Session));
        written.Add(skeleton);
        string uncovered = stem + ".uncovered.ply";
        File.WriteAllText(uncovered, PreviewModel.PointsPly(result.Survey.Report.Plan.Uncovered.Select(u => (u.Position, u.Amount)), "regions no station sees (value: m2)"));
        written.Add(uncovered);
        string lox = stem + ".preview.lox";
        LoxWriter.WriteFile(PreviewModel.Build(result.Survey, result.Session), lox);
        written.Add(lox);
        return written;
    }
}

/// <summary>A guide from a Therion centreline: its stations (placed by dead reckoning from a fixed
/// station, whose fix must be in the model's coordinates) and its legs.</summary>
public static class TherionGuideReader
{
    public static (SurveyGuide Guide, IReadOnlyList<string> Warnings) Read(string path, string text)
    {
        var warnings = new List<string>();
        var parse = new ThParser().Parse(path, text);
        var model = WorkspaceSemanticModel.Build(new Dictionary<string, ParseResult<Therion.Syntax.TherionFile>> { [path] = parse }, []);
        var positions = StationPositionEstimator.Estimate(model);

        // Each piece is placed relative to its own anchor; a fix in it gives the offset to the model frame.
        var offsets = new Dictionary<int, Therion.Cave3D.CaveVector3>();
        QualifiedName? fixedName = null;
        foreach (var station in model.StationsByQn.Values.OrderBy(s => s.Name.ToString(), StringComparer.Ordinal))
        {
            if (station.Kind != StationDeclarationKind.Fix || station.FixX is not { } fx || station.FixY is not { } fy || station.FixZ is not { } fz) continue;
            if (positions.For(station.Name) is not { } p || offsets.ContainsKey(p.ComponentId)) continue;
            offsets[p.ComponentId] = new Therion.Cave3D.CaveVector3(fx - p.X, fy - p.Y, fz - p.Z);
            fixedName ??= station.Name;
        }
        if (offsets.Count == 0) warnings.Add("The centreline has no fix, so its stations cannot be placed on the model: add a fix in the model's coordinates.");

        var points = new List<GuidePoint>();
        var names = new Dictionary<QualifiedName, string>();
        foreach (var (name, p) in positions.Positions.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
        {
            if (!offsets.TryGetValue(p.ComponentId, out var offset)) continue;
            string label = name.ToString();
            if (names.ContainsValue(label)) continue;
            names[name] = label;
            points.Add(new GuidePoint { Name = label, Position = new Therion.Cave3D.CaveVector3(p.X, p.Y, p.Z) + offset });
        }
        var legs = new List<GuideLeg>();
        var seen = new HashSet<(string, string)>();
        foreach (var file in model.PerFile.Values)
            foreach (var shot in file.Shots)
            {
                if ((shot.Flags & ShotFlags.Splay) != 0) continue;
                if (!names.TryGetValue(shot.From, out var a) || !names.TryGetValue(shot.To, out var b) || a == b) continue;
                if (seen.Add((a, b)) && seen.Add((b, a))) legs.Add(new GuideLeg { From = a, To = b });
            }
        int skipped = positions.Positions.Count - points.Count;
        if (skipped > 0 && offsets.Count > 0) warnings.Add($"{skipped} stations are in a part of the centreline with no fix and were left out.");
        GuidePoint? start = fixedName is { } f && names.TryGetValue(f, out var startName) ? points.First(p => p.Name == startName) : null;
        var others = points.Where(p => p != start).ToList();
        return (new SurveyGuide { Start = start, Points = others, Legs = legs }, warnings);
    }
}
