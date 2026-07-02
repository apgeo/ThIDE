// Configuration toggles for the schema-validation pass (syntax-coverage effort, A1).
// User requirement: validation "sections" must be independently enable/disable-able because
// not every workspace needs every check. Two orthogonal dials:
//   - Categories : which KINDS of checks run (arity, enums, ranges, …)
//   - DisabledSections : which command GROUPS are checked (CommandSchema.Section tags)
// Perf notes & budget: .claude/therion-syntax/PERF.md.

using System;
using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>Kinds of schema checks; each can be toggled independently.</summary>
[Flags]
public enum ValidationCategories
{
    None            = 0,
    /// <summary>Command known in its context.</summary>
    Commands        = 1 << 0,
    /// <summary>Min/max positional argument counts.</summary>
    Arity           = 1 << 1,
    /// <summary>Positional/option value kinds (number, date, …).</summary>
    ValueTypes      = 1 << 2,
    /// <summary>Closed keyword tables (flags, mark types, formats, …).</summary>
    Enums           = 1 << 3,
    /// <summary>Numeric range constraints (angles, percents, …).</summary>
    Ranges          = 1 << 4,
    /// <summary>Option names known + option value arity.</summary>
    Options         = 1 << 5,
    /// <summary>Identifier charset conformance (spec §2.2).</summary>
    Identifiers     = 1 << 6,
    /// <summary>Exact-case keyword/enum spelling (Therion matches case-sensitively).</summary>
    CaseSensitivity = 1 << 7,
    /// <summary>Special-value spellings (<c>-</c> <c>.</c> NaN/Inf/up/down).</summary>
    SpecialValues   = 1 << 8,
    /// <summary>Per-object-type option validity (e.g. -text only on label-like points).</summary>
    TypeOptionMatrix = 1 << 9,

    All = Commands | Arity | ValueTypes | Enums | Ranges | Options
        | Identifiers | CaseSensitivity | SpecialValues | TypeOptionMatrix,
}

/// <summary>
/// Options for <see cref="SchemaValidator"/>. Reached via <c>ParserOptions.Validation</c>;
/// <see cref="Default"/> = everything on (the pass is a no-op until schema data lands).
/// </summary>
public sealed record SchemaValidationOptions(
    bool Enabled = true,
    ValidationCategories Categories = ValidationCategories.All,
    ImmutableHashSet<string>? DisabledSections = null)
{
    public static SchemaValidationOptions Default { get; } = new();

    /// <summary>Fully disabled — restores pre-schema behaviour exactly (PERF.md §5.4).</summary>
    public static SchemaValidationOptions Off { get; } = new(Enabled: false);

    public bool IsCategoryEnabled(ValidationCategories category) =>
        (Categories & category) != 0;

    public bool IsSectionEnabled(string section) =>
        DisabledSections is null || !DisabledSections.Contains(section);
}
