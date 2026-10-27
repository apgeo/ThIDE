using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Semantics.Linking;

namespace Therion.Workspace.Linking;

/// <summary>A station as a picker offers it: where it is declared and its file-local and workspace-wide names.</summary>
public sealed record StationEntry(string FilePath, QualifiedName Local, QualifiedName Effective);

/// <summary>A survey as a picker offers it; <see cref="Effective"/> is the workspace-wide path.</summary>
public sealed record SurveyEntry(string FilePath, ImmutableArray<string> Local, ImmutableArray<string> Effective)
{
    public string Name => Effective.IsEmpty ? "" : Effective[^1];
    public int Depth => Math.Max(0, Effective.Length - 1);
}

/// <summary>The lists the link pickers show, in natural order.</summary>
public static class LinkCatalog
{
    /// <summary>Every survey of the workspace, depth-first by effective path (a tree read top to bottom).</summary>
    public static IReadOnlyList<SurveyEntry> Surveys(WorkspaceSemanticModel ws, FileScopes scopes)
    {
        var byPath = new Dictionary<string, SurveyEntry>(StringComparer.Ordinal);
        foreach (var (file, model) in ws.PerFile)
            foreach (var name in model.Surveys.Keys)
            {
                var effective = scopes.EffectiveScope(file, name.Parts);
                byPath.TryAdd(string.Join('\0', effective), new SurveyEntry(file, name.Parts, effective));
            }
        return byPath.Values.OrderBy(s => s.Effective, PathOrder.Instance).ToList();
    }

    /// <summary>
    /// The workspace's stations, optionally only those inside <paramref name="withinSurvey"/>
    /// (effective path; its sub-surveys included).
    /// </summary>
    public static IReadOnlyList<StationEntry> Stations(WorkspaceSemanticModel ws, FileScopes scopes, ImmutableArray<string>? withinSurvey = null)
    {
        var result = new List<StationEntry>();
        foreach (var (file, model) in ws.PerFile)
            foreach (var name in model.Stations.Keys)
            {
                var effective = scopes.Effective(file, name);
                if (withinSurvey is { } within && !StationReference.IsPrefix(within.AsSpan(), effective.Parts.AsSpan(0, effective.Parts.Length - 1)))
                    continue;
                result.Add(new StationEntry(file, name, effective));
            }
        return Sort(result);
    }

    /// <summary>The stations of one file parsed on its own (local and effective names coincide).</summary>
    public static IReadOnlyList<StationEntry> Stations(SourceSurvey source) =>
        Sort(source.Model.Stations.Keys.Select(n => new StationEntry(source.FilePath, n, n)).ToList());

    private static IReadOnlyList<StationEntry> Sort(List<StationEntry> stations)
    {
        // Survey path first (so a survey's stations stay together), then the station number.
        stations.Sort((a, b) =>
        {
            int bySurvey = PathOrder.Instance.Compare(a.Effective.Parts.RemoveAt(a.Effective.Parts.Length - 1),
                                                       b.Effective.Parts.RemoveAt(b.Effective.Parts.Length - 1));
            return bySurvey != 0 ? bySurvey : NaturalStringComparer.Instance.Compare(a.Effective.Last, b.Effective.Last);
        });
        return stations;
    }

    /// <summary>Paths compared part by part, naturally; a parent sorts before its children.</summary>
    private sealed class PathOrder : IComparer<ImmutableArray<string>>
    {
        public static PathOrder Instance { get; } = new();

        public int Compare(ImmutableArray<string> x, ImmutableArray<string> y)
        {
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            {
                int c = NaturalStringComparer.Instance.Compare(x[i], y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
