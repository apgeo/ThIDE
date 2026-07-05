// The editor's "go to parent file" button resolves the file(s) that include the current one
// from the workspace object graph (FileGraphEdges). These cover the pure resolver behind it.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Semantics;
using TherionProc.ViewModels.Docking;
using Xunit;

namespace TherionProc.Tests;

public class ParentFileNavigationTests
{
    // A workspace snapshot carrying only the file-inclusion edges we want to test against.
    private static WorkspaceSemanticModel WorkspaceWith(params (string From, string To)[] edges) =>
        new(FrozenDictionary<string, SemanticModel>.Empty,
            XviIndex.Empty,
            edges.ToImmutableArray(),
            ImmutableArray<Diagnostic>.Empty);

    private static string Full(string relative) => Path.GetFullPath(relative);

    [Fact]
    public void Root_file_with_no_includers_has_no_parent()
    {
        var child = Full("cave/main.th");
        var ws = WorkspaceWith((Full("cave/project.thconfig"), Full("cave/other.th")));

        Assert.Empty(FileDocumentViewModel.ComputeParentFiles(ws, child));
    }

    [Fact]
    public void Single_includer_is_returned_as_the_parent()
    {
        var parent = Full("cave/project.thconfig");
        var child = Full("cave/main.th");
        var ws = WorkspaceWith((parent, child));

        Assert.Equal(new[] { parent }, FileDocumentViewModel.ComputeParentFiles(ws, child));
    }

    [Fact]
    public void Multiple_includers_are_returned_distinct_and_sorted()
    {
        var child = Full("cave/shared.th");
        var a = Full("cave/a.th");
        var b = Full("cave/b.th");
        // b listed first, and a duplicate edge, to prove dedup + stable ordering.
        var ws = WorkspaceWith((b, child), (a, child), (b, child));

        Assert.Equal(new[] { a, b }, FileDocumentViewModel.ComputeParentFiles(ws, child));
    }

    [Fact]
    public void Child_path_match_is_case_insensitive()
    {
        var parent = Full("cave/project.thconfig");
        var child = Full("cave/Main.th");
        var ws = WorkspaceWith((parent, child));

        Assert.Equal(new[] { parent },
            FileDocumentViewModel.ComputeParentFiles(ws, child.ToUpperInvariant()));
    }

    [Fact]
    public void A_self_loop_is_not_reported_as_its_own_parent()
    {
        var file = Full("cave/main.th");
        var ws = WorkspaceWith((file, file));

        Assert.Empty(FileDocumentViewModel.ComputeParentFiles(ws, file));
    }

    [Fact]
    public void Null_workspace_yields_no_parent()
    {
        Assert.Empty(FileDocumentViewModel.ComputeParentFiles(null, Full("cave/main.th")));
    }
}
