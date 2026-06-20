// Implementation Plan §4.2 (Parser modes), Decision #2.

namespace Therion.Syntax;

/// <summary>
/// Parser strictness mode. Default is <see cref="Lenient"/>, configurable per
/// workspace via <c>WorkspaceOptions.ParserMode</c> or per parse call.
/// </summary>
public enum ParserMode
{
    /// <summary>Recover from errors, produce partial AST + warnings.</summary>
    Lenient,

    /// <summary>Stop at first unrecoverable error; do not synthesize nodes.</summary>
    Strict,
}

/// <summary>Per-parse-call options.</summary>
public sealed record ParserOptions(
    ParserMode Mode = ParserMode.Lenient,
    Therion.Core.TherionSyntaxVersion? Version = null,
    bool PreserveTrivia = true)
{
    public static ParserOptions Default { get; } = new();
}
