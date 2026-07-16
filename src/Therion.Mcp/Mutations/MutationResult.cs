using Therion.Mcp.Tools;

namespace Therion.Mcp.Mutations;

/// <param name="Action">"edit" or "create".</param>
/// <param name="Sha256">
/// Digest of the file <em>as it now stands</em> — before the write on a dry run, after it on an apply.
/// Pass it back as <c>expectedSha256</c> on the next call and that call is refused if anything else
/// touched the file in between.
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
/// decide whether the edit was good, without having to ask again. Empty when <paramref name="LintSkipped"/>.
/// </param>
/// <param name="NewErrors">Errors in the changed files that were not there before. Negative means fixed.</param>
/// <param name="LintSkipped">
/// The write landed but the project could not be reloaded, so there is no evidence about it. Treat
/// <paramref name="NewErrors"/> as unknown, not as zero.
/// </param>
/// <param name="Note">Why the lint was skipped, or any other caveat about this result.</param>
public sealed record MutationResult(
    bool DryRun,
    IReadOnlyList<FileChangeDto> Files,
    IReadOnlyList<DiagnosticDto> Diagnostics,
    int NewErrors,
    int NewWarnings,
    bool LintSkipped = false,
    string? Note = null);
