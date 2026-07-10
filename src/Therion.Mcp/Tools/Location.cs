using Therion.Core;

namespace Therion.Mcp.Tools;

/// <summary>
/// A place in the project. Line and column are 1-based, as the editor and the diagnostics report
/// them; <see cref="File"/> is workspace-relative with forward slashes.
/// </summary>
public sealed record Location(string File, int Line, int Column, int EndLine, int EndColumn)
{
    /// <summary>
    /// The wire form of <paramref name="span"/>, or <c>null</c> for a synthetic span with no file
    /// (project-wide diagnostics and symbols legitimately have none).
    /// </summary>
    public static Location? From(SourceSpan span, string root) =>
        string.IsNullOrEmpty(span.FilePath)
            ? null
            : new Location(
                WorkspacePaths.ToRelative(root, span.FilePath),
                span.Start.Line, span.Start.Column,
                span.End.Line, span.End.Column);
}
