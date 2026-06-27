// Implementation Plan �5 � cross-file model + indexes.
// Implements ISymbolIndex from Therion.Processing.Abstractions.

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Semantics;

/// <summary>
/// Immutable result of running <see cref="SemanticBinder"/> over a parse tree.
/// Exposes Roslyn-style snapshot indexes (�8: FrozenDictionary on .NET 8).
/// </summary>
public sealed class SemanticModel : ISymbolIndex
{
    public FrozenDictionary<QualifiedName, StationSymbol> Stations { get; }
    public FrozenDictionary<QualifiedName, SurveySymbol> Surveys { get; }
    public ImmutableArray<ShotSymbol> Shots { get; }
    public EquateGraph Equates { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    /// <summary>
    /// Cross-file XVI index, populated at the workspace level (Plan �5.1).
    /// Per-file models leave this at <see cref="XviIndex.Empty"/>.
    /// </summary>
    public XviIndex Xvi { get; init; } = XviIndex.Empty;

    /// <summary>Scrap declarations in this file, keyed by scrap id (Plan �7.3 / M6 #7).</summary>
    public FrozenDictionary<string, ScrapSymbol> Scraps { get; init; } =
        FrozenDictionary<string, ScrapSymbol>.Empty;

    /// <summary>Map declarations in this file, keyed by map id (<c>map &lt;id&gt; ... endmap</c>).</summary>
    public FrozenDictionary<string, MapSymbol> Maps { get; init; } =
        FrozenDictionary<string, MapSymbol>.Empty;

    /// <summary>
    /// The input coordinate system declared via <c>cs &lt;system&gt;</c> in this file, if any (LANG-03).
    /// </summary>
    public string? InputCoordinateSystem { get; init; }

    /// <summary><c>equate</c> relationships declared in this file (DATA-03).</summary>
    public ImmutableArray<EquateRecord> EquateRecords { get; init; } = ImmutableArray<EquateRecord>.Empty;

    public SemanticModel(
        FrozenDictionary<QualifiedName, StationSymbol> stations,
        FrozenDictionary<QualifiedName, SurveySymbol> surveys,
        ImmutableArray<ShotSymbol> shots,
        EquateGraph equates,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Stations = stations;
        Surveys = surveys;
        Shots = shots;
        Equates = equates;
        Diagnostics = diagnostics;
    }

    public static SemanticModel Empty { get; } = new(
        FrozenDictionary<QualifiedName, StationSymbol>.Empty,
        FrozenDictionary<QualifiedName, SurveySymbol>.Empty,
        ImmutableArray<ShotSymbol>.Empty,
        new EquateGraph(),
        ImmutableArray<Diagnostic>.Empty);

    // ---- ISymbolIndex ----------------------------------------------------

    public bool TryResolve(string qualifiedName, out SourceSpan declarationSpan)
    {
        var name = QualifiedName.Parse(qualifiedName);
        if (Stations.TryGetValue(name, out var s))
        {
            declarationSpan = s.DeclarationSpan;
            return true;
        }
        if (Surveys.TryGetValue(name, out var sv))
        {
            declarationSpan = sv.DeclarationSpan;
            return true;
        }
        if (Scraps.TryGetValue(qualifiedName, out var sc))
        {
            declarationSpan = sc.DeclarationSpan;
            return true;
        }
        if (Maps.TryGetValue(qualifiedName, out var m))
        {
            declarationSpan = m.DeclarationSpan;
            return true;
        }
        declarationSpan = SourceSpan.None;
        return false;
    }

    public ImmutableArray<SourceSpan> FindReferences(string qualifiedName)
    {
        var name = QualifiedName.Parse(qualifiedName);
        if (Stations.TryGetValue(name, out var s)) return s.References;
        return ImmutableArray<SourceSpan>.Empty;
    }
}
