// M6 #4 / #7 / #8 — focused tests for the latest iteration.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class M6IterationCTests
{
    // ---- #7 expanded go-to-def ------------------------------------------

    [Fact]
    public void Binder_collects_scrap_symbols_from_th2()
    {
        const string src = """
            scrap s1
              point 100 200 station
            endscrap
            """;
        var parse = new Th2Parser().Parse("(test.th2)", src);
        Assert.NotNull(parse.Value);
        var scrapAst = parse.Value!.Children.OfType<ScrapBlock>().FirstOrDefault();
        Assert.NotNull(scrapAst);
        Assert.Equal("s1", scrapAst!.Id);
        var model = new SemanticBinder().Bind(parse.Value!);
        Assert.True(model.Scraps.ContainsKey("s1"),
            $"Scraps were: [{string.Join(",", model.Scraps.Keys)}], child types: [{string.Join(",", parse.Value!.Children.Select(c => c.GetType().Name))}]");
        Assert.True(model.TryResolve("s1", out var span));
        Assert.Equal(scrapAst.Span, span);
    }

    [Fact]
    public void WorkspaceNavigation_resolves_input_file_by_basename()
    {
        var wsm = WorkspaceSemanticModel.Empty;
        // Manufacture a workspace model with a single per-file entry.
        var perFile = new System.Collections.Generic.Dictionary<string, SemanticModel>(System.StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\proj\cave.th"] = SemanticModel.Empty,
        }.ToFrozenDictionary(System.StringComparer.OrdinalIgnoreCase);
        var ws = new WorkspaceSemanticModel(perFile, XviIndex.Empty,
            ImmutableArray<(string, string)>.Empty, ImmutableArray<Diagnostic>.Empty);
        var nav = new WorkspaceSymbolNavigationService(ws);
        var span = nav.GoToDefinition("cave.th");
        Assert.NotNull(span);
        Assert.Equal(@"C:\proj\cave.th", span!.Value.FilePath);
    }

    // ---- #8 inline editing ----------------------------------------------

    [Fact]
    public void ModelEdit_round_trips_shot_value_change()
    {
        const string src = "survey demo\n  centreline\n    data normal from to length compass clino\n      0 1 12.5 0 -5\n  endcentreline\nendsurvey\n";
        var parse = new ThParser().Parse("(test.th)", src);
        var ast = parse.Value!;
        var model = new SemanticBinder().Bind(ast);
        var shot = model.Shots.First();
        Assert.NotNull(shot.SourceRow);
        Assert.NotNull(shot.FieldDefinition);

        var fields = shot.FieldDefinition!;
        int lenIdx = -1;
        for (int i = 0; i < fields.Fields.Length; i++)
            if (fields.Fields[i] == "length") { lenIdx = i; break; }
        Assert.True(lenIdx >= 0);

        var row = shot.SourceRow!;
        var newValues = row.Values.SetItem(lenIdx, "99.5");
        var replacement = row with { Values = newValues };

        var svc = new ModelEditService();
        var result = svc.ReplaceNode(ast, row, replacement);
        Assert.True(result.Success);
        Assert.NotNull(result.UpdatedText);
        Assert.Contains("99.5", result.UpdatedText);
    }
}

// ---- #4 external-tool overrides -------------------------------------

public class ExternalToolOverridesTests
{
    private sealed class StubOverrides : IExternalToolPathOverrides
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _map = new();
        public System.Collections.Generic.IReadOnlyDictionary<string, string> Overrides => _map;
        public void Set(string toolId, string? path)
        {
            if (string.IsNullOrEmpty(path)) _map.Remove(toolId);
            else _map[toolId] = path!;
            OverridesChanged?.Invoke(this, System.EventArgs.Empty);
        }
        public event System.EventHandler? OverridesChanged;
    }

    [Fact]
    public async System.Threading.Tasks.Task Locator_returns_override_when_file_exists()
    {
        var temp = Path.GetTempFileName();
        try
        {
            var overrides = new StubOverrides();
            overrides.Set(ExternalToolLocator.Therion, temp);
            var loc = new ExternalToolLocator(overrides);
            var info = await loc.FindAsync(ExternalToolLocator.Therion);
            Assert.NotNull(info);
            Assert.Equal(temp, info!.Path);
            Assert.Equal("override", info.Source);
        }
        finally { File.Delete(temp); }
    }

    [Fact]
    public async System.Threading.Tasks.Task Locator_ignores_override_when_file_missing()
    {
        var overrides = new StubOverrides();
        overrides.Set(ExternalToolLocator.Therion, @"C:\does\not\exist\therion.exe");
        var loc = new ExternalToolLocator(overrides);
        var info = await loc.FindAsync(ExternalToolLocator.Therion);
        // Either null (not installed) or non-override source (well-known/PATH).
        if (info is not null)
            Assert.NotEqual("override", info.Source);
    }
}
