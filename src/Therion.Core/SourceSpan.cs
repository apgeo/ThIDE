// Implementation Plan §4.1, §10. SourceSpan is the universal coordinate
// used by lexer, parser, semantic layer, diagnostics and UI navigation.

namespace Therion.Core;

/// <summary>
/// A 1-based (line, column) position inside a source file.
/// Column counts visible characters (not bytes); tabs count as 1.
/// </summary>
public readonly record struct SourceLocation(int Line, int Column)
{
    public static SourceLocation Start { get; } = new(1, 1);
    public override string ToString() => $"{Line}:{Column}";
}

/// <summary>
/// A contiguous range of characters inside a source file.
/// Spans are inclusive of <see cref="Start"/> and exclusive of <see cref="End"/>.
/// </summary>
/// <param name="FilePath">Absolute path of the source file (or virtual identifier).</param>
/// <param name="Start">Inclusive start location.</param>
/// <param name="End">Exclusive end location.</param>
/// <param name="StartOffset">Absolute character offset (0-based) of <see cref="Start"/>.</param>
/// <param name="Length">Number of characters in the span.</param>
public readonly record struct SourceSpan(
    string FilePath,
    SourceLocation Start,
    SourceLocation End,
    int StartOffset,
    int Length)
{
    /// <summary>An empty / synthetic span (no file).</summary>
    public static SourceSpan None { get; } =
        new(string.Empty, SourceLocation.Start, SourceLocation.Start, 0, 0);

    public bool IsEmpty => Length == 0;

    public override string ToString() =>
        IsEmpty ? "<none>" : $"{FilePath}:{Start.Line}:{Start.Column}";
}
