// shared parser for a `layout … endlayout` body.
// Layout blocks appear both in .thconfig/.thc/.thl (ThconfigParser) and, in some projects, inside
// .th files that are `input`-ed (ThParser). This single body walker is reused by both so the typed
// model (options, copy/cs, symbol-set/directives) and the opaque `code … endcode` handling agree.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>The decoded pieces of a <c>layout</c> body.</summary>
public readonly record struct LayoutBodyParts(
    ImmutableArray<LayoutOption> Options,
    ImmutableArray<LayoutCodeBlock> CodeBlocks,
    string? CopyFrom,
    string? CoordinateSystem,
    string? SymbolSet,
    ImmutableArray<LayoutSymbolDirective> SymbolDirectives);

/// <summary>Walks the logical lines of a layout body, decoding options and skipping code blocks.</summary>
public static class LayoutBodyParser
{
    public static LayoutBodyParts Parse(
        ImmutableArray<LogicalLine> bodyLines,
        ImmutableArray<Diagnostic>.Builder? diagnostics = null,
        ParserOptions? options = null)
    {
        var optionsB = ImmutableArray.CreateBuilder<LayoutOption>();
        var codeB = ImmutableArray.CreateBuilder<LayoutCodeBlock>();
        var symbolB = ImmutableArray.CreateBuilder<LayoutSymbolDirective>();
        string? copyFrom = null, layoutCs = null, symbolSet = null;
        bool insideCode = false;

        foreach (var bl in bodyLines)
        {
            if (bl.IsEmpty) continue;
            var key = bl.Keyword;

            if (insideCode)
            {
                if (string.Equals(key, "endcode", StringComparison.OrdinalIgnoreCase)) insideCode = false;
                continue; // skip code-block bodies opaquely
            }
            if (string.Equals(key, "code", StringComparison.OrdinalIgnoreCase))
            {
                insideCode = true;
                codeB.Add(new LayoutCodeBlock(bl.Span, bl.Tokens.Length > 1 ? bl.Tokens[1].Text : string.Empty));
                continue;
            }
            if (string.Equals(key, "endcode", StringComparison.OrdinalIgnoreCase)) continue;

            var value = bl.Tokens.Length > 1 ? JoinTokenText(bl.Tokens, 1) : string.Empty;
            var option = new LayoutOption(bl.Span, key, value);
            optionsB.Add(option);

            if (diagnostics is not null && !LayoutKeywords.IsKnown(key))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.UnknownLayoutOption,
                    (options?.Mode == ParserMode.Strict) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    $"Unknown layout option '{key}'.", bl.Head.Span));
            else if (diagnostics is not null)
                Schema.LayoutValueRules.Check(option, diagnostics, options);   // C6, spec §8

            if (string.Equals(key, "copy", StringComparison.OrdinalIgnoreCase) && bl.Tokens.Length > 1)
                copyFrom = bl.Tokens[1].Text;
            else if (string.Equals(key, "cs", StringComparison.OrdinalIgnoreCase) && bl.Tokens.Length > 1)
                layoutCs = bl.Tokens[1].Text;
            else if (string.Equals(key, "symbol-set", StringComparison.OrdinalIgnoreCase) && bl.Tokens.Length > 1)
                symbolSet = bl.Tokens[1].Text;
            else if (SymbolSets.DirectiveFor(key) is { } action)
            {
                var dkind = bl.Tokens.Length > 1 ? bl.Tokens[1].Text : string.Empty;
                var sym = bl.Tokens.Length > 2 ? bl.Tokens[2].Text : string.Empty;
                var rest = bl.Tokens.Length > 3 ? JoinTokenText(bl.Tokens, 3) : string.Empty;
                symbolB.Add(new LayoutSymbolDirective(bl.Span, action, dkind, sym, rest));
            }
        }

        return new LayoutBodyParts(optionsB.ToImmutable(), codeB.ToImmutable(),
            copyFrom, layoutCs, symbolSet, symbolB.ToImmutable());
    }

    internal static string JoinTokenText(ImmutableArray<TherionToken> toks, int from)
    {
        var sb = new System.Text.StringBuilder();
        for (int k = from; k < toks.Length; k++)
        {
            if (k > from) sb.Append(' ');
            sb.Append(toks[k].Text);
        }
        return sb.ToString();
    }
}
