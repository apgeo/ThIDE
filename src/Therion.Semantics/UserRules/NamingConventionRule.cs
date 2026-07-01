// user-authored semantic rules. A configurable naming-convention lint: the user supplies
// regex patterns that station / survey / scrap / map names must (or must not) match, and the rule
// emits diagnostics for violations. Specs are loaded from settings (see SemanticRuleConfig), so
// users can add lints without writing code.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Therion.Core;

namespace Therion.Semantics.UserRules;

/// <summary>Which entity kind a naming-convention spec targets.</summary>
public enum NamingTarget { Station, Survey, Scrap, Map }

/// <summary>
/// A single user naming-convention: names of <see cref="Target"/> must match (or, when
/// <see cref="Forbid"/> is true, must <em>not</em> match) <see cref="Pattern"/>.
/// </summary>
public sealed record NamingConventionSpec(
    string Id,
    NamingTarget Target,
    string Pattern,
    DiagnosticSeverity Severity = DiagnosticSeverity.Warning,
    bool Forbid = false,
    string? Message = null);

/// <summary>A configurable rule that enforces user-supplied naming conventions.</summary>
public sealed class NamingConventionRule : ISemanticRule
{
    private readonly ImmutableArray<(NamingConventionSpec Spec, Regex? Rx)> _specs;

    public string Id => SemanticDiagnosticCodes.NamingConvention;

    public NamingConventionRule(IEnumerable<NamingConventionSpec> specs)
    {
        var b = ImmutableArray.CreateBuilder<(NamingConventionSpec, Regex?)>();
        foreach (var s in specs)
        {
            Regex? rx = null;
            try { rx = new Regex(s.Pattern, RegexOptions.CultureInvariant); }
            catch { /* invalid pattern → spec is inert rather than throwing */ }
            b.Add((s, rx));
        }
        _specs = b.ToImmutable();
    }

    public ImmutableArray<Diagnostic> Run(SemanticContext ctx)
    {
        if (_specs.IsDefaultOrEmpty) return ImmutableArray<Diagnostic>.Empty;
        var model = ctx.Model;
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var (spec, rx) in _specs)
        {
            if (rx is null) continue;
            switch (spec.Target)
            {
                case NamingTarget.Station:
                    foreach (var s in model.Stations.Values)
                        Check(spec, rx, LastComponent(s.Name), s.DeclarationSpan, diags);
                    break;
                case NamingTarget.Survey:
                    foreach (var s in model.Surveys.Values)
                        Check(spec, rx, LastComponent(s.Name), s.DeclarationSpan, diags);
                    break;
                case NamingTarget.Scrap:
                    foreach (var s in model.Scraps.Values)
                        Check(spec, rx, s.Id, s.DeclarationSpan, diags);
                    break;
                case NamingTarget.Map:
                    foreach (var m in model.Maps.Values)
                        Check(spec, rx, m.Id, m.DeclarationSpan, diags);
                    break;
            }
        }
        return diags.ToImmutable();
    }

    private static string LastComponent(QualifiedName qn) =>
        qn.HasParent ? qn.ToString().Split('.')[^1] : qn.ToString();

    private static void Check(
        NamingConventionSpec spec, Regex rx, string name, SourceSpan span,
        ImmutableArray<Diagnostic>.Builder diags)
    {
        if (string.IsNullOrEmpty(name)) return;
        bool matches = rx.IsMatch(name);
        bool violates = spec.Forbid ? matches : !matches;
        if (!violates) return;
        var msg = spec.Message ?? (spec.Forbid
            ? $"{spec.Target} name '{name}' matches the forbidden pattern '{spec.Pattern}'."
            : $"{spec.Target} name '{name}' does not match the required pattern '{spec.Pattern}'.");
        diags.Add(Diagnostic.Create(SemanticDiagnosticCodes.NamingConvention, spec.Severity, msg, span));
    }
}
