// Declarative command-schema data model (syntax-coverage effort, batch A1).
// One CommandSchema per Therion command per context; static data filled in by the
// C batches from .claude/therion-syntax/syntax-spec.md (which distills Therion's
// thstok token tables + set() methods — see spec Appendix A for the inventory).
//
// The schema also backs future features (completion, code generation, refactoring),
// so it captures more than the validator consumes today.

using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>
/// The syntactic context a command may appear in. Mirrors Therion's block structure
/// (thbook ch02/ch03); a command is only legal in its declared contexts.
/// </summary>
public enum SchemaContext
{
    /// <summary>Top level of a .th file (also inside <c>survey</c> for nested surveys).</summary>
    ThTopLevel,
    /// <summary>Inside <c>survey … endsurvey</c>.</summary>
    Survey,
    /// <summary>Inside <c>centreline … endcentreline</c>.</summary>
    Centreline,
    /// <summary>Top level of a .th2 file.</summary>
    Th2TopLevel,
    /// <summary>Inside <c>scrap … endscrap</c>.</summary>
    Scrap,
    /// <summary>Inside <c>line … endline</c> (vertex/sub-option lines).</summary>
    LineBody,
    /// <summary>Inside <c>area … endarea</c> (border line references).</summary>
    AreaBody,
    /// <summary>Inside <c>map … endmap</c>.</summary>
    MapBody,
    /// <summary>Inside <c>layout … endlayout</c> (.thconfig or .th).</summary>
    Layout,
    /// <summary>Top level of a .thconfig/.thc/.thl file.</summary>
    Thconfig,
    /// <summary>Top level of an .xvi file (<c>set XVI…</c> statements).</summary>
    Xvi,
}

/// <summary>One positional parameter of a command.</summary>
public sealed record ParamSpec(
    string Name,
    ValueSpec Value,
    bool Required = true,
    bool Repeated = false);

/// <summary>One <c>-option</c> of a command.</summary>
public sealed record OptionSpec(
    string Name,
    ImmutableArray<ParamSpec> Values,
    ImmutableArray<string> Aliases,
    bool Repeatable = false,
    // Object types (point/line/area type names) this option is valid for; null = all.
    ImmutableHashSet<string>? ValidForTypes = null,
    bool Deprecated = false)
{
    public static OptionSpec Flag(string name, params string[] aliases) =>
        new(name, ImmutableArray<ParamSpec>.Empty, ImmutableArray.Create(aliases));

    public static OptionSpec Of(string name, ValueSpec value, params string[] aliases) =>
        new(name, ImmutableArray.Create(new ParamSpec(name, value)), ImmutableArray.Create(aliases));
}

/// <summary>
/// A reusable, named group of options shared by several commands — mirrors Therion's
/// class hierarchy (e.g. thdataobject's generic <c>-title/-author/-copyright/-cs/-attr</c>
/// apply to every data object). Referenced from <see cref="CommandSchema.Inherits"/>.
/// </summary>
public sealed record OptionSet(string Name, ImmutableArray<OptionSpec> Options);

/// <summary>
/// The full declarative description of one Therion command in given contexts.
/// Static instances are registered in <see cref="SchemaRegistry"/>; each entry cites
/// its Therion source table (spec Appendix A) in <see cref="SourceRef"/>.
/// </summary>
public sealed record CommandSchema(
    string Keyword,
    ImmutableArray<SchemaContext> Contexts,
    // Toggle group for SchemaValidationOptions.DisabledSections ("centreline", "point", "export"…).
    string Section,
    ImmutableArray<ParamSpec> Positional,
    ImmutableArray<OptionSpec> Options,
    ImmutableArray<OptionSet> Inherits,
    ImmutableArray<string> Aliases,
    // Terminator keyword for block commands ("endsurvey"…); null for line commands.
    string? Terminator = null,
    // Citation into the Therion source (e.g. "thpoint.h:67 thtt_point_opt") for traceability.
    string? SourceRef = null,
    string? Notes = null)
{
    public bool IsBlock => Terminator is not null;

    /// <summary>Minimum number of required positional arguments.</summary>
    public int MinArgs
    {
        get
        {
            int n = 0;
            foreach (var p in Positional)
                if (p.Required) n++;
            return n;
        }
    }

    /// <summary>Maximum positional arity, or null when a trailing parameter repeats.</summary>
    public int? MaxArgs
    {
        get
        {
            foreach (var p in Positional)
                if (p.Repeated) return null;
            return Positional.Length;
        }
    }
}
