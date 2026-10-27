using System.Collections.Immutable;

namespace Therion.Semantics.Linking;

/// <summary>
/// Writes and reads station references relative to the survey a command is written in. Paths are
/// effective (workspace-wide, top-down) — see <see cref="FileScopes"/>.
/// </summary>
public static class StationReference
{
    /// <summary>
    /// The token naming <paramref name="station"/> from inside survey <paramref name="scope"/>:
    /// the bare point when the station's survey is the scope itself, otherwise <c>point@inner.outer</c>
    /// with the survey path below the scope written bottom-up. Null when the scope is not an ancestor
    /// of (or equal to) the station's survey — Therion cannot reach upwards with <c>@</c>, so the
    /// command has to go higher up. The station token is never split on '.'.
    /// </summary>
    public static string? Format(QualifiedName station, ImmutableArray<string> scope)
    {
        var survey = station.Parts.AsSpan(0, station.Parts.Length - 1);
        if (!IsPrefix(scope.AsSpan(), survey)) return null;

        var below = survey[scope.Length..];
        if (below.IsEmpty) return station.Last;

        var bottomUp = below.ToArray();
        Array.Reverse(bottomUp);
        return station.Last + "@" + string.Join('.', bottomUp);
    }

    /// <summary>
    /// The effective name of a reference written inside <paramref name="scope"/> — the inverse of
    /// <see cref="Format"/>.
    /// </summary>
    public static QualifiedName Resolve(string reference, ImmutableArray<string> scope)
    {
        var parsed = StationRef.Parse(reference);
        var surveys = parsed.HasSurvey ? scope.AddRange(parsed.SurveyPathTopDown) : scope;
        return QualifiedName.OfStation(surveys, parsed.Point);
    }

    /// <summary>True when <paramref name="prefix"/> is a (not necessarily proper) prefix of <paramref name="path"/>.</summary>
    public static bool IsPrefix(ReadOnlySpan<string> prefix, ReadOnlySpan<string> path)
    {
        if (prefix.Length > path.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (!string.Equals(prefix[i], path[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
