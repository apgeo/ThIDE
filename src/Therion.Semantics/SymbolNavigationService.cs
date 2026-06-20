// Implementation Plan §7.3 — Go to Definition / Find All References.
// UI-agnostic façade over a SemanticModel (the active workspace snapshot).

using System.Collections.Immutable;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace Therion.Semantics;

/// <summary>
/// Resolves clicks/keyboard shortcuts (F12, Shift+F12) in the editor to source
/// locations. Backed by <see cref="SemanticModel"/>.
/// </summary>
public sealed class SymbolNavigationService : ISymbolNavigationService
{
    private readonly SemanticModel _model;

    public SymbolNavigationService(SemanticModel model) => _model = model;

    public SourceSpan? GoToDefinition(string qualifiedName) =>
        _model.TryResolve(qualifiedName, out var span) ? span : null;

    public ImmutableArray<SourceSpan> FindReferences(string qualifiedName) =>
        _model.FindReferences(qualifiedName);
}
