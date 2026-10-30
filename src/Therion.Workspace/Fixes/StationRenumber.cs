// Re-prefix or renumber every station of a survey at once, on top of the rename engine's occurrence
// index: the same scope-correct, @-aware token edits as a single rename, for the whole set. Built for
// "the trip used 1, 2, 3… and the cave wants A1, A2, A3…" and "shift this survey's numbers by 100".

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Fixes;

/// <summary>
/// How each station name changes: names starting with <see cref="OldPrefix"/> lose it and get
/// <see cref="NewPrefix"/>; with a non-zero <see cref="Offset"/> the number after the prefix is
/// shifted (zero padding kept). Names that do not fit are left alone.
/// </summary>
/// <param name="OldPrefix">The prefix to take off ("" = every name).</param>
/// <param name="NewPrefix">The prefix to put on.</param>
/// <param name="Offset">Added to the number that follows the old prefix (0 = numbers unchanged).</param>
public sealed record RenumberRule(string OldPrefix, string NewPrefix, int Offset = 0)
{
    /// <summary>The new name, or null when the rule leaves <paramref name="name"/> alone.</summary>
    public string? Apply(string name)
    {
        if (!name.StartsWith(OldPrefix, StringComparison.Ordinal)) return null;
        var rest = name[OldPrefix.Length..];
        if (Offset != 0)
        {
            var m = Regex.Match(rest, @"^(\d+)(.*)$", RegexOptions.CultureInvariant);
            if (!m.Success) return null;
            var digits = m.Groups[1].Value;
            var n = BigInteger.Parse(digits, CultureInfo.InvariantCulture) + Offset;
            if (n < 0) return null;
            rest = n.ToString(CultureInfo.InvariantCulture).PadLeft(digits.Length, '0') + m.Groups[2].Value;
        }
        var result = NewPrefix + rest;
        return result == name || result.Length == 0 ? null : result;
    }

    public bool IsNoOp => OldPrefix == NewPrefix && Offset == 0;
}

public enum RenumberProblemKind
{
    /// <summary>The new name cannot be a Therion station name (Detail = the name).</summary>
    InvalidName,
    /// <summary>Two stations would get the same name (Detail = the name).</summary>
    Duplicate,
    /// <summary>The new name is already a station that is not renamed (Detail = the name).</summary>
    Collides,
    /// <summary>A file no longer holds the text the model was built from (Detail = the file).</summary>
    Stale,
    /// <summary>The survey is not in the model (Detail = its name).</summary>
    NoSurvey,
}

public sealed record RenumberProblem(RenumberProblemKind Kind, string Detail, string? Station = null);

/// <param name="Renames">Old → new, by full station name.</param>
/// <param name="Unchanged">Stations the rule leaves alone (no matching prefix, or no number to shift).</param>
public sealed record RenumberPlan(
    IReadOnlyList<(QualifiedName Station, string NewName)> Renames,
    IReadOnlyList<QualifiedName> Unchanged,
    IReadOnlyList<RenumberProblem> Problems,
    EditBatch Batch)
{
    public bool Ok => Problems.Count == 0 && !Batch.IsEmpty;
}

public static class StationRenumber
{
    private static readonly char[] Structural = ['.', '@', ':', '/'];

    /// <summary>
    /// The edits that apply <paramref name="rule"/> to the stations declared directly in
    /// <paramref name="survey"/> (a full dotted name), and in its sub-surveys when
    /// <paramref name="includeSubsurveys"/>. Nothing is planned when any problem is found.
    /// </summary>
    public static RenumberPlan Plan(WorkspaceSemanticModel model, string survey, RenumberRule rule, bool includeSubsurveys, Func<string, string?> readText)
    {
        if (!model.SurveysByFullName.ContainsKey(survey))
            return new RenumberPlan([], [], [new RenumberProblem(RenumberProblemKind.NoSurvey, survey)], EditBatch.Empty);
        var scope = QualifiedName.Parse(survey);

        bool InScope(QualifiedName station) =>
            station.HasParent && (station.Parent().Equals(scope) || includeSubsurveys && IsUnder(station.Parent(), scope));

        var stations = model.StationOccurrences.Keys
            .Where(k => k.Kind == SymbolKind.Station && InScope(k.Name))
            .Select(k => k.Name)
            .Distinct()
            .OrderBy(n => n.ToString(), NaturalStringComparer.Instance)
            .ToList();

        var renames = new List<(QualifiedName Station, string NewName)>();
        var unchanged = new List<QualifiedName>();
        var problems = new List<RenumberProblem>();
        foreach (var s in stations)
        {
            if (rule.Apply(s.Last) is { } n) renames.Add((s, n));
            else unchanged.Add(s);
        }

        // Each survey is its own namespace: check new names against the stations staying in it.
        var renamed = renames.Select(r => r.Station).ToHashSet();
        var byParent = model.StationsByQn.Values.Select(v => v.Name).Concat(stations)
            .Where(q => q.HasParent && !renamed.Contains(q))
            .ToLookup(q => q.Parent(), q => q.Last);
        foreach (var group in renames.GroupBy(r => r.Station.Parent()))
        {
            var staying = byParent[group.Key].ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (station, newName) in group)
            {
                if (InvalidName(newName)) problems.Add(new RenumberProblem(RenumberProblemKind.InvalidName, newName, station.ToString()));
                else if (!seen.Add(newName)) problems.Add(new RenumberProblem(RenumberProblemKind.Duplicate, newName, station.ToString()));
                else if (staying.Contains(newName)) problems.Add(new RenumberProblem(RenumberProblemKind.Collides, newName, station.ToString()));
            }
        }
        if (problems.Count > 0 || renames.Count == 0) return new RenumberPlan(renames, unchanged, problems, EditBatch.Empty);

        // Every occurrence becomes an edit; a span that no longer slices to the old name means the
        // model is stale, and the whole plan is refused rather than renaming some occurrences.
        var byFile = new Dictionary<string, List<TextEdit>>(FilePaths.Comparer);
        var texts = new Dictionary<string, string?>(FilePaths.Comparer);
        foreach (var (station, newName) in renames)
        {
            foreach (var occ in model.FindOccurrences(new SymbolId(SymbolKind.Station, station)))
            {
                var file = occ.Span.FilePath;
                if (string.IsNullOrEmpty(file)) continue;
                if (!texts.TryGetValue(file, out var text)) texts[file] = text = readText(file);
                int start = occ.Span.StartOffset, length = occ.Span.Length;
                if (text is null || length != station.Last.Length || start < 0 || start + length > text.Length || string.CompareOrdinal(text, start, station.Last, 0, length) != 0)
                {
                    problems.Add(new RenumberProblem(RenumberProblemKind.Stale, file, station.ToString()));
                    continue;
                }
                if (!byFile.TryGetValue(file, out var edits)) byFile[file] = edits = [];
                if (edits.All(e => e.Start != start)) edits.Add(new TextEdit(start, length, station.Last, newName));
            }
        }
        if (problems.Count > 0)
            return new RenumberPlan(renames, unchanged, problems.DistinctBy(p => p.Detail).ToList(), EditBatch.Empty);

        var groups = byFile.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new FileTextEdits(p.Key, p.Value.OrderBy(e => e.Start).ToList()))
            .ToList();
        return new RenumberPlan(renames, unchanged, [], new EditBatch(groups, [], []));
    }

    private static bool IsUnder(QualifiedName name, QualifiedName scope) =>
        name.Parts.Length > scope.Parts.Length && name.Parts.Take(scope.Parts.Length).SequenceEqual(scope.Parts, StringComparer.Ordinal);

    private static bool InvalidName(string name) =>
        name.Length == 0 || name[0] == '-' || name.IndexOfAny(Structural) >= 0 || name.Any(char.IsWhiteSpace)
        || TherionIdentifiers.FirstIllegalChar(name) is not null;
}
