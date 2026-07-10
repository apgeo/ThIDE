using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Mcp.Tools;

/// <param name="Symbol">The symbol that was resolved, fully qualified, e.g. <c>cave.upper.1</c>.</param>
/// <param name="Kind">station or survey.</param>
public sealed record RenameResult(
    string Symbol,
    string Kind,
    string OldName,
    string NewName,
    MutationResult Mutation);

/// <summary>Ring R2 — renaming a station or a survey everywhere it is written.</summary>
[McpServerToolType]
public sealed class RenameTools(WorkspaceHost host, MutationEngine mutations)
{
    /// <summary>
    /// A rename rewrites one name token. These characters make a name into a path or a reference, so
    /// accepting them would silently produce something that is not the symbol the caller asked for.
    /// </summary>
    private static readonly char[] StructuralCharacters = ['.', '@', ':', '/'];

    // destructiveHint: it rewrites existing files. idempotentHint: false — applied twice, the second
    // call cannot find the old name. Hosts use both to decide when to ask the user.
    [McpServerTool(Name = "rename_symbol", Title = "Rename symbol",
        ReadOnly = false, Destructive = true, Idempotent = false)]
    [Description("Renames a station or a survey everywhere it is written — declaration, shots, "
               + "equates, and @-qualified references in other files. Scope-correct: station '1' in "
               + "one survey is not station '1' in another. Returns a plan by default; pass "
               + "dryRun:false to write. Refuses if the new name already exists in the same scope, "
               + "and refuses outright rather than renaming some occurrences and not others. "
               + "Maps and scraps cannot be renamed this way — they have no occurrence index.")]
    public async Task<ToolResult<RenameResult>> RenameSymbol(
        [Description("The symbol to rename — qualified ('cave.upper.1'), @-qualified ('1@upper'), or bare.")]
        string name,
        [Description("The new name. One token: no dots, '@', ':' or '/'.")]
        string newName,
        [Description("Disambiguates a bare name: any, station, or survey. Defaults to any.")]
        string kind = "any",
        [Description("Preview the edits without writing. Defaults to true — pass false to apply.")]
        bool dryRun = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Failure(ToolErrorCodes.InvalidArgument, "No name given.");

        if (ValidateNewName(newName) is { } invalid)
            return Failure(ToolErrorCodes.InvalidArgument, invalid);

        if (!TryParseKind(kind, out var referenceKind))
            return Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown kind '{kind}'. rename_symbol understands any, station, or survey.");

        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<RenameResult>.Failure(error);

        var model = snapshot!.Model;

        var declaration = new WorkspaceSymbolNavigationService(model).GoToDefinition(name, referenceKind);
        if (declaration is not { IsEmpty: false } span)
            return Failure(ToolErrorCodes.SymbolNotFound,
                $"Nothing named '{name}' is declared in this project.");

        if (SymbolReferences.ResolveDeclaration(model, referenceKind, span) is not { } resolved)
            return Failure(ToolErrorCodes.InvalidArgument,
                $"'{name}' is not a station or a survey. Maps and scraps have no occurrence index, "
                + "so they cannot be renamed safely.");

        var (symbol, currentName) = resolved;

        if (string.Equals(currentName, newName, StringComparison.Ordinal))
            return Failure(ToolErrorCodes.InvalidArgument, $"'{name}' is already called '{newName}'.");

        if (Collides(model, symbol, newName) is { } collision)
            return Failure(ToolErrorCodes.NameCollision, collision);

        var plan = BuildPlan(model, symbol, currentName, newName);
        if (plan.IsEmpty)
            return Failure(ToolErrorCodes.SymbolNotFound, $"'{name}' has no occurrences to rename.");

        var applied = await mutations.ApplyAsync(plan, dryRun, expectedSha256: null, ct);
        if (applied.Error is { } failure) return ToolResult<RenameResult>.Failure(failure);

        return ToolResult<RenameResult>.Success(new RenameResult(
            Symbol: symbol.Name.ToString(),
            Kind: symbol.Kind.ToString().ToLowerInvariant(),
            OldName: currentName,
            NewName: newName,
            Mutation: applied.Data!));
    }

    /// <summary>
    /// Every occurrence of the symbol becomes an edit whose expected text is the current name. The
    /// engine then verifies each span against the file on disk, so a workspace that has drifted since
    /// it was loaded fails the whole rename rather than doing part of it.
    /// </summary>
    private static MutationPlan BuildPlan(
        WorkspaceSemanticModel model, SymbolId symbol, string currentName, string newName)
    {
        var changes = model.FindOccurrences(symbol)
            .Select(o => o.Span)
            .Where(s => !string.IsNullOrEmpty(s.FilePath))
            .Distinct()
            .GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => (FileChange)new EditFile(
                group.Key,
                group.Select(s => new TextEdit(s.StartOffset, s.Length, currentName, newName))
                     .DistinctBy(e => e.Start)
                     .OrderBy(e => e.Start)
                     .ToList()))
            .ToList();

        return changes.Count == 0 ? MutationPlan.Empty : new MutationPlan(changes);
    }

    /// <summary>The name a sibling already holds, or null when the new name is free in this scope.</summary>
    private static string? Collides(WorkspaceSemanticModel model, SymbolId symbol, string newName)
    {
        var renamed = new QualifiedName(symbol.Name.Parts.SetItem(symbol.Name.Parts.Length - 1, newName));
        var qualified = renamed.ToString();

        bool taken = symbol.Kind switch
        {
            SymbolKind.Station => model.StationsByQn.ContainsKey(qualified),
            SymbolKind.Survey => model.SurveysByFullName.ContainsKey(qualified),
            _ => false,
        };

        return taken
            ? $"'{qualified}' already exists. Renaming to it would merge two different {symbol.Kind.ToString().ToLowerInvariant()}s."
            : null;
    }

    /// <summary>Why <paramref name="newName"/> cannot be a Therion name token, or null when it can.</summary>
    private static string? ValidateNewName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return "No new name given.";
        if (newName.Trim() != newName) return "The new name has leading or trailing whitespace.";

        int structural = newName.IndexOfAny(StructuralCharacters);
        if (structural >= 0)
            return $"'{newName}' contains '{newName[structural]}'. A rename replaces one name token, "
                 + "not a qualified path or a reference.";

        if (TherionIdentifiers.FirstIllegalChar(newName) is { } illegal)
            return $"'{newName}' contains '{illegal}', which cannot appear in a Therion name.";

        if (newName[0] == '-') return $"'{newName}' starts with '-', which Therion reads as an option.";

        return null;
    }

    private static bool TryParseKind(string value, out ReferenceKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind)
        && kind is ReferenceKind.Any or ReferenceKind.Station or ReferenceKind.Survey
        && !char.IsAsciiDigit(value.TrimStart().FirstOrDefault());

    private static ToolResult<RenameResult> Failure(string code, string message) =>
        ToolResult<RenameResult>.Failure(code, message);
}
