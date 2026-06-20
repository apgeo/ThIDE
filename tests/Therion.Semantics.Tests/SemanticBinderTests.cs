// M3 semantic tests — bind the synthetic cave.th fixture and assert
// station/survey indexes, qualified-name resolution, and equate graph.

using System.Collections.Immutable;
using System.IO;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SemanticBinderTests
{
    private static string LoadCorpusCave()
    {
        // tests bin/<cfg>/net8.0/ ? walk up to repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tests", "Corpus", "Synthetic", "project", "cave.th")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "Corpus", "Synthetic", "project", "cave.th");
    }

    private static SemanticModel BindFile(string path)
    {
        var text = File.ReadAllText(path);
        var parse = new ThParser().Parse(path, text);
        return new SemanticBinder().Bind(parse.Value);
    }

    [Fact]
    public void Cave_corpus_binds_with_no_semantic_errors()
    {
        var model = BindFile(LoadCorpusCave());
        Assert.DoesNotContain(model.Diagnostics, d => d.Severity == Core.DiagnosticSeverity.Error);
    }

    [Fact]
    public void Cave_corpus_indexes_nested_surveys()
    {
        var model = BindFile(LoadCorpusCave());
        Assert.True(model.Surveys.ContainsKey(QualifiedName.Of("cave")));
        Assert.True(model.Surveys.ContainsKey(QualifiedName.Of("cave", "upper")));
    }

    [Fact]
    public void Cave_corpus_qualifies_shot_stations_with_survey_path()
    {
        var model = BindFile(LoadCorpusCave());
        Assert.True(model.Stations.ContainsKey(QualifiedName.Of("cave", "0")));
        Assert.True(model.Stations.ContainsKey(QualifiedName.Of("cave", "1")));
        Assert.True(model.Stations.ContainsKey(QualifiedName.Of("cave", "upper", "u1")));
    }

    [Fact]
    public void Cave_corpus_records_fix_declaration_on_station()
    {
        var model = BindFile(LoadCorpusCave());
        var fix0 = model.Stations[QualifiedName.Of("cave", "0")];
        Assert.Equal(StationDeclarationKind.Fix, fix0.Kind);
    }

    [Fact]
    public void Equate_unifies_local_and_absolute_stations()
    {
        var model = BindFile(LoadCorpusCave());
        var u1 = QualifiedName.Of("cave", "upper", "u1");
        var cave0 = QualifiedName.Of("cave", "0");
        Assert.True(model.Equates.Find(u1).Equals(model.Equates.Find(cave0)));
    }"C:\Users\Z\source\repos\TherionProc\TherionProc\bin\Debug\net8.0\TherionProc.exe"

    [Fact]
    public void GoToDefinition_resolves_qualified_station()
    {
        var model = BindFile(LoadCorpusCave());
        var nav = new SymbolNavigationService(model);
        var span = nav.GoToDefinition("cave.0");
        Assert.NotNull(span);
    }

    [Fact]
    public void Unresolved_equate_target_emits_TH_SEM_001()
    {
        const string src = """
            survey foo
              centreline
                data normal from to length compass clino
                  a b 1 0 0
              endcentreline
              equate a missing.zz
            endsurvey
            """;
        var parse = new ThParser().Parse("inline.th", src);
        var model = new SemanticBinder().Bind(parse.Value);
        Assert.Contains(model.Diagnostics,
            d => d.Code.Value == SemanticDiagnosticCodes.UnresolvedStation);
    }
}
