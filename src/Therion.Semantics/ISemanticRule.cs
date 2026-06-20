// Implementation Plan §5.3 — semantic rules as plugins.

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>Context exposed to semantic rules.</summary>
public sealed record SemanticContext(SemanticModel Model);

/// <summary>A user- or built-in semantic check that produces diagnostics.</summary>
public interface ISemanticRule
{
    string Id { get; }
    ImmutableArray<Diagnostic> Run(SemanticContext ctx);
}
