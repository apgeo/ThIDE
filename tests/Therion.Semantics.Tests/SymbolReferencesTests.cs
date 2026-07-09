// Find All References — the read-only half of true rename. Resolution must be scope-correct
// (a station named `1` in another survey is a different symbol) and cross-file.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SymbolReferencesTests
{
    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
            parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    // Two surveys, each with a station `p1`; `upper.p1` is also referenced from an equate in b.th.
    private static WorkspaceSemanticModel TwoSurveys() => Build(
        ("/p/a.th", """
            survey upper
              centreline
                data normal from to length compass clino
                p1 p2 10 0 0
              endcentreline
            endsurvey
            """),
        ("/p/b.th", """
            survey lower
              centreline
                data normal from to length compass clino
                p1 p2 10 0 0
              endcentreline
              equate p1@upper p1
            endsurvey
            """));

    [Fact]
    public void Finds_the_declaration_and_every_reference_of_a_station()
    {
        var ws = TwoSurveys();
        var refs = SymbolReferences.FindAll(ws, "p1@upper", ReferenceKind.Any);

        Assert.NotEmpty(refs);
        Assert.Contains(refs, r => r.IsDeclaration);
        // the shot row in a.th plus the equate in b.th
        Assert.Contains(refs, r => r.Span.FilePath == "/p/a.th");
        Assert.Contains(refs, r => r.Span.FilePath == "/p/b.th");
    }

    [Fact]
    public void A_same_named_station_in_another_survey_is_a_different_symbol()
    {
        var ws = TwoSurveys();
        var upper = SymbolReferences.FindAll(ws, "p1@upper", ReferenceKind.Any);
        var lower = SymbolReferences.FindAll(ws, "p1@lower", ReferenceKind.Any);

        // `lower.p1` is never referenced from a.th; scope, not spelling, decides.
        Assert.DoesNotContain(lower, r => r.Span.FilePath == "/p/a.th");
        Assert.NotEmpty(upper);
        Assert.NotEmpty(lower);
    }

    [Fact]
    public void Results_are_ordered_by_file_then_position()
    {
        var ws = TwoSurveys();
        var refs = SymbolReferences.FindAll(ws, "p1@upper", ReferenceKind.Any);

        var ordered = refs
            .OrderBy(r => r.Span.FilePath, System.StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Span.StartOffset)
            .ToList();
        Assert.Equal(ordered, refs);
    }

    [Fact]
    public void Exactly_one_occurrence_is_marked_as_the_declaration()
    {
        var ws = TwoSurveys();
        var refs = SymbolReferences.FindAll(ws, "p1@upper", ReferenceKind.Any);
        Assert.Equal(1, refs.Count(r => r.IsDeclaration));
    }

    [Fact]
    public void A_survey_name_resolves_to_its_own_symbol()
    {
        var ws = TwoSurveys();
        var refs = SymbolReferences.FindAll(ws, "upper", ReferenceKind.Survey);
        Assert.Contains(refs, r => r.IsDeclaration && r.Span.FilePath == "/p/a.th");
    }

    [Fact]
    public void An_unknown_token_yields_no_references()
        => Assert.Empty(SymbolReferences.FindAll(TwoSurveys(), "nosuchstation", ReferenceKind.Any));

    [Fact]
    public void A_blank_token_yields_no_references()
        => Assert.Empty(SymbolReferences.FindAll(TwoSurveys(), "   ", ReferenceKind.Any));
}


