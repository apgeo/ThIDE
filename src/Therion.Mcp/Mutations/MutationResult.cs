using Therion.Mcp.Tools;

namespace Therion.Mcp.Mutations;

/// <param name="Action">"edit" or "create".</param>
/// <param name="Sha256">
/// Digest of the file as the plan saw it. Pass it back as <c>expectedSha256</c> when applying, and
/// the apply refuses if anything touched the file in between.
/// </param>
/// <param name="Preview">Unified-ish before/after lines for each edit, capped.</param>
public sealed record FileChangeDto(
    string Path,
    string Action,
    int Edits,
    string? Sha256,
    IReadOnlyList<PreviewLine> Preview,
    bool PreviewTruncated);

/// <param name="Line">1-based line number in the file as it stands now.</param>
public sealed record PreviewLine(int Line, string Before, string After);

/// <param name="DryRun">True when nothing was written — the default.</param>
/// <param name="Diagnostics">
/// The state of the changed files <em>after</em> the write. This is the evidence a caller needs to
/// decide whether the edit was good, without having to ask again.
/// </param>
/// <param name="NewErrors">Errors in the changed files that were not there before. Negative means fixed.</param>
public sealed record MutationResult(
    bool DryRun,
    IReadOnlyList<FileChangeDto> Files,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int NewErrors,
    int NewWarnings);
