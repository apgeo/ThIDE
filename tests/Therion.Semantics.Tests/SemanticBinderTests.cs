// M3 semantic tests � bind the synthetic cave.th fixture and assert
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

    private static SemanticModel BindText(string text)
        => new SemanticBinder().Bind(new ThParser().Parse("decl.th", text).Value);

    [Fact]
    public void Cave_corpus_binds_with_no_semantic_errors()
    {
        var model = BindFile(LoadCorpusCave());
        Assert.DoesNotContain(model.Diagnostics, d => d.Severity == Core.DiagnosticSeverity.Error);
    }

    // STRUCT-01 — the survey `declination` value is surfaced (degrees, east positive).
    [Fact]
    public void Declination_singleValueDegrees_isSurfaced()
    {
        var model = BindText(
            "survey s\n centerline\n  declination 3.5 degrees\n  data normal from to length compass clino\n  a b 5 0 0\n endcenterline\nendsurvey\n");
        Assert.Equal(3.5, model.Declination!.Value, 6);
    }

    [Fact]
    public void Declination_grads_areConvertedToDegrees()
    {
        var model = BindText(
            "survey s\n centerline\n  declination 100 grads\n  data normal from to length compass clino\n  a b 5 0 0\n endcenterline\nendsurvey\n");
        Assert.Equal(90.0, model.Declination!.Value, 6);   // 100 grad = 90°
    }

    [Fact]
    public void Declination_absent_isNull()
    {
        var model = BindText(
            "survey s\n centerline\n  data normal from to length compass clino\n  a b 5 0 0\n endcenterline\nendsurvey\n");
        Assert.Null(model.Declination);
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
    }

    [Fact]
    public void GoToDefinition_resolves_qualified_station()
    {
        var model = BindFile(LoadCorpusCave());
        var nav = new SymbolNavigationService(model);
        var span = nav.GoToDefinition("cave.0");
        Assert.NotNull(span);
    }

    [Fact]
    public void Unresolved_equate_target_is_deferred_to_the_workspace_and_warns_standalone()
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

        // The per-file binder no longer warns directly (it can't see other files); it records the
        // unresolved reference for the workspace validator.
        Assert.DoesNotContain(model.Diagnostics,
            d => d.Code.Value == SemanticDiagnosticCodes.UnresolvedStation);
        Assert.Contains(model.UnresolvedEquateRefs, r => r.Raw == "missing.zz");
        Assert.DoesNotContain(model.UnresolvedEquateRefs, r => r.Raw == "a");   // 'a' resolves locally

        // With no workspace, the standalone fallback still surfaces the warning.
        Assert.Contains(model.UnresolvedEquateDiagnostics(),
            d => d.Code.Value == SemanticDiagnosticCodes.UnresolvedStation);
    }
}
