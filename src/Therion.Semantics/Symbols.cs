// Implementation Plan �5.1 � symbol records produced by the binder.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Kind of declaration that introduced a station into the model.</summary>
public enum StationDeclarationKind
{
    /// <summary>Mentioned as the <c>from</c>/<c>to</c> of a data row.</summary>
    Shot,
    /// <summary>Introduced by a <c>fix</c> command.</summary>
    Fix,
    /// <summary>Referenced by an <c>equate</c> command.</summary>
    Equate,
}

/// <summary>A station symbol: a unique fully-qualified station name.</summary>
public sealed record StationSymbol(
    QualifiedName Name,
    SourceSpan DeclarationSpan,
    StationDeclarationKind Kind,
    ImmutableArray<SourceSpan> References);

/// <summary>A survey symbol (hierarchical scope).</summary>
public sealed record SurveySymbol(
    QualifiedName Name,
    SourceSpan DeclarationSpan,
    QualifiedName? Parent,
    ImmutableArray<QualifiedName> Children);

/// <summary>
/// Centreline shot flags (Therion <c>flags surface|duplicate|splay|approximate</c>).
/// Stateful in source: a <c>flags</c> command toggles these for subsequent shots until
/// changed by another <c>flags [not] ...</c>.
/// </summary>
[System.Flags]
public enum ShotFlags
{
    None        = 0,
    Surface     = 1 << 0,
    Duplicate   = 1 << 1,
    Splay       = 1 << 2,
    Approximate = 1 << 3,
}

/// <summary>A "shot" leg between two stations (from one data row).</summary>
public sealed record ShotSymbol(
    QualifiedName From,
    QualifiedName To,
    double? Length,
    double? Compass,
    double? Clino,
    SourceSpan Span)
{
    /// <summary>Effective flags in force at this shot (folded from preceding <c>flags</c> commands).</summary>
    public ShotFlags Flags { get; init; }

    /// <summary>
    /// Comment attached to the shot: the inline <c># ...</c> after the values and/or the
    /// <c># ...</c> line directly above it, joined with " | ". Null when none.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// AST node this shot was lowered from � required to round-trip inline edits
    /// through <see cref="IModelEditService.ReplaceNode"/> (Plan �7.3 / M6 #8).
    /// Null on Empty/synthetic shots.
    /// </summary>
    public DataRow? SourceRow { get; init; }

    /// <summary>
    /// The <c>data</c> declaration that defines column positions for <see cref="SourceRow"/>.
    /// Used to map edited fields (length/compass/clino) back to their slot.
    /// </summary>
    public DataCommand? FieldDefinition { get; init; }
}

/// <summary>A <c>scrap</c> symbol (.th2): id + declaration span (Plan �7.3 / M6 #7).</summary>
public sealed record ScrapSymbol(
    string Id,
    SourceSpan DeclarationSpan);
