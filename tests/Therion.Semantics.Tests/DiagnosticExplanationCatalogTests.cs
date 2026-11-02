// The "explain this diagnostic" table must describe the codes the parsers and binder actually emit.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Therion.Semantics.Thbook;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class DiagnosticExplanationCatalogTests
{
    private static IEnumerable<string> Constants(Type catalog) =>
        catalog.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static IReadOnlyDictionary<string, DiagnosticExplanation> Table()
    {
        var field = typeof(DiagnosticExplanations).GetField("Map", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (IReadOnlyDictionary<string, DiagnosticExplanation>)field!.GetValue(null)!;
    }

    [Fact]
    public void Every_explained_code_is_one_some_layer_emits()
    {
        var emitted = Constants(typeof(DiagnosticCodes))
            .Concat(Constants(typeof(SemanticDiagnosticCodes)))
            .ToHashSet(StringComparer.Ordinal);

        var dead = Table().Keys.Where(k => !emitted.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.True(dead.Length == 0, "explained but never emitted: " + string.Join(", ", dead));
    }

    [Fact]
    public void Unknown_xvi_variable_is_not_explained_as_a_missing_file()
    {
        var ex = DiagnosticExplanations.For(DiagnosticCodes.XviUnknownVariable);

        Assert.NotNull(ex);
        Assert.DoesNotContain("not found", ex!.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_xvi_file_and_unterminated_block_have_explanations()
    {
        Assert.NotNull(DiagnosticExplanations.For(SemanticDiagnosticCodes.XviFileMissing));
        Assert.NotNull(DiagnosticExplanations.For(DiagnosticCodes.UnterminatedBlock));
    }

    [Fact]
    public void Every_doc_term_opens_a_thbook_page()
    {
        var missing = Table()
            .Where(kv => !string.IsNullOrEmpty(kv.Value.DocTerm) && ThbookIndex.Lookup(kv.Value.DocTerm!) is null)
            .Select(kv => kv.Key + "→" + kv.Value.DocTerm)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, "doc terms with no thbook page: " + string.Join(", ", missing));
    }
}
