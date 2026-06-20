// §5.3 — semantic rule runner tests.

using System.Collections.Frozen;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SemanticRuleRunnerTests
{
    private sealed class CountingRule : ISemanticRule
    {
        public string Id => "TEST_COUNT";
        public int Calls;
        public ImmutableArray<Diagnostic> Run(SemanticContext ctx)
        {
            Calls++;
            return ImmutableArray.Create(
                Diagnostic.Create(Id, DiagnosticSeverity.Info, "ran", SourceSpan.None));
        }
    }

    private sealed class ThrowingRule : ISemanticRule
    {
        public string Id => "TEST_THROW";
        public ImmutableArray<Diagnostic> Run(SemanticContext ctx)
            => throw new System.InvalidOperationException("boom");
    }

    [Fact]
    public void Runs_each_rule_per_file_model()
    {
        var parse = new ThParser().Parse("/p/a.th", "survey s\nendsurvey\n");
        var ws = WorkspaceSemanticModel.Build(
            new Dictionary<string, ParseResult<TherionFile>> { ["/p/a.th"] = parse },
            System.Array.Empty<XviFile>());

        var rule = new CountingRule();
        var runner = new SemanticRuleRunner(new[] { (ISemanticRule)rule });
        var diags = runner.Run(ws);

        Assert.Equal(1, rule.Calls);
        Assert.Single(diags);
        Assert.Equal("TEST_COUNT", diags[0].Code);
    }

    [Fact]
    public void Throwing_rule_yields_TH_SEM_RULE_diagnostic_not_exception()
    {
        var parse = new ThParser().Parse("/p/a.th", "survey s\nendsurvey\n");
        var ws = WorkspaceSemanticModel.Build(
            new Dictionary<string, ParseResult<TherionFile>> { ["/p/a.th"] = parse },
            System.Array.Empty<XviFile>());

        var runner = new SemanticRuleRunner(new ISemanticRule[] { new ThrowingRule() });
        var diags = runner.Run(ws);
        Assert.Contains(diags, d => d.Code == SemanticRuleRunner.RuleFailureCode);
    }

    [Fact]
    public void Empty_rule_set_returns_empty()
    {
        var runner = new SemanticRuleRunner(System.Array.Empty<ISemanticRule>());
        Assert.Empty(runner.Run(WorkspaceSemanticModel.Empty));
    }
}
