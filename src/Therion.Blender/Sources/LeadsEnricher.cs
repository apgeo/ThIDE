// Leads-register enrichment (BA-B4): resolve each workspace lead to a 3-D position by
// matching its station name against the metadata's labelled stations, and write the
// results into scene-meta.json's `leads` array (local coordinates, matching the mesh).
// Unmatched leads (e.g. a lead on an anonymous/absent station) are dropped.

namespace Therion.Blender.Sources;

/// <summary>Adds positioned leads to a <see cref="SceneMeta"/> document.</summary>
public static class LeadsEnricher
{
    /// <summary>
    /// Returns a copy of <paramref name="meta"/> with its <c>Leads</c> populated from
    /// <paramref name="leads"/>. A lead is placed at its station's position; matching is
    /// exact first, then separator-insensitive, then a unique last-name-segment fallback
    /// (which bridges a <c>.lox</c> file's survey-local station names against the leads
    /// register's fully-qualified names). Leads whose station can't be resolved are
    /// dropped. Result leads are ordered by station name for determinism.
    /// </summary>
    public static SceneMeta Enrich(SceneMeta meta, IReadOnlyList<SourceLead> leads)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(leads);
        if (leads.Count == 0) return meta with { Leads = [] };

        char separator = meta.Source.Separator is { Length: > 0 } s ? s[0] : '.';

        var byName = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        var byNormalized = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        // Last-segment index; a segment that occurs more than once is ambiguous and removed.
        var byLastSegment = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        var ambiguousSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var station in meta.Stations)
        {
            byName.TryAdd(station.Name, station.Position);
            byNormalized.TryAdd(Normalize(station.Name, separator), station.Position);

            var segment = LastSegment(station.Name, separator);
            if (ambiguousSegments.Contains(segment)) continue;
            if (byLastSegment.TryGetValue(segment, out _))
            {
                byLastSegment.Remove(segment);
                ambiguousSegments.Add(segment);
            }
            else
            {
                byLastSegment[segment] = station.Position;
            }
        }

        var matched = new List<SceneMetaLead>();
        foreach (var lead in leads)
        {
            if (TryResolve(lead.Station, separator, byName, byNormalized, byLastSegment, out var position))
                matched.Add(new SceneMetaLead { Station = lead.Station, Position = position, Note = lead.Note });
        }

        matched.Sort((a, b) => string.CompareOrdinal(a.Station, b.Station));
        return meta with { Leads = matched };
    }

    private static bool TryResolve(
        string station, char separator,
        Dictionary<string, SceneMetaVec> byName,
        Dictionary<string, SceneMetaVec> byNormalized,
        Dictionary<string, SceneMetaVec> byLastSegment,
        out SceneMetaVec position)
    {
        if (byName.TryGetValue(station, out position)) return true;
        if (byNormalized.TryGetValue(Normalize(station, separator), out position)) return true;
        if (byLastSegment.TryGetValue(LastSegment(station, separator), out position)) return true;
        position = default!;
        return false;
    }

    private static string Normalize(string name, char separator)
        => separator == '.' ? name : name.Replace(separator, '.');

    private static string LastSegment(string name, char separator)
    {
        int idx = name.LastIndexOf(separator);
        return idx >= 0 && idx < name.Length - 1 ? name[(idx + 1)..] : name;
    }
}
