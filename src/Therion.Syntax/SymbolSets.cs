// symbol-set / legend model. thbook v6.4.0 §"layout" (symbol-set / symbol-hide /
// symbol-show / symbol-assign). Therion source-of-truth: therion/src/thsymbolset.cxx + thsymbol*.
//
// `symbol-set <std>` chooses a built-in symbol standard; `symbol-hide/show/assign <kind> <symbol>`
// tweak individual symbols. This models those directives (parsed from a layout body) so the app
// can render a legend palette and reason about which symbols a layout uses.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>What a <c>symbol-*</c> layout directive does.</summary>
public enum SymbolDirectiveKind { Hide, Show, Assign, Colour }

/// <summary>
/// A single <c>symbol-hide/show/assign/colour &lt;kind&gt; &lt;symbol&gt; [args]</c> directive
/// from a layout body. <see cref="Kind"/> is point/line/area/group/special; <see cref="Symbol"/>
/// is the symbol type (or group name).
/// </summary>
public readonly record struct LayoutSymbolDirective(
    SourceSpan Span,
    SymbolDirectiveKind Action,
    string Kind,
    string Symbol,
    string Rest);

/// <summary>Known Therion built-in symbol standards (for <c>symbol-set</c>) + helpers.</summary>
public static class SymbolSets
{
    /// <summary>Built-in symbol-set standard codes (thbook + thsymbolset.cxx).</summary>
    public static readonly ImmutableHashSet<string> Standards =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "UIS", "SKBB", "AUT", "ASF", "BCRA", "NSS", "SBE", "CCNP", "NZSS", "GLZ");

    /// <summary>The symbol-object kinds a <c>symbol-hide/show</c> directive can target.</summary>
    public static readonly ImmutableHashSet<string> Kinds =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "point", "line", "area", "group", "special", "all");

    public static bool IsKnownStandard(string name) => Standards.Contains(name);
    public static IReadOnlyCollection<string> StandardNames => Standards;

    /// <summary>The directive kind for a layout option key, or null if it isn't a symbol directive.</summary>
    public static SymbolDirectiveKind? DirectiveFor(string key) => key.ToLowerInvariant() switch
    {
        "symbol-hide" => SymbolDirectiveKind.Hide,
        "symbol-show" => SymbolDirectiveKind.Show,
        "symbol-assign" => SymbolDirectiveKind.Assign,
        "symbol-colour" or "symbol-color" => SymbolDirectiveKind.Colour,
        _ => null,
    };
}
