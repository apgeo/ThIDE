// user rule configuration. A small JSON document (stored in app settings) lets users
// turn built-in rules off and declare naming-convention lints without code. Lives in the semantics
// library (no UI dependency) so the CLI and any host can apply the same config.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using Therion.Core;

namespace Therion.Semantics.UserRules;

/// <summary>One naming-convention entry as it appears in the JSON config.</summary>
public sealed class NamingConventionEntry
{
    public string? Id { get; set; }
    public string? Target { get; set; }   // Station | Survey | Scrap | Map
    public string? Pattern { get; set; }
    public string? Severity { get; set; } // Error | Warning | Info | Hint
    public bool Forbid { get; set; }
    public string? Message { get; set; }
}

/// <summary>The deserialized user-rule config (disabled rules + naming conventions).</summary>
public sealed class SemanticRuleConfig
{
    public List<string> DisabledRules { get; set; } = new();
    public List<NamingConventionEntry> NamingConventions { get; set; } = new();

    public static SemanticRuleConfig Empty { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses a config document; returns <see cref="Empty"/> on null/blank/invalid JSON.</summary>
    public static SemanticRuleConfig Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try { return JsonSerializer.Deserialize<SemanticRuleConfig>(json, JsonOpts) ?? Empty; }
        catch (JsonException) { return Empty; }
    }

    /// <summary>The disabled-rule set as runner options.</summary>
    public SemanticRuleOptions ToRunnerOptions() => new()
    {
        DisabledRuleIds = DisabledRules.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Compiles the naming entries into specs (skipping malformed ones).</summary>
    public ImmutableArray<NamingConventionSpec> ToNamingSpecs()
    {
        var b = ImmutableArray.CreateBuilder<NamingConventionSpec>();
        foreach (var e in NamingConventions)
        {
            if (string.IsNullOrWhiteSpace(e.Pattern)) continue;
            if (!Enum.TryParse<NamingTarget>(e.Target, ignoreCase: true, out var target)) continue;
            var severity = Enum.TryParse<DiagnosticSeverity>(e.Severity, ignoreCase: true, out var sev)
                ? sev : DiagnosticSeverity.Warning;
            b.Add(new NamingConventionSpec(
                e.Id ?? "naming", target, e.Pattern!, severity, e.Forbid, e.Message));
        }
        return b.ToImmutable();
    }

    /// <summary>True if there is at least one usable naming convention.</summary>
    public bool HasNamingRules => NamingConventions.Count > 0;
}
