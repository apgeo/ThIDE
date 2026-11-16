// Leads-register enrichment (BA-B4): resolve each workspace lead to a 3-D position by
// matching its station name against the metadata's labelled stations, and write the
// results into scene-meta.json's `leads` array (local coordinates, matching the mesh).
// Unmatched leads (e.g. a lead on an anonymous/absent station) are dropped.

using System.Diagnostics.CodeAnalysis;
namespace Therion.Blender.Sources;

/// <summary>Adds positioned leads to a <see cref="SceneMeta"/> document.</summary>
public static class LeadsEnricher
{
    /// <summary>
    /// Returns a copy of <paramref name="meta"/> with its <c>Leads</c> populated from
    /// <paramref name="leads"/>. A lead is placed at its station's position; matching tries
    /// the survey-qualified names of a <c>.lox</c> file's survey tree first (see
    /// <see cref="QualifiedNames"/>), then the station names exactly, then
    /// separator-insensitive, then a unique last-name-segment fallback (which bridges a <c>.lox</c> file's survey-local station names against the leads
    /// register's fully-qualified names). Leads whose station can't be resolved are
    /// dropped. Result leads are ordered by station name for determinism.
    /// </summary>
    public static SceneMeta Enrich(SceneMeta meta, IReadOnlyList<SourceLead> leads)
    {
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(leads);
        if (leads.Count == 0) return meta with { Leads = [] };

        char separator = meta.Source.Separator is { Length: > 0 } s ? s[0] : '.';

        // Each index keeps only names that occur once: a name shared by several stations (a
        // .lox file's survey-local "7", or a last segment) would place the lead arbitrarily.
        var byName = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        var byNormalized = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        var byLastSegment = new Dictionary<string, SceneMetaVec>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = (
            Name: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Normalized: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Segment: new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        foreach (var station in meta.Stations)
        {
            AddUnique(byName, ambiguous.Name, station.Name, station.Position);
            AddUnique(byNormalized, ambiguous.Normalized, Normalize(station.Name, separator), station.Position);
            AddUnique(byLastSegment, ambiguous.Segment, LastSegment(station.Name, separator), station.Position);
        }

        var qualified = QualifiedNames(meta);

        var matched = new List<SceneMetaLead>();
        foreach (var lead in leads)
        {
            if (TryResolveQualified(lead.Station, separator, qualified, out var position)
                || TryResolve(lead.Station, separator, byName, byNormalized, byLastSegment, out position))
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
        [MaybeNullWhen(false)] out SceneMetaVec position)
    {
        if (byName.TryGetValue(station, out position)) return true;
        if (byNormalized.TryGetValue(Normalize(station, separator), out position)) return true;
        if (byLastSegment.TryGetValue(LastSegment(station, separator), out position)) return true;
        position = null;
        return false;
    }

    /// <summary>
    /// The top-down dotted name of each station a <c>.lox</c> file places in its survey tree:
    /// "cave.branch.7" for station "7" of survey "branch" inside "cave" — the form the
    /// workspace's leads register uses. A <c>.lox</c> file's station names are local to their
    /// survey, so "7" alone usually names a station in several surveys. Empty when the document
    /// has no survey tree (<c>.3d</c>, whose names are already qualified).
    /// </summary>
    private static List<(string Name, SceneMetaVec Position)> QualifiedNames(SceneMeta meta)
    {
        var result = new List<(string, SceneMetaVec)>();
        if (meta.Surveys.Count == 0) return result;
        var surveys = new Dictionary<uint, SceneMetaSurvey>();
        foreach (var survey in meta.Surveys) surveys.TryAdd(survey.Id, survey);

        var paths = new Dictionary<uint, string>();
        foreach (var station in meta.Stations)
        {
            if (station.SurveyId is not { } id || !surveys.ContainsKey(id)) continue;
            if (!paths.TryGetValue(id, out var path)) paths[id] = path = SurveyPath(id, surveys);
            result.Add((path.Length == 0 ? station.Name : path + "." + station.Name, station.Position));
        }
        return result;
    }

    private static string SurveyPath(uint id, Dictionary<uint, SceneMetaSurvey> surveys)
    {
        var names = new List<string>();
        var seen = new HashSet<uint>();
        // The root survey is its own parent; the guard also stops a malformed cycle.
        while (surveys.TryGetValue(id, out var survey) && seen.Add(id))
        {
            if (survey.Name.Length > 0) names.Add(survey.Name);
            if (survey.ParentId == id) break;
            id = survey.ParentId;
        }
        names.Reverse();
        return string.Join('.', names);
    }

    /// <summary>A lead matched against the survey-qualified names: exactly, or else by a
    /// unique match where one name ends the other on a survey boundary (the workspace and the
    /// file may disagree about an outermost wrapper survey).</summary>
    private static bool TryResolveQualified(
        string station, char separator, List<(string Name, SceneMetaVec Position)> qualified,
        [MaybeNullWhen(false)] out SceneMetaVec position)
    {
        position = null;
        if (qualified.Count == 0) return false;
        string wanted = Normalize(station, separator);
        foreach (var (name, at) in qualified)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                position = at;
                return true;
            }
        }

        SceneMetaVec? found = null;
        int count = 0;
        foreach (var (name, at) in qualified)
        {
            if (!EndsOnBoundary(name, wanted) && !EndsOnBoundary(wanted, name)) continue;
            // Only a name with a survey part counts: a bare local name is the ambiguous case.
            if (!name.Contains('.') || !wanted.Contains('.')) continue;
            found = at;
            if (++count > 1) return false;
        }
        position = found;
        return count == 1;
    }

    private static bool EndsOnBoundary(string longer, string shorter)
        => longer.Length > shorter.Length
           && longer.EndsWith(shorter, StringComparison.OrdinalIgnoreCase)
           && longer[longer.Length - shorter.Length - 1] == '.';

    private static void AddUnique(
        Dictionary<string, SceneMetaVec> index, HashSet<string> ambiguous, string key, SceneMetaVec position)
    {
        if (ambiguous.Contains(key)) return;
        if (index.Remove(key))
            ambiguous.Add(key);
        else
            index[key] = position;
    }

    private static string Normalize(string name, char separator)
        => separator == '.' ? name : name.Replace(separator, '.');

    private static string LastSegment(string name, char separator)
    {
        int idx = name.LastIndexOf(separator);
        return idx >= 0 && idx < name.Length - 1 ? name[(idx + 1)..] : name;
    }
}
