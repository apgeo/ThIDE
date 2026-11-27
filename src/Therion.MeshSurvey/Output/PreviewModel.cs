// A derived survey as a 3-D model for the viewer: the input mesh as a scrap (the walls), the stations,
// legs and splays as a centreline, in model coordinates, so the existing .lox writer and viewer show
// the result without running Therion. Also writes the debug layers (skeleton, rejected legs,
// uncovered regions) as PLY point/line files.

using System.Globalization;
using System.Text;
using Therion.Cave3D;
using Therion.MeshSurvey.Plan;
using Therion.MeshSurvey.Skeleton;

namespace Therion.MeshSurvey.Output;

public static class PreviewModel
{
    private const uint LoxSplay = 16;
    private const uint LoxEntrance = 2;
    private const uint LoxFixed = 4;
    private const uint LoxContinuation = 8;

    /// <summary>The survey (and the model's walls, when <paramref name="session"/> has a mesh) as a <see cref="CaveModel"/>.</summary>
    public static CaveModel Build(DerivedSurvey survey, MeshSurveySession? session)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var stations = new List<CaveStation>();
        foreach (var s in survey.Stations)
        {
            uint flags = 0;
            var typed = CaveStationFlags.None;
            if (s.Index == survey.StartStation) { flags |= LoxEntrance | LoxFixed; typed |= CaveStationFlags.Entrance | CaveStationFlags.Fixed; }
            if (s.Continuation) { flags |= LoxContinuation; typed |= CaveStationFlags.Continuation; }
            stations.Add(new CaveStation { Id = (uint)s.Index, SurveyId = 1, Name = s.Name, Position = s.Position, RawFlags = flags, Flags = typed });
        }
        var shots = new List<CaveShot>();
        foreach (var l in survey.Legs)
        {
            shots.Add(new CaveShot
            {
                FromStationId = (uint)l.From, ToStationId = (uint)l.To, SurveyId = 1,
                FromPosition = survey.Stations[l.From].Position, ToPosition = survey.Stations[l.To].Position,
                FromLrud = survey.Stations[l.From].Lrud, ToLrud = survey.Stations[l.To].Lrud,
            });
        }
        uint next = (uint)survey.Stations.Count;
        foreach (var sp in survey.Splays)
        {
            uint id = next++;
            stations.Add(new CaveStation { Id = id, SurveyId = 1, Name = "", Position = sp.End });
            shots.Add(new CaveShot
            {
                FromStationId = (uint)sp.Station, ToStationId = id, SurveyId = 1, RawFlags = LoxSplay, Flags = CaveShotFlags.Splay,
                FromPosition = survey.Stations[sp.Station].Position, ToPosition = sp.End,
            });
        }
        var scraps = new List<CaveScrap>();
        if (session?.Model.Mesh is { } mesh)
            scraps.Add(new CaveScrap { Id = 0, SurveyId = 1, Points = mesh.Vertices.Select(session.Model.ToModel).ToList(), Triangles = mesh.Triangles });
        return new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Title = survey.Name + " (derived)",
            CoordinateSystem = survey.CoordinateSystem,
            Surveys = [new CaveSurvey(0, 0, "", survey.Name), new CaveSurvey(1, 0, TherionCentrelineWriter.Identifier(survey.Name), survey.Name + " (derived)")],
            Stations = stations,
            Shots = shots,
            Scraps = scraps,
        };
    }

    /// <summary>Writes the skeleton (lines) as an ASCII PLY in model coordinates.</summary>
    public static string SkeletonPly(SkeletonGraph graph, MeshSurveySession session)
    {
        var vertices = new List<CaveVector3>();
        var edges = new List<(int, int)>();
        foreach (var e in graph.LiveEdges)
        {
            int first = vertices.Count;
            vertices.AddRange(e.Points.Select(session.Model.ToModel));
            for (int i = first + 1; i < vertices.Count; i++) edges.Add((i - 1, i));
        }
        return LinesPly(vertices, edges, "skeleton of the void");
    }

    /// <summary>Points with a value (uncovered regions, cuts, relaxations) as an ASCII PLY.</summary>
    public static string PointsPly(IEnumerable<(CaveVector3 Position, double Value)> points, string comment) =>
        LinesPly(points.Select(p => p.Position).ToList(), [], comment, points.Select(p => p.Value).ToList());

    private static string LinesPly(List<CaveVector3> vertices, List<(int, int)> edges, string comment, List<double>? values = null)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("ply\nformat ascii 1.0\ncomment ").Append(comment).Append('\n');
        sb.Append("element vertex ").Append(vertices.Count.ToString(c)).Append("\nproperty double x\nproperty double y\nproperty double z\n");
        if (values is not null) sb.Append("property double value\n");
        if (edges.Count > 0) sb.Append("element edge ").Append(edges.Count.ToString(c)).Append("\nproperty int vertex1\nproperty int vertex2\n");
        sb.Append("end_header\n");
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            sb.Append(v.X.ToString("F3", c)).Append(' ').Append(v.Y.ToString("F3", c)).Append(' ').Append(v.Z.ToString("F3", c));
            if (values is not null) sb.Append(' ').Append(values[i].ToString("F3", c));
            sb.Append('\n');
        }
        foreach (var (a, b) in edges) sb.Append(a.ToString(c)).Append(' ').Append(b.ToString(c)).Append('\n');
        return sb.ToString();
    }
}
