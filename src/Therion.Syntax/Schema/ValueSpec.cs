// Declarative command-schema data model (syntax-coverage effort, batch A1).
// Spec: .claude/therion-syntax/syntax-spec.md §2 — value types, charsets, ranges.
// Therion source of truth: thparse.cxx (charsets, special values), thparse.h (shared enums).

using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>
/// The kind of value a positional parameter or option argument accepts.
/// Mirrors syntax-spec.md §2.4; extended by the B/C batches as new kinds are distilled.
/// </summary>
public enum SchemaValueKind
{
    /// <summary>Anything — not validated.</summary>
    Free,
    /// <summary>A real number.</summary>
    Number,
    /// <summary>An integer.</summary>
    Int,
    /// <summary>A real number ≥ 0 (sizes, tape lengths).</summary>
    NonNegative,
    /// <summary>A bearing (0–360° / 0–400 grad).</summary>
    Angle,
    /// <summary>An inclination (−90..+90° / ±100 grad; also up/down/plumb specials).</summary>
    Clino,
    /// <summary>A percentage 0–200 (extend spec).</summary>
    Percent,
    /// <summary>A number optionally followed by a unit keyword.</summary>
    Length,
    /// <summary>A unit keyword (thtt_tfunits_length / thtt_tfunits_angle).</summary>
    Units,
    /// <summary>A name constrained by an identifier charset (spec §2.2).</summary>
    Identifier,
    /// <summary>A station reference, possibly survey-qualified (<c>name@survey.sub</c>).</summary>
    StationRef,
    /// <summary>A reference to another object (map, scrap, line-id …).</summary>
    ObjectRef,
    /// <summary>A quoted or bare string.</summary>
    String,
    /// <summary>A member of a closed keyword table (see <see cref="ValueSpec.Enum"/>).</summary>
    Enum,
    /// <summary>Strict Therion boolean: <c>on</c>|<c>off</c> only (thtt_bool).</summary>
    Bool,
    /// <summary><c>on</c>|<c>off</c>|<c>auto</c> (thtt_onoffauto).</summary>
    OnOffAuto,
    /// <summary>A number or a special value (<c>-</c> <c>.</c> NaN/Inf/up/down — thtt_special_val).</summary>
    SpecialOrNumber,
    /// <summary>A date/time spec (<c>YYYY.MM.DD[@hh:mm[:ss]]</c>, ranges, <c>-</c>) — thdate.cxx.</summary>
    Date,
    /// <summary>A person spec (<c>"Name Surname"</c> / <c>Name/Surname</c>) — thperson.cxx.</summary>
    Person,
    /// <summary>A coordinate value (numeric; meaning depends on the active cs).</summary>
    Coord,
    /// <summary>A colour (name / <c>[r g b]</c> / hex; models per thlayoutclr).</summary>
    Color,
}

/// <summary>
/// Identifier charsets, exactly as implemented in Therion's <c>thparse.cxx</c>
/// (verified — syntax-spec.md §2.2).
/// </summary>
public enum IdentifierCharset
{
    /// <summary><c>a-z A-Z 0-9 / _</c> plus <c>-</c> not as the first char; no <c>.</c> (th_is_keyword).</summary>
    Keyword,
    /// <summary>Keyword chars (incl. <c>-</c> anywhere) plus <c>' * + , . /</c> not as the first char (th_is_extkeyword).</summary>
    ExtKeyword,
    /// <summary><c>a-z A-Z 0-9</c> plus <c>_</c> not as the first char; no dash (th_is_attr_name).</summary>
    AttrName,
    /// <summary>Digits only (th_is_index).</summary>
    Index,
}

/// <summary>An inclusive numeric range constraint.</summary>
public sealed record NumericRange(double? Min, double? Max)
{
    public static readonly NumericRange NonNegative = new(0, null);
    public static readonly NumericRange Angle = new(0, 360);
    public static readonly NumericRange Clino = new(-90, 90);
    public static readonly NumericRange Percent = new(0, 200);

    public bool Contains(double v) => (Min is not { } lo || v >= lo) && (Max is not { } hi || v <= hi);
}

/// <summary>
/// Full description of one accepted value: its kind plus the applicable
/// enum table / charset / range refinement.
/// </summary>
public sealed record ValueSpec(
    SchemaValueKind Kind,
    ImmutableHashSet<string>? Enum = null,
    IdentifierCharset? Charset = null,
    NumericRange? Range = null,
    bool CaseSensitive = false)
{
    public static readonly ValueSpec Free = new(SchemaValueKind.Free);
    public static readonly ValueSpec Number = new(SchemaValueKind.Number);
    public static readonly ValueSpec String = new(SchemaValueKind.String);

    public static ValueSpec OfEnum(ImmutableHashSet<string> table, bool caseSensitive = false) =>
        new(SchemaValueKind.Enum, Enum: table, CaseSensitive: caseSensitive);

    public static ValueSpec Id(IdentifierCharset charset = IdentifierCharset.ExtKeyword) =>
        new(SchemaValueKind.Identifier, Charset: charset);
}
