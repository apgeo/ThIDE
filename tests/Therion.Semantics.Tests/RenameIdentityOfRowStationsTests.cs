// Rename and Find All References turn a clicked token into a symbol identity. Two stations first
// declared by the same data row must still resolve to two different symbols.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class RenameIdentityOfRowStationsTests
{
    private const string Upper = """
        survey upper
          centreline
            data normal from to length compass clino
            p1 p2 10 0 0
          endcentreline
        endsurvey
        """;

    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
            parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    [Fact]
    public void The_to_station_of_a_first_row_resolves_to_itself_not_to_the_from_station()
    {
        var ws = Build(("/p/a.th", Upper));
        var span = new WorkspaceSymbolNavigationService(ws).GoToDefinition("p2@upper", ReferenceKind.Any);
        Assert.NotNull(span);

        var resolved = SymbolReferences.ResolveDeclaration(ws, ReferenceKind.Any, span!.Value);

        Assert.NotNull(resolved);
        Assert.Equal("upper.p2", resolved!.Value.Symbol.Name.ToString());
    }

    [Fact]
    public void Find_all_references_of_the_to_station_lists_only_its_own_tokens()
    {
        var ws = Build(("/p/a.th", Upper));

        var refs = SymbolReferences.FindAll(ws, "p2@upper", ReferenceKind.Any);

        Assert.NotEmpty(refs);
        var texts = refs.Select(r => Upper.Substring(r.Span.StartOffset, r.Span.Length)).ToList();
        Assert.All(texts, t => Assert.Equal("p2", t));
    }
}
