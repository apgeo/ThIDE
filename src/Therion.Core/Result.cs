// Implementation Plan §4.2 (ParseResult), §6 (Workspace I/O).

using System.Collections.Immutable;

namespace Therion.Core;

/// <summary>
/// Generic success/failure result that carries diagnostics produced
/// while computing the value. Used at every layer (lex, parse, semantic).
/// </summary>
public readonly record struct Result<T>(
    T? Value,
    ImmutableArray<Diagnostic> Diagnostics,
    bool HasValue)
{
    public static Result<T> Success(T value) =>
        new(value, ImmutableArray<Diagnostic>.Empty, true);

    public static Result<T> Success(T value, ImmutableArray<Diagnostic> diagnostics) =>
        new(value, diagnostics, true);

    public static Result<T> Failure(ImmutableArray<Diagnostic> diagnostics) =>
        new(default, diagnostics, false);

    public static Result<T> Failure(Diagnostic diagnostic) =>
        new(default, ImmutableArray.Create(diagnostic), false);

    public bool HasErrors
    {
        get
        {
            foreach (var d in Diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Error)
                    return true;
            }
            return false;
        }
    }
}

/// <summary>
/// Abstraction over reading source file contents. Allows in-memory sources
/// for tests, the editor's live buffer, or the filesystem.
/// </summary>
public interface IFileSource
{
    /// <summary>Absolute path or virtual identifier of the file.</summary>
    string Path { get; }

    /// <summary>Read the file's text content using the given encoding (or auto-detect).</summary>
    ValueTask<string> ReadAllTextAsync(CancellationToken cancellationToken = default);

    /// <summary>Last-write timestamp used as part of the parser cache key.</summary>
    DateTime LastWriteUtc { get; }

    /// <summary>Length in bytes; used in the cache key.</summary>
    long Length { get; }
}
