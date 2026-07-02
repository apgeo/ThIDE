// Mutation-harness tests (syntax-coverage effort, A1 — PLAN §8, D1 grows the catalog).
// Data-driven: every (seed, mutation) pair in MutationCatalog runs through the same
// two assertions, so new file types / mutations need no new test code.

using System;
using System.Linq;
using Therion.Core;

namespace Therion.Syntax.Tests.Mutation;

public class MutationTests
{
    /// <summary>Deterministic per-case RNG so Tier2 mutations are reproducible.</summary>
    private static Random RngFor(string seedId, string mutationId) =>
        new(StringComparer.Ordinal.GetHashCode(seedId + "|" + mutationId) & int.MaxValue);

    [Theory]
    [InlineData("th")]
    [InlineData("th2")]
    [InlineData("thconfig")]
    public void Seeds_parse_clean(string seedId)
    {
        var (ext, text) = MutationSeeds.ById[seedId];
        var diags = MutationCatalog.ParseDiagnostics(ext, text);
        Assert.True(diags.IsEmpty,
            $"Seed '{seedId}' must parse with zero diagnostics, got: " +
            string.Join("; ", diags.Select(d => $"{d.Code}:{d.Message}")));
    }

    [Theory]
    [MemberData(nameof(MutationCatalog.Cases), MemberType = typeof(MutationCatalog))]
    public void Mutant_is_flagged(string seedId, string mutationId)
    {
        var (ext, seedText) = MutationSeeds.ById[seedId];
        var mutation = MutationCatalog.Get(mutationId);

        var mutated = mutation.Apply(seedText, RngFor(seedId, mutationId));
        if (mutated is null) return; // not applicable / accidentally valid → skip

        var seedDiags = MutationCatalog.ParseDiagnostics(ext, seedText);
        var mutantDiags = MutationCatalog.ParseDiagnostics(ext, mutated);

        if (mutation.Tier == MutationTier.Tier1)
        {
            Assert.False(seedDiags.Any(d => d.Code.Value == mutation.ExpectedCode),
                $"Seed already contains {mutation.ExpectedCode}; mutation '{mutationId}' can't be attributed.");
            Assert.True(mutantDiags.Any(d => d.Code.Value == mutation.ExpectedCode),
                $"Mutation '{mutationId}' expected {mutation.ExpectedCode}, got: " +
                (mutantDiags.IsEmpty ? "(none)" : string.Join("; ", mutantDiags.Select(d => $"{d.Code}:{d.Message}"))));
        }
        else
        {
            // Tier2: the mutation may accidentally stay valid; the invariant is only that
            // corrupting a clean seed never REMOVES diagnostics.
            Assert.True(mutantDiags.Length >= seedDiags.Length,
                $"Mutation '{mutationId}' unexpectedly reduced diagnostics " +
                $"({seedDiags.Length} → {mutantDiags.Length}).");
        }
    }

    // D2 (user aim: tests with validation parts disabled): with the master switch off, NO
    // schema-pass diagnostic may appear on any mutant — proving the pass is fully skippable.
    private static readonly string[] SchemaPassCodes =
    {
        DiagnosticCodes.MissingRequiredArgument, DiagnosticCodes.TooManyArguments,
        DiagnosticCodes.ValueTypeMismatch, DiagnosticCodes.OptionNotValidInContext,
        DiagnosticCodes.KeywordCaseMismatch, DiagnosticCodes.InvalidSpecialValue,
        DiagnosticCodes.ValueOutOfRange,
        DiagnosticCodes.InvalidReadingForStyle, DiagnosticCodes.DuplicateReading,
        DiagnosticCodes.IncompleteDataOrder, DiagnosticCodes.InvalidNewlinePosition,
        DiagnosticCodes.InterleavedMix, DiagnosticCodes.Th2UnknownSubtype,
    };

    [Theory]
    [MemberData(nameof(MutationCatalog.Cases), MemberType = typeof(MutationCatalog))]
    public void Schema_pass_is_fully_skippable(string seedId, string mutationId)
    {
        var (ext, seedText) = MutationSeeds.ById[seedId];
        var mutation = MutationCatalog.Get(mutationId);
        var mutated = mutation.Apply(seedText, RngFor(seedId, mutationId));
        if (mutated is null) return;

        var off = new ParserOptions(Validation: Therion.Syntax.Schema.SchemaValidationOptions.Off);
        var diags = ext switch
        {
            ".th" => new ThParser().Parse("/mut/a.th", mutated, off).Diagnostics,
            ".th2" => new Th2Parser().Parse("/mut/a.th2", mutated, off).Diagnostics,
            _ => new ThconfigParser().Parse("/mut/a.thconfig", mutated, off).Diagnostics,
        };
        foreach (var d in diags)
            Assert.DoesNotContain(d.Code.Value, SchemaPassCodes);
    }
}
