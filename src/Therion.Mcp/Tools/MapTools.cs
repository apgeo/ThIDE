using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Mcp.Mutations;
using Therion.Processing.Abstractions;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <summary>
/// Ring R2 — a structured edit to a <c>map … endmap</c> body. `edit_file` can do this when the model
/// quotes the block correctly, but the block is exactly what small models fumble; here the server
/// finds the span, checks the members exist, and inserts them. Every R2 guarantee comes from the
/// shared <see cref="MutationEngine"/>: dry-run by default, the expected-text guard, an atomic
/// encoding-preserving write, the open-and-dirty refusal, and a re-lint afterwards.
/// </summary>
[McpServerToolType]
public sealed class MapTools(IWorkspaceHost host, MutationEngine mutations)
{
    // Not readOnly (writes). Not destructive: it only ever adds lines to a block — nothing is
    // overwritten. Idempotent: members already in the map are skipped, so a repeat call is a no-op.
    [McpServerTool(Name = "add_map_members", Title = "Add scraps or sub-maps to a map",
        ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Adds scrap or sub-map references to an existing 'map … endmap' block, inserting them "
               + "after the members already there. The map is found by its id, and every member is "
               + "checked to exist in the project before anything is written — a typo is refused, not "
               + "compiled. Members already in the map are skipped, so calling twice is safe. "
               + "IMPORTANT: it PREVIEWS by default and writes NOTHING — pass dryRun:false to actually "
               + "apply it. Use list_symbols (kind 'map' or 'scrap') to find ids, or get_file_outline "
               + "to see a file's maps.")]
    public async Task<ToolResult<MutationResult>> AddMapMembers(
        [Description("The id of the map to add to, e.g. 'cave-plan'. Must already exist.")]
        string map,
        [Description("Scrap or sub-map ids to add, e.g. ['scrap1','scrap2@upper']. Must already exist.")]
        string[] members,
        [Description("Preview only (default). Pass false to write the change to disk.")]
        bool dryRun = true,
        [Description("The sha256 from an earlier result. The write is refused if the file changed since.")]
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<MutationResult>.Failure(error);

        var wanted = (members ?? [])
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .ToList();

        if (wanted.Count == 0)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                "Name at least one scrap or sub-map to add.");

        if (string.IsNullOrWhiteSpace(map))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                "Name the map to add to. Call list_symbols with kind 'map' for the ids.");

        var mapId = map.Trim();
        if (!snapshot!.Model.MapsById.TryGetValue(mapId, out var symbol))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.SymbolNotFound,
                $"No map '{mapId}' in the project. Call list_symbols with kind 'map' for the ids. "
                + "This tool adds to an existing map; it does not create one.");

        // A member that does not resolve would compile to a dangling reference, so it is refused
        // before anything is written. Resolution goes through the same index the IDE navigates with,
        // so `scrap@survey` works here exactly as it does on ctrl-click.
        var unknown = wanted.Where(m => !Resolves(snapshot.Model, m)).ToList();
        if (unknown.Count > 0)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"Not in the project: {string.Join(", ", unknown)}. A map member must be a scrap or "
                + "another map that already exists. Check the ids with list_symbols.");

        if (wanted.Contains(mapId, StringComparer.Ordinal))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"A map cannot contain itself ('{mapId}').");

        var file = symbol.DeclarationSpan.FilePath;
        if (string.IsNullOrEmpty(file))
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.ModelUnavailable,
                $"'{mapId}' has no source location, so it cannot be edited.");

        SourceFile source;
        try { source = SourceFileIo.Read(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        var parsed = TherionWorkspace.ParseText(file, source.Text);
        if (parsed.Value is null || FindMap(parsed.Value.Children, mapId) is not { } block)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.StalePlan,
                $"'{mapId}' is in the loaded model but not in '{Relative(snapshot.Root, file)}' as it "
                + "reads now. Call load_workspace to pick up the current files, then retry.");

        // Inserting "before endmap" is only meaningful if there is an endmap. An unterminated block
        // would put the new members wherever the parser gave up, which is not what anyone asked for.
        if (!block.IsTerminated)
            return ToolResult<MutationResult>.Failure(ToolErrorCodes.InvalidArgument,
                $"The 'map {mapId}' block has no 'endmap', so there is no body to add to safely. "
                + "Fix the block first — get_diagnostics points at it.");

        var already = block.Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var toAdd = wanted.Where(m => !already.Contains(m)).Distinct(StringComparer.Ordinal).ToList();

        // Everything asked for is already there. Report a clean no-op rather than an error: the
        // caller's intent ("these should be in the map") is satisfied.
        if (toAdd.Count == 0)
            return ToolResult<MutationResult>.Success(new MutationResult(dryRun, [], [], 0, 0));

        var (offset, indent) = InsertionPoint(source.Text, block);
        var newline = source.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = string.Concat(toAdd.Select(m => newline + indent + m));

        var plan = new MutationPlan([new EditFile(file, [new TextEdit(offset, 0, string.Empty, insertion)])]);
        var expected = expectedSha256 is null
            ? null
            : new Dictionary<string, string> { [Relative(snapshot.Root, file)] = expectedSha256 };

        return await mutations.ApplyAsync(plan, dryRun, expected, ct);
    }

    /// <summary>
    /// True when <paramref name="reference"/> names a scrap or a map that exists. Uses the workspace
    /// reference index rather than a raw id lookup, so the <c>id@survey</c> forms Therion accepts in a
    /// map body resolve here too.
    /// </summary>
    private static bool Resolves(Therion.Semantics.WorkspaceSemanticModel model, string reference) =>
        model.DescribeReference(reference, ReferenceKind.Map) is not null
        || model.DescribeReference(reference, ReferenceKind.ScrapObject) is not null;

    private static MapCommand? FindMap(System.Collections.Immutable.ImmutableArray<TherionNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node is MapCommand m && string.Equals(m.Id, id, StringComparison.Ordinal)) return m;
            // Maps nest inside survey blocks, so the search cannot stop at the top level.
            if (node is BlockCommand block && FindMap(block.Children, id) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Where a new member line goes, and the indentation to give it: at the end of the last member's
    /// line, or of the map header when the body is empty. Anchoring to a line the parser identified
    /// avoids searching the text for `endmap`, which could match one inside a comment.
    /// </summary>
    private static (int Offset, string Indent) InsertionPoint(string text, MapCommand block)
    {
        if (block.Members.Length > 0)
        {
            var last = block.Members[^1].Span.StartOffset;
            return (EndOfLine(text, last), IndentOf(text, last));
        }

        // Empty body: sit under the header, indented one step in from it.
        var header = block.Span.StartOffset;
        return (EndOfLine(text, header), IndentOf(text, header) + "  ");
    }

    /// <summary>
    /// The offset just past the last character of the line holding <paramref name="offset"/> —
    /// before its newline, and before the CR of a CRLF so an insertion cannot land between them.
    /// </summary>
    private static int EndOfLine(string text, int offset)
    {
        int at = text.IndexOf('\n', Math.Clamp(offset, 0, Math.Max(text.Length - 1, 0)));
        if (at < 0) return text.Length;
        return at > 0 && text[at - 1] == '\r' ? at - 1 : at;
    }

    /// <summary>The leading whitespace of the line holding <paramref name="offset"/>.</summary>
    private static string IndentOf(string text, int offset)
    {
        if (text.Length == 0) return string.Empty;

        int probe = Math.Clamp(offset, 0, text.Length - 1);
        int start = text.LastIndexOf('\n', probe) + 1;   // -1 (first line) becomes 0

        int i = start;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text[start..i];
    }

    private static string Relative(string root, string path) => WorkspacePaths.ToRelative(root, path);
}
