// LANG-13 — pluggable user rules: enable/disable in the runner + configurable naming-convention
// lints + JSON config loading.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Semantics;
using Therion.Semantics.BuiltinRules;
using Therion.Semantics.UserRules;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class UserRulesTests
{
    private static WorkspaceSemanticModel WorkspaceOf(string source)
    {
        var model = new SemanticBinder().Bind(new ThParser().Parse("/p/a.th", source).Value!);
        return new WorkspaceSemanticModel(
            new System.Collections.Generic.Dictionary<string, SemanticModel> { ["/p/a.th"] = model }
                .ToFrozenDictionary(),
            XviIndex.Empty,
            ImmutableArray<(string, string)>.Empty,
            ImmutableArray<Diagnostic>.Empty);
    }

    private const string Sample = """
        survey cave
          centreline
            fix lowercasefix 0 0 0
            data normal from to length compass clino
            1 2 5 0 0
          endcentreline
        endsurvey
        """;

    [Fact]
    public void Disabled_rule_is_skipped()
    {
        var ws = WorkspaceOf(Sample);
        var rules = new ISemanticRule[] { new OrphanFixedStationRule() };

        var on = new SemanticRuleRunner(rules).Run(ws);
        Assert.Contains(on, d => d.Code.Value == SemanticDiagnosticCodes.OrphanFixedStation);

        var off = new SemanticRuleRunner(rules, new SemanticRuleOptions
        {
            DisabledRuleIds = ImmutableHashSet.Create(SemanticDiagnosticCodes.OrphanFixedStation),
        }).Run(ws);
        Assert.DoesNotContain(off, d => d.Code.Value == SemanticDiagnosticCodes.OrphanFixedStation);
    }

    [Fact]
    public void Naming_convention_flags_non_matching_station_names()
    {
        var ws = WorkspaceOf(Sample);
        var rule = new NamingConventionRule(new[]
        {
            // stations must be all-uppercase/digits; "lowercasefix" violates it.
            new NamingConventionSpec("upper", NamingTarget.Station, "^[A-Z0-9]+$"),
        });
        var diags = new SemanticRuleRunner(new ISemanticRule[] { rule }).Run(ws);
        Assert.Contains(diags, d => d.Code.Value == SemanticDiagnosticCodes.NamingConvention
                                    && d.Message.Contains("lowercasefix"));
    }

    [Fact]
    public void Naming_convention_forbid_mode_flags_matches()
    {
        var ws = WorkspaceOf(Sample);
        var rule = new NamingConventionRule(new[]
        {
            new NamingConventionSpec("noTemp", NamingTarget.Survey, "temp", DiagnosticSeverity.Info, Forbid: true),
        });
        // survey "cave" doesn't match "temp" → no violation in forbid mode.
        var diags = new SemanticRuleRunner(new ISemanticRule[] { rule }).Run(ws);
        Assert.DoesNotContain(diags, d => d.Code.Value == SemanticDiagnosticCodes.NamingConvention);
    }

    [Fact]
    public void Invalid_regex_makes_the_spec_inert_not_crashing()
    {
        var ws = WorkspaceOf(Sample);
        var rule = new NamingConventionRule(new[]
        {
            new NamingConventionSpec("bad", NamingTarget.Station, "([unclosed"),
        });
        var diags = new SemanticRuleRunner(new ISemanticRule[] { rule }).Run(ws);
        Assert.DoesNotContain(diags, d => d.Code.Value == SemanticDiagnosticCodes.NamingConvention);
    }

    [Fact]
    public void Config_json_round_trips_into_options_and_specs()
    {
        var config = SemanticRuleConfig.Load("""
            {
              "disabledRules": [ "TH_SEM_004" ],
              "namingConventions": [
                { "id": "upper", "target": "station", "pattern": "^[A-Z0-9]+$", "severity": "warning" }
              ]
            }
            """);
        Assert.Contains("TH_SEM_004", config.ToRunnerOptions().DisabledRuleIds);
        var specs = config.ToNamingSpecs();
        Assert.Single(specs);
        Assert.Equal(NamingTarget.Station, specs[0].Target);
        Assert.Equal(DiagnosticSeverity.Warning, specs[0].Severity);
    }

    [Fact]
    public void Blank_or_invalid_config_is_empty()
    {
        Assert.Empty(SemanticRuleConfig.Load(null).NamingConventions);
        Assert.Empty(SemanticRuleConfig.Load("not json").NamingConventions);
    }
}
