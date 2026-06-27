// Builds the workspace-wide lookup tables that back WorkspaceSemanticModel.ResolveReference.
// Surveys / stations / maps come from the per-file .th models; scrap + scrap-object
// ids come straight from the .th2 ASTs (point/line `-id` lives unparsed in OptionsRaw).

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Frozen reference indexes consumed by <see cref="WorkspaceSemanticModel"/>.</summary>
internal sealed record ReferenceIndexes(
    FrozenDictionary<string, SurveySymbol> SurveysByFullName,
    FrozenDictionary<string, ImmutableArray<SurveySymbol>> SurveysByLastName,
    FrozenDictionary<string, StationSymbol> StationsByQn,
    FrozenDictionary<string, StationSymbol> StationsBySurveyAndPoint,
    FrozenDictionary<string, ImmutableArray<StationSymbol>> StationsByLastName,
    FrozenDictionary<string, MapSymbol> MapsById,
    FrozenDictionary<string, ScrapSymbol> ScrapsById,
    FrozenDictionary<string, ScrapObjectSymbol> ScrapObjectsById,
    ImmutableArray<Th2ObjectRecord> Th2Objects);

internal static class ReferenceIndexBuilder
{
    public static ReferenceIndexes Build(
        IEnumerable<SemanticModel> perFile,
        IEnumerable<TherionFile> th2Files)
    {
        var surveysByFull = new Dictionary<string, SurveySymbol>(StringComparer.Ordinal);
        var surveysByLast = new Dictionary<string, ImmutableArray<SurveySymbol>.Builder>(StringComparer.Ordinal);
        var stationsByQn = new Dictionary<string, StationSymbol>(StringComparer.Ordinal);
        var stationsBySp = new Dictionary<string, StationSymbol>(StringComparer.Ordinal);
        var stationsByLast = new Dictionary<string, ImmutableArray<StationSymbol>.Builder>(StringComparer.Ordinal);
        var mapsById = new Dictionary<string, MapSymbol>(StringComparer.Ordinal);
        var scrapsById = new Dictionary<string, ScrapSymbol>(StringComparer.Ordinal);
        var scrapObjectsById = new Dictionary<string, ScrapObjectSymbol>(StringComparer.Ordinal);
        var th2Objects = ImmutableArray.CreateBuilder<Th2ObjectRecord>();

        foreach (var model in perFile)
        {
            foreach (var sv in model.Surveys.Values)
            {
                surveysByFull.TryAdd(sv.Name.ToString(), sv);
                if (!surveysByLast.TryGetValue(sv.Name.Last, out var bucket))
                    surveysByLast[sv.Name.Last] = bucket = ImmutableArray.CreateBuilder<SurveySymbol>();
                bucket.Add(sv);
            }

            foreach (var st in model.Stations.Values)
            {
                // First definition wins (binder already points DeclarationSpan at the first shot/fix).
                stationsByQn.TryAdd(st.Name.ToString(), st);
                if (st.Name.Parts.Length >= 2)
                    stationsBySp.TryAdd(
                        WorkspaceSemanticModel.SurveyPointKey(st.Name.Parts[^2], st.Name.Last), st);
                if (!stationsByLast.TryGetValue(st.Name.Last, out var byLast))
                    stationsByLast[st.Name.Last] = byLast = ImmutableArray.CreateBuilder<StationSymbol>();
                byLast.Add(st);
            }

            foreach (var map in model.Maps.Values)
                mapsById.TryAdd(map.Id, map);
        }

        foreach (var th2 in th2Files)
            IndexTh2(th2, scrapsById, scrapObjectsById, th2Objects);

        return new ReferenceIndexes(
            surveysByFull.ToFrozenDictionary(StringComparer.Ordinal),
            surveysByLast.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToImmutable(), StringComparer.Ordinal),
            stationsByQn.ToFrozenDictionary(StringComparer.Ordinal),
            stationsBySp.ToFrozenDictionary(StringComparer.Ordinal),
            stationsByLast.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToImmutable(), StringComparer.Ordinal),
            mapsById.ToFrozenDictionary(StringComparer.Ordinal),
            scrapsById.ToFrozenDictionary(StringComparer.Ordinal),
            scrapObjectsById.ToFrozenDictionary(StringComparer.Ordinal),
            th2Objects.ToImmutable());
    }

    private static void IndexTh2(
        TherionFile file,
        Dictionary<string, ScrapSymbol> scraps,
        Dictionary<string, ScrapObjectSymbol> objects,
        ImmutableArray<Th2ObjectRecord>.Builder th2Objects)
    {
        foreach (var node in file.Children)
        {
            if (node is not ScrapBlock scrap) continue;
            if (!string.IsNullOrEmpty(scrap.Id))
                scraps.TryAdd(scrap.Id, new ScrapSymbol(scrap.Id, scrap.Span));

            foreach (var child in scrap.Children)
            {
                switch (child)
                {
                    case PointObject p:
                        if (TryGetIdOption(p.OptionsRaw, out var pid))
                            objects.TryAdd(pid, new ScrapObjectSymbol(pid, p.Span, scrap.Id));
                        th2Objects.Add(new Th2ObjectRecord("point", p.PointType, scrap.Id, p.Span));
                        break;
                    case LineObject l:
                        if (TryGetIdOption(l.OptionsRaw, out var lid))
                            objects.TryAdd(lid, new ScrapObjectSymbol(lid, l.Span, scrap.Id));
                        th2Objects.Add(new Th2ObjectRecord("line", l.LineType, scrap.Id, l.Span));
                        break;
                    case AreaObject a:
                        th2Objects.Add(new Th2ObjectRecord("area", a.AreaType, scrap.Id, a.Span));
                        break;
                }
            }
        }
    }

    /// <summary>Extracts the <c>-id &lt;name&gt;</c> value from a point/line option string.</summary>
    private static bool TryGetIdOption(string optionsRaw, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrEmpty(optionsRaw)) return false;
        var tokens = optionsRaw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], "-id", StringComparison.OrdinalIgnoreCase))
            {
                id = tokens[i + 1];
                return true;
            }
        }
        return false;
    }
}
