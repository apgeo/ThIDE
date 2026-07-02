// The mutation catalog: (seed, mutation) cases consumed by MutationTests.
// Grow it per C batch (D1) — new checks get mutations that provoke them.
// All mutations are deterministic: string surgery, or Random seeded per case.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests.Mutation;

public static class MutationCatalog
{
    /// <summary>All (seedId, mutation) cases; xUnit MemberData-friendly.</summary>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (seedId, mutation) in All)
            yield return new object[] { seedId, mutation.Id };
    }

    public static readonly ImmutableArray<(string SeedId, SyntaxMutation Mutation)> All =
        ImmutableArray.Create<(string, SyntaxMutation)>(
            // --- Tier 1: invalid by construction --------------------------------------
            ("th", new SyntaxMutation(
                "th-drop-endsurvey", "delete the endsurvey line → unterminated block",
                MutationTier.Tier1, DiagnosticCodes.UnterminatedBlock,
                (src, _) => ReplaceOnce(src, "endsurvey", ""))),

            ("th", new SyntaxMutation(
                "th-fix-nonnumeric", "corrupt a fix coordinate → malformed fix",
                MutationTier.Tier1, DiagnosticCodes.MalformedFix,
                (src, _) => ReplaceOnce(src, "fix 1 500", "fix 1 5x0"))),

            ("th", new SyntaxMutation(
                "th-illegal-id-char", "inject '&' into the survey id → illegal identifier",
                MutationTier.Tier1, DiagnosticCodes.IllegalIdentifier,
                (src, _) => ReplaceOnce(src, "survey main", "survey ma&in"))),

            ("th", new SyntaxMutation(
                "th-endsurvey-id-mismatch", "close 'survey main' with 'endsurvey other'",
                MutationTier.Tier1, DiagnosticCodes.BlockIdMismatch,
                (src, _) => ReplaceOnce(src, "endsurvey", "endsurvey other"))),

            ("th", new SyntaxMutation(
                "th-bad-mark-type", "misspell the mark type → unknown mark type",
                MutationTier.Tier1, DiagnosticCodes.InvalidMarkType,
                (src, _) => ReplaceOnce(src, "mark 2 fixed", "mark 2 fixxed"))),

            ("th", new SyntaxMutation(
                "th-bad-data-style", "misspell the data style → unknown data style",
                MutationTier.Tier1, DiagnosticCodes.UnknownDataStyle,
                (src, _) => ReplaceOnce(src, "data normal", "data nromal"))),

            ("th2", new SyntaxMutation(
                "th2-point-nonnumeric", "corrupt a point coordinate → malformed point",
                MutationTier.Tier1, DiagnosticCodes.Th2MalformedPoint,
                (src, _) => ReplaceOnce(src, "point 100 200", "point 10x0 200"))),

            ("th2", new SyntaxMutation(
                "th2-drop-endscrap", "delete the endscrap line → unterminated scrap",
                MutationTier.Tier1, DiagnosticCodes.Th2UnterminatedScrap,
                (src, _) => ReplaceOnce(src, "endscrap", ""))),

            ("thconfig", new SyntaxMutation(
                "thconfig-bad-export-type", "misspell the export type",
                MutationTier.Tier1, DiagnosticCodes.UnknownExportType,
                (src, _) => ReplaceOnce(src, "export map ", "export mapp "))),

            ("thconfig", new SyntaxMutation(
                "thconfig-fmt-type-mismatch", "'sql' is a database format, not a map format",
                MutationTier.Tier1, DiagnosticCodes.UnknownExportFormat,
                (src, _) => ReplaceOnce(src, "export map -fmt pdf", "export map -fmt sql"))),

            // --- Tier 1: C1 centreline rules (spec §5.2/§5.3) --------------------------
            ("th", new SyntaxMutation(
                "th-style-reading-mismatch", "switch style to diving → gradient invalid for style",
                MutationTier.Tier1, DiagnosticCodes.InvalidReadingForStyle,
                (src, _) => ReplaceOnce(src, "data normal from", "data diving from"))),

            ("th", new SyntaxMutation(
                "th-duplicate-reading", "duplicate the length column (tape ≡ length)",
                MutationTier.Tier1, DiagnosticCodes.DuplicateReading,
                (src, _) => ReplaceOnce(src, "to length compass", "to length tape compass"))),

            ("th", new SyntaxMutation(
                "th-book-only-team-role", "add the book-only 'explorer' role (compiler rejects)",
                MutationTier.Tier1, DiagnosticCodes.ValueTypeMismatch,
                (src, _) => ReplaceOnce(src, "team \"John Doe\"", "team \"John Doe\" explorer"))),

            ("th", new SyntaxMutation(
                "th-fix-nonpositive-sd", "append a zero std deviation to fix (must be > 0)",
                MutationTier.Tier1, DiagnosticCodes.ValueOutOfRange,
                (src, _) => ReplaceOnce(src, "fix 1 500 600 700", "fix 1 500 600 700 0"))),

            ("th", new SyntaxMutation(
                "th-direct-fixed-station-flag", "set the fixed station flag directly",
                MutationTier.Tier1, DiagnosticCodes.InvalidStationFlag,
                (src, _) => ReplaceOnce(src, "\"junction\" continuation", "\"junction\" fixed"))),

            ("th", new SyntaxMutation(
                "th-extend-too-many-stations", "extend with 4 station arguments (max 3)",
                MutationTier.Tier1, DiagnosticCodes.TooManyArguments,
                (src, _) => ReplaceOnce(src, "extend left", "extend left 1 2 3 4"))),

            ("th", new SyntaxMutation(
                "th-declination-without-units", "numeric declination requires angular units",
                MutationTier.Tier1, DiagnosticCodes.MalformedDeclination,
                (src, _) => ReplaceOnce(src, "units length metres",
                    "units length metres\n    declination 3"))),

            // --- Tier 2: probably invalid (random, position-seeded) --------------------
            ("th", new SyntaxMutation(
                "th-random-digit-corruption", "replace one random digit with 'x'",
                MutationTier.Tier2, null,
                (src, rng) =>
                {
                    var digits = new List<int>();
                    for (int i = 0; i < src.Length; i++)
                        if (char.IsDigit(src[i])) digits.Add(i);
                    if (digits.Count == 0) return null;
                    int at = digits[rng.Next(digits.Count)];
                    return src[..at] + 'x' + src[(at + 1)..];
                })));

    /// <summary>Replaces the first occurrence, or returns null when the anchor is absent
    /// (the case is then skipped — keeps mutations reusable across evolving seeds).</summary>
    private static string? ReplaceOnce(string src, string oldValue, string newValue)
    {
        int at = src.IndexOf(oldValue, StringComparison.Ordinal);
        if (at < 0) return null;
        return src[..at] + newValue + src[(at + oldValue.Length)..];
    }

    public static SyntaxMutation Get(string mutationId)
    {
        foreach (var (_, m) in All)
            if (m.Id == mutationId) return m;
        throw new ArgumentException($"Unknown mutation id '{mutationId}'.");
    }

    /// <summary>Parses <paramref name="text"/> with the parser matching <paramref name="extension"/>.</summary>
    public static ImmutableArray<Diagnostic> ParseDiagnostics(string extension, string text) =>
        extension switch
        {
            ".th" => new ThParser().Parse("/mut/seed.th", text).Diagnostics,
            ".th2" => new Th2Parser().Parse("/mut/seed.th2", text).Diagnostics,
            ".thconfig" => new ThconfigParser().Parse("/mut/seed.thconfig", text).Diagnostics,
            _ => throw new ArgumentException($"No parser route for '{extension}'."),
        };
}
