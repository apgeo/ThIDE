// The Diagnostics panel's project scope lists each finding of the files that are not active once:
// the workspace snapshot's list already carries every file's semantic diagnostics, so the panel
// must not add the per-file models on top. Invented paths and survey data only.

using System.Collections.Generic;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace ThIDE.Tests;

public class ProjectScopeDiagnosticsTests
{
    private const string Main = "/invented/project/main.th";
    private const string Side = "/invented/project/side.th";

    private static WorkspaceSemanticModel Workspace()
    {
        var parser = new ThParser();
        var parsed = new Dictionary<string, ParseResult<TherionFile>>
        {
            [Main] = parser.Parse(Main, "survey main\n  input side.th\nendsurvey\n"),
            // 'four' is not a length (TH_SEM_006) and 400 is not a compass bearing (TH_SEM_007).
            [Side] = parser.Parse(Side, """
                survey side
                  centreline
                    data normal from to length compass clino
                    1 2 four 10 0
                    2 3 5 400 0
                  endcentreline
                endsurvey
                """),
        };
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => false);
    }

    [Fact]
    public void A_file_that_is_not_active_has_each_semantic_finding_listed_once() => UiThread.Run(() =>
    {
        var docs = new ScriptedDocumentService { Workspace = Workspace() };
        var vm = MainWindowViewModelHost.Create(docs);
        docs.OpenTextDocument(Main, "survey main\n  input side.th\nendsurvey\n");   // side.th is not the active tab

        vm.Diagnostics.ShowProjectScope = true;
        docs.RaiseDocumentChanged();

        var sideFindings = vm.Diagnostics.All
            .Where(d => d.Span.FilePath == Side && d.Code.Value is SemanticDiagnosticCodes.DataValueInvalid or SemanticDiagnosticCodes.DataValueRange)
            .ToList();
        Assert.Single(sideFindings, d => d.Code.Value == SemanticDiagnosticCodes.DataValueInvalid);
        Assert.Single(sideFindings, d => d.Code.Value == SemanticDiagnosticCodes.DataValueRange);
        Assert.Equal(vm.Diagnostics.All.Count, vm.Diagnostics.All.Distinct().Count());
    });
}
