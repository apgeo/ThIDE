// "Add unreferenced scraps to maps": which map each drawing no map composes should join, and the
// insertions that add them, named so the map's file can resolve them (scrap@survey when the scrap
// lives in another survey).

using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

public static class MapSync
{
    /// <summary>
    /// The map a scrap most likely belongs to: a map in the file that inputs the scrap's sketch, else
    /// the map whose id shares the longest prefix with the scrap's (at least two characters), else
    /// the first map; null when there are no maps.
    /// </summary>
    public static string? DefaultMap(WorkspaceSemanticModel ws, string scrapId)
    {
        if (ws.MapsById.Count == 0) return null;
        if (ws.ScrapsById.TryGetValue(scrapId, out var scrap) && scrap.DeclarationSpan.FilePath is { } sketch)
        {
            var includers = ws.FileGraphEdges.Where(e => FilePaths.Same(e.To, sketch)).Select(e => e.From).ToList();
            var near = ws.MapsById.Values.Where(m => m.DeclarationSpan.FilePath is { } f && includers.Any(i => FilePaths.Same(i, f)))
                .OrderBy(m => m.Id, StringComparer.Ordinal).FirstOrDefault();
            if (near is not null) return near.Id;
        }
        var byPrefix = ws.MapsById.Keys
            .Select(id => (Id: id, Common: CommonPrefix(id, scrapId)))
            .Where(x => x.Common >= 2)
            .OrderByDescending(x => x.Common).ThenBy(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return byPrefix.Id ?? ws.MapsById.Keys.Order(StringComparer.Ordinal).First();
    }

    private static int CommonPrefix(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i])) i++;
        return i;
    }

    /// <summary>
    /// The edits adding each scrap to its chosen map (null = skip). Scraps for maps in the same file
    /// are inserted together; problems (a map without endmap, a map not found as the file reads now)
    /// are listed by map id.
    /// </summary>
    public static (EditBatch Batch, IReadOnlyList<(string Map, MapEditProblem Problem)> Problems) Plan(
        WorkspaceSemanticModel ws, FileScopes scopes, IReadOnlyDictionary<string, string?> assignments,
        Func<string, string?> readText, Func<string, TherionFile?> ast)
    {
        var groups = new Dictionary<string, List<TextEdit>>(FilePaths.Comparer);
        var problems = new List<(string, MapEditProblem)>();
        foreach (var byMap in assignments.Where(a => a.Value is not null).GroupBy(a => a.Value!, StringComparer.Ordinal))
        {
            if (!ws.MapsById.TryGetValue(byMap.Key, out var map) || map.DeclarationSpan.FilePath is not { } file || readText(file) is not { } text)
            {
                problems.Add((byMap.Key, MapEditProblem.MapNotFound));
                continue;
            }
            var mapScope = scopes.EffectiveScope(file, SurveyScopeResolver.SurveyPathAt(ast(file), map.DeclarationSpan.StartOffset, text));
            var members = byMap.Select(a => Reference(ws, scopes, a.Key, mapScope)).ToList();
            var planned = MapMemberEdits.Plan(file, text, byMap.Key, members);
            if (planned.Problem != MapEditProblem.None) { problems.Add((byMap.Key, planned.Problem)); continue; }
            if (planned.Edit is { } edit)
            {
                if (!groups.TryGetValue(file, out var list)) groups[file] = list = [];
                list.Add(edit);
            }
        }
        var batch = new EditBatch(groups.Select(g => new FileTextEdits(g.Key, g.Value)).ToList(), [], [], RequiresWorkspaceReload: true);
        return (batch, problems);
    }

    // The scrap named from the map's survey: `id`, `id@sub`, or the bare id when the scrap is not
    // below the map (Therion then resolves it by its unique id, as the user would have written it).
    private static string Reference(WorkspaceSemanticModel ws, FileScopes scopes, string scrapId, ImmutableArray<string> mapScope)
    {
        if (!ws.ScrapsById.TryGetValue(scrapId, out var scrap) || scrap.DeclarationSpan.FilePath is not { } sketch) return scrapId;
        var scrapScope = scopes.PrefixOf(sketch);
        return StationReference.Format(QualifiedName.OfStation(scrapScope, scrapId), mapScope) ?? scrapId;
    }
}
