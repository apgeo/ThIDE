// EXT-05 — the LSP server's core: turn Therion source into LSP diagnostics. Pure and host-agnostic
// (no stdio), so it is unit-testable and reused by the JSON-RPC loop in Program.cs.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Lsp;

public static class DiagnosticProvider
{
    public readonly record struct Position(int Line, int Character);
    public readonly record struct Range(Position Start, Position End);

    /// <summary>An LSP diagnostic: 0-based range, LSP severity (1=Error…4=Hint), code + message.</summary>
    public sealed record LspDiagnostic(Range Range, int Severity, string Code, string Source, string Message);

    /// <summary>Parses <paramref name="text"/> (parser chosen by <paramref name="path"/>'s extension)
    /// and returns its diagnostics mapped to the LSP shape.</summary>
    public static IReadOnlyList<LspDiagnostic> Compute(string path, string text)
    {
        var result = new List<LspDiagnostic>();
        foreach (var d in Parse(path, text)) result.Add(ToLsp(d));
        return result;
    }

    private static ImmutableArray<Diagnostic> Parse(string path, string text)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".thconfig":
            case ".thc":
            case ".thl":
                return new ThconfigParser().Parse(path, text).Diagnostics;
            case ".th2":
                return new Th2Parser().Parse(path, text).Diagnostics;
            case ".xvi":
                return new XviParser().Parse(path, text).Diagnostics;
            default:
                // .th — parse, then bind so semantic diagnostics surface too.
                var r = new ThParser().Parse(path, text);
                var all = r.Diagnostics;
                if (r.Value is not null) all = all.AddRange(new SemanticBinder().Bind(r.Value).Diagnostics);
                return all;
        }
    }

    private static LspDiagnostic ToLsp(Diagnostic d)
    {
        var span = d.Span;
        int sl = Math.Max(0, span.Start.Line - 1);
        int sc = Math.Max(0, span.Start.Column - 1);
        int el = Math.Max(0, span.End.Line - 1);
        int ec = Math.Max(0, span.End.Column - 1);
        if (el < sl) el = sl;
        if (el == sl && ec <= sc) ec = sc + 1;   // ensure a non-empty range so editors show the squiggle

        int severity = d.Severity switch
        {
            DiagnosticSeverity.Error => 1,
            DiagnosticSeverity.Warning => 2,
            DiagnosticSeverity.Info => 3,
            _ => 4, // Hint
        };
        return new LspDiagnostic(
            new Range(new Position(sl, sc), new Position(el, ec)),
            severity, d.Code.Value, "therion", d.Message);
    }
}
