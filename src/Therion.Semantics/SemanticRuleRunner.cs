// Implementation Plan §5.3 — semantic rule runner.
// Loads & invokes ISemanticRule plugins against a WorkspaceSemanticModel
// and aggregates their diagnostics. Exceptions in a rule are turned into
// TH_SEM_RULE diagnostics so one broken plugin can't poison the build.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Semantics;

public interface ISemanticRuleRunner
{
    ImmutableArray<Diagnostic> Run(WorkspaceSemanticModel workspace);
}

public sealed class SemanticRuleRunner : ISemanticRuleRunner
{
    public const string RuleFailureCode = "TH_SEM_RULE";

    private readonly IReadOnlyCollection<ISemanticRule> _rules;

    public SemanticRuleRunner(IEnumerable<ISemanticRule> rules)
    {
        _rules = rules.ToArray();
    }

    public ImmutableArray<Diagnostic> Run(WorkspaceSemanticModel workspace)
    {
        if (_rules.Count == 0) return ImmutableArray<Diagnostic>.Empty;

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var (path, model) in workspace.PerFile)
        {
            _ = path;
            var ctx = new SemanticContext(model);
            foreach (var rule in _rules)
            {
                try
                {
                    var produced = rule.Run(ctx);
                    if (!produced.IsDefault) diags.AddRange(produced);
                }
                catch (System.Exception ex)
                {
                    diags.Add(Diagnostic.Create(
                        RuleFailureCode,
                        DiagnosticSeverity.Warning,
                        $"Semantic rule '{rule.Id}' threw: {ex.Message}",
                        SourceSpan.None));
                }
            }
        }
        return diags.ToImmutable();
    }
}
