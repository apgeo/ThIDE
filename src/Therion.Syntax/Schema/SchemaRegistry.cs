// Declarative command-schema registry (syntax-coverage effort, batch A1).
// The C batches (C1..C7) populate the static tables from syntax-spec.md; until then the
// registry is empty and SchemaValidator is a guaranteed no-op (no behaviour change).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>
/// Lookup of <see cref="CommandSchema"/> entries by (context, keyword). One shared,
/// immutable default instance holds the built-in Therion 6.4 grammar; tests may build
/// custom instances.
/// </summary>
public sealed class SchemaRegistry
{
    private readonly ImmutableDictionary<(SchemaContext Context, string Keyword), CommandSchema> _byContextKeyword;

    /// <summary>Shared option sets referenced from <see cref="CommandSchema.Inherits"/>.</summary>
    public ImmutableArray<OptionSet> OptionSets { get; }

    public SchemaRegistry(IEnumerable<CommandSchema> schemas, IEnumerable<OptionSet>? optionSets = null)
    {
        var builder = ImmutableDictionary.CreateBuilder<(SchemaContext, string), CommandSchema>(
            ContextKeywordComparer.Instance);
        foreach (var schema in schemas)
            foreach (var ctx in schema.Contexts)
            {
                builder[(ctx, schema.Keyword)] = schema;
                foreach (var alias in schema.Aliases)
                    builder[(ctx, alias)] = schema;
            }
        _byContextKeyword = builder.ToImmutable();
        OptionSets = optionSets?.ToImmutableArray() ?? ImmutableArray<OptionSet>.Empty;
    }

    /// <summary>
    /// The built-in Therion 6.4 grammar. Populated incrementally by the C batches
    /// (see .claude/therion-syntax/PLAN.md §6); empty in A1.
    /// </summary>
    public static SchemaRegistry Default { get; } = new(Array.Empty<CommandSchema>());

    public int Count => _byContextKeyword.Count;

    /// <summary>
    /// Finds the schema for <paramref name="keyword"/> in <paramref name="context"/>.
    /// Keyword matching is case-insensitive here; exact-case checking is the validator's
    /// CaseSensitivity category (Therion itself matches case-sensitively — spec §2.1).
    /// </summary>
    public bool TryGet(SchemaContext context, string keyword, out CommandSchema schema) =>
        _byContextKeyword.TryGetValue((context, keyword), out schema!);

    private sealed class ContextKeywordComparer : IEqualityComparer<(SchemaContext Context, string Keyword)>
    {
        public static readonly ContextKeywordComparer Instance = new();

        public bool Equals((SchemaContext Context, string Keyword) x, (SchemaContext Context, string Keyword) y) =>
            x.Context == y.Context && string.Equals(x.Keyword, y.Keyword, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((SchemaContext Context, string Keyword) v) =>
            HashCode.Combine(v.Context, StringComparer.OrdinalIgnoreCase.GetHashCode(v.Keyword));
    }
}
