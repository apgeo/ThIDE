// Implementation Plan §4.2: every parse always returns a result (partial AST + diagnostics).

using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax;

/// <summary>
/// Output of a parser invocation. Contains the (possibly partial) AST and any
/// diagnostics produced while parsing — even on errors.
/// </summary>
public sealed record ParseResult<T>(
    T? Value,
    ImmutableArray<Diagnostic> Diagnostics) where T : TherionNode
{
    public bool HasValue => Value is not null;

    public bool HasErrors
    {
        get
        {
            foreach (var d in Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error)
                    return true;
            return false;
        }
    }
}
