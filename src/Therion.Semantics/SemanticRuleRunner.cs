// Implementation Plan �5.3 � semantic rule runner.
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

/// <summary>Runtime options for the rule runner: which rules are switched off (LANG-13).</summary>
public sealed class SemanticRuleOptions
{
    public ImmutableHashSet<string> DisabledRuleIds { get; init; } =
        ImmutableHashSet<string>.Empty;

    public static SemanticRuleOptions Default { get; } = new();
}

public sealed class SemanticRuleRunner : ISemanticRuleRunner
{
    public const string RuleFailureCode = "TH_SEM_RULE";

    private readonly IReadOnlyCollection<ISemanticRule> _rules;

    public SemanticRuleRunner(IEnumerable<ISemanticRule> rules)
        : this(rules, null) { }

    /// <summary>
    /// Constructs a runner that skips any rule whose <see cref="ISemanticRule.Id"/> is listed in
    /// <paramref name="options"/>.<see cref="SemanticRuleOptions.DisabledRuleIds"/> (LANG-13).
    /// </summary>
    public SemanticRuleRunner(IEnumerable<ISemanticRule> rules, SemanticRuleOptions? options)
    {
        var disabled = (options ?? SemanticRuleOptions.Default).DisabledRuleIds;
        _rules = rules.Where(r => !disabled.Contains(r.Id)).ToArray();
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
