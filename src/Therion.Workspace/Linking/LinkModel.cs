using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Linking;

/// <summary>Which side of a link a station comes from.</summary>
public enum LinkSide
{
    /// <summary>The survey file being linked in (its <c>input</c> is written by this link).</summary>
    Source,
    /// <summary>A file already in the workspace.</summary>
    Workspace,
}

/// <summary>One station of an equate, named the way its own file declares it.</summary>
public sealed record LinkMember(LinkSide Side, string FilePath, QualifiedName LocalStation);

/// <summary>The survey file being linked: parsed on its own, outside the workspace model.</summary>
public sealed record SourceSurvey(string FilePath, TherionFile Ast, SemanticModel Model);

/// <summary>Where the generated lines go: a file and the survey block in it (file-local path; empty = file root).</summary>
public sealed record LinkDestination(string FilePath, ImmutableArray<string> LocalScope);

/// <param name="CopySource">Copy the source file into <paramref name="TargetDirectory"/> (else it is input in place).</param>
/// <param name="TargetDirectory">Absolute folder the source (and sketches) are copied into.</param>
/// <param name="Sketches">Absolute paths of sketches beside the source to copy, keeping their path relative to it.</param>
public sealed record LinkCopy(bool CopySource, string? TargetDirectory, IReadOnlyList<string> Sketches)
{
    public static LinkCopy InPlace { get; } = new(false, null, []);
}

/// <param name="Comment">A ready-made comment line text (without <c>#</c>), placed above the inserted lines; null for none.</param>
public sealed record LinkOptions(bool InsertInput = true, bool InsertEquate = true, string? Comment = null);

public sealed record LinkRequest(
    SourceSurvey Source,
    IReadOnlyList<LinkMember> Members,
    LinkDestination Destination,
    LinkCopy Copy,
    LinkOptions Options);

/// <summary>What the planner needs from the workspace. All I/O goes through the delegates.</summary>
/// <param name="ReadText">A file's current text (the editor buffer when it is open), or null.</param>
/// <param name="Ast">A file's parse (the workspace's, or a fresh one); null when unavailable.</param>
public sealed record LinkContext(
    WorkspaceSemanticModel Workspace,
    FileScopes Scopes,
    Func<string, string?> ReadText,
    Func<string, TherionFile?> Ast,
    Func<string, bool> FileExists,
    Func<string, string, bool>? SameContent = null);

public enum LinkWarningCode
{
    /// <summary>A station's survey is not inside the destination scope; Therion cannot reference it from there. Blocks.</summary>
    ScopeNotAncestor,
    /// <summary>The destination survey block has no terminator. Blocks.</summary>
    UnterminatedBlock,
    /// <summary>The destination survey block was not found in the file. Blocks.</summary>
    ScopeNotFound,
    /// <summary>The file is already input somewhere in the workspace.</summary>
    InputExists,
    /// <summary>An equate with the same stations exists already.</summary>
    EquateExists,
    /// <summary>The copy target exists with different content. Blocks.</summary>
    TargetExists,
    /// <summary>A sketch target exists with different content; that sketch is not copied.</summary>
    SketchExists,
    /// <summary>The source inputs a file that is not being copied with it; its <c>input</c> will not resolve.</summary>
    SketchNotCopied,
    /// <summary>Fewer than two stations, or nothing to insert. Blocks.</summary>
    NothingToLink,
}

public sealed record LinkWarning(LinkWarningCode Code, string? Subject = null)
{
    public bool Blocks => Code is LinkWarningCode.ScopeNotAncestor or LinkWarningCode.UnterminatedBlock
        or LinkWarningCode.ScopeNotFound or LinkWarningCode.TargetExists or LinkWarningCode.NothingToLink;
}

/// <param name="Text">The generated lines as they would be inserted (for the preview and the clipboard).</param>
public sealed record LinkPlan(
    string? InputLine,
    string? EquateLine,
    string Text,
    EditBatch Batch,
    IReadOnlyList<LinkWarning> Warnings)
{
    public bool CanApply => !Warnings.Any(w => w.Blocks) && !Batch.IsEmpty;
}

public enum DestinationReason
{
    /// <summary>The survey block already holds equates at this level.</summary>
    BlockWithEquates,
    /// <summary>A file of equates input from inside the survey (no survey block of its own).</summary>
    EquateFile,
    /// <summary>The survey block, which has no equates yet.</summary>
    Block,
}

/// <param name="Scope">Effective survey path the lines would be written in.</param>
/// <param name="Rank">Lower is better.</param>
public sealed record DestinationCandidate(
    string FilePath,
    ImmutableArray<string> LocalScope,
    ImmutableArray<string> Scope,
    DestinationReason Reason,
    int Rank)
{
    public LinkDestination Destination => new(FilePath, LocalScope);
}
