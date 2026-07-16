using System.Collections.Immutable;
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Therion.Core;
using Therion.Syntax;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <summary>What <c>workspace_info</c> and <c>load_workspace</c> report.</summary>
/// <param name="Loaded">False when no workspace is open; every other field is then null/empty.</param>
/// <param name="Root">Absolute path of the directory holding the entry point.</param>
/// <param name="EntryPoint">Workspace-relative path of the loaded .thconfig or .th.</param>
/// <param name="FileCount">Files reached through the source/input/load graph, entry point included.</param>
/// <param name="EntryPointCandidates">Other .thconfig-like files under the root, workspace-relative.</param>
public sealed record WorkspaceInfo(
    bool Loaded,
    string? Root,
    string? EntryPoint,
    int FileCount,
    IReadOnlyList<string> EntryPointCandidates);

/// <param name="Files">Workspace-relative paths, ordinal-sorted.</param>
/// <param name="Total">Matches before paging.</param>
/// <param name="Truncated">True when <paramref name="Total"/> exceeds <c>offset + limit</c>.</param>
public sealed record FileList(IReadOnlyList<string> Files, int Total, int Offset, bool Truncated);

/// <param name="Text">The requested slice, newline-joined.</param>
/// <param name="TotalLines">Line count of the whole file.</param>
/// <param name="Truncated">True when the byte cap cut the slice short, or lines remain after it.</param>
public sealed record FileContent(
    string Path,
    string Text,
    int Offset,
    int LineCount,
    int TotalLines,
    bool Truncated);

/// <param name="Kind">The block keyword: survey, centreline, group, map, scrap, layout, export, …</param>
/// <param name="Name">The declared id, when the block has one (survey name, map/scrap/layout id).</param>
/// <param name="StartLine">1-based line of the block header.</param>
/// <param name="EndLine">
/// 1-based line of the block's last <em>content</em> line. The closing keyword (<c>endsurvey</c> …)
/// sits after it and is not part of the span the parser records, so it is not counted here.
/// </param>
/// <param name="Depth">Nesting level; 0 at the top of the file.</param>
/// <param name="Detail">Whatever identifies this block at a glance — a projection, a format, a path.</param>
public sealed record OutlineEntry(
    string Kind,
    string? Name,
    int StartLine,
    int EndLine,
    int Depth,
    string? Detail);

/// <param name="Entries">Blocks in source order, parents before children.</param>
/// <param name="TotalLines">Line count of the whole file, so a caller can judge what read_file will cost.</param>
public sealed record FileOutline(
    string Path,
    IReadOnlyList<OutlineEntry> Entries,
    int TotalLines);

/// <summary>Ring R1 — discovering and reading the workspace. Every path parameter is jailed to the root.</summary>
[McpServerToolType]
public sealed class WorkspaceTools(IWorkspaceHost host)
{
    private static readonly string[] TherionFileExtensions = [".th", ".th2"];

    [McpServerTool(Name = "workspace_info", Title = "Workspace info", ReadOnly = true, Idempotent = true)]
    [Description("Which Therion project this server currently has open: its root, entry point "
               + "(.thconfig or .th), how many files the source graph reaches, and any other "
               + "entry-point candidates found under the root. Returns loaded:false if none is open.")]
    public async Task<ToolResult<WorkspaceInfo>> WorkspaceInfo(CancellationToken ct = default)
    {
        WorkspaceSnapshot snapshot;
        try
        {
            snapshot = await host.GetAsync(ct);
        }
        catch (WorkspaceNotLoadedException)
        {
            return ToolResult<WorkspaceInfo>.Success(new WorkspaceInfo(false, null, null, 0, []));
        }
        catch (WorkspaceLoadException ex)
        {
            return ToolResult<WorkspaceInfo>.Failure(ToolErrorCodes.WorkspaceLoadFailed, ex.Message);
        }

        return ToolResult<WorkspaceInfo>.Success(Describe(snapshot));
    }

    [McpServerTool(Name = "load_workspace", Title = "Load workspace", ReadOnly = true, Idempotent = true)]
    [Description("Opens a Therion project and makes it the subject of every other tool. Give it a "
               + ".thconfig or .th file, or a folder containing exactly one entry-point candidate. "
               + "This is the one path argument not restricted to the current workspace — it "
               + "defines the workspace. Loading again replaces what was open.")]
    public async Task<ToolResult<WorkspaceInfo>> LoadWorkspace(
        [Description("Absolute or working-directory-relative path to a .thconfig, a .th, or a project folder.")]
        string path,
        CancellationToken ct = default)
    {
        try
        {
            var snapshot = await host.LoadAsync(path, ct);
            return ToolResult<WorkspaceInfo>.Success(Describe(snapshot));
        }
        catch (WorkspaceLoadException ex)
        {
            return ToolResult<WorkspaceInfo>.Failure(ToolErrorCodes.WorkspaceLoadFailed, ex.Message);
        }
    }

    [McpServerTool(Name = "list_files", Title = "List files", ReadOnly = true, Idempotent = true)]
    [Description("Lists the project's files as workspace-relative paths. By default these are the "
               + "files reachable from the entry point. With orphansOnly, it instead lists .th/.th2 "
               + "files on disk under the root that no .thconfig in the project reaches — the "
               + "candidates for dead files.")]
    public async Task<ToolResult<FileList>> ListFiles(
        [Description("Filter by extension, with or without the dot (e.g. 'th', '.th2'). Omit for all.")]
        string? extension = null,
        [Description("List unreachable .th/.th2 files on disk instead of the loaded file set.")]
        bool orphansOnly = false,
        [Description("Number of entries to skip, for paging.")]
        int offset = 0,
        [Description("Maximum entries to return; capped at 2000, defaults to 200.")]
        int limit = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<FileList>.Failure(error);

        IEnumerable<string> absolute = orphansOnly
            ? FindOrphans(snapshot!.Root, ct)
            : snapshot!.LoadedFiles;

        if (!string.IsNullOrWhiteSpace(extension))
        {
            var wanted = extension.StartsWith('.') ? extension : "." + extension;
            absolute = absolute.Where(p => Path.GetExtension(p).Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }

        var all = absolute
            .Select(p => WorkspacePaths.ToRelative(snapshot!.Root, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        int start = Math.Clamp(offset, 0, all.Count);
        int take = ToolLimits.ClampLimit(limit);
        var page = all.Skip(start).Take(take).ToList();

        return ToolResult<FileList>.Success(
            new FileList(page, all.Count, start, Truncated: start + page.Count < all.Count));
    }

    [McpServerTool(Name = "read_file", Title = "Read file", ReadOnly = true, Idempotent = true)]
    [Description("Reads a text file from the workspace, decoding it per its BOM or its Therion "
               + "'encoding' directive (UTF-8 otherwise). Returns whole-line slices; a slice cut "
               + "short by the byte cap comes back with truncated:true.")]
    public async Task<ToolResult<FileContent>> ReadFile(
        [Description("Workspace-relative path, forward slashes (e.g. 'caves/upper.th').")]
        string path,
        [Description("First line to return, 0-based.")]
        int offset = 0,
        [Description("Maximum lines to return; 0 means as many as the byte cap allows.")]
        int limit = 0,
        [Description("Byte budget for the returned text; capped at 1000000, defaults to 100000.")]
        int maxBytes = 0,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<FileContent>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, path, out var full, out var reason))
            return ToolResult<FileContent>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (!File.Exists(full))
            return ToolResult<FileContent>.Failure(ToolErrorCodes.FileNotFound, $"No such file: {path}");

        string[] lines;
        try
        {
            lines = SplitLines(EncodingResolver.ReadAllText(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<FileContent>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        int start = Math.Clamp(offset, 0, lines.Length);
        int budget = ToolLimits.ClampBytes(maxBytes);
        int wanted = limit <= 0 ? lines.Length - start : Math.Min(limit, lines.Length - start);

        var text = new System.Text.StringBuilder();
        int used = 0, taken = 0;
        bool cutMidLine = false;

        for (; taken < wanted; taken++)
        {
            var line = lines[start + taken];
            int cost = Encoding.UTF8.GetByteCount(line) + 1;   // +1 for the newline once joined

            if (used + cost > budget)
            {
                // Never return zero lines: a caller advancing by lineCount would make no progress and
                // could never read past a single over-long line. Give it a byte-safe prefix instead.
                if (taken > 0) break;

                text.Append(ToolLimits.Utf8Prefix(line, budget));
                cutMidLine = true;
                taken = 1;
                break;
            }

            if (taken > 0) text.Append('\n');
            text.Append(line);
            used += cost;
        }

        bool truncated = cutMidLine || start + taken < lines.Length;
        return ToolResult<FileContent>.Success(new FileContent(
            Path: WorkspacePaths.ToRelative(snapshot.Root, full),
            Text: text.ToString(),
            Offset: start,
            LineCount: taken,
            TotalLines: lines.Length,
            Truncated: truncated));
    }

    [McpServerTool(Name = "get_file_outline", Title = "Outline a file", ReadOnly = true, Idempotent = true)]
    [Description("The block structure of one Therion file — survey, centreline, map, scrap, layout, "
               + "export … — each with its name and line range, nested by depth. Use it to find where a "
               + "block lives before reading or editing it, instead of paging the whole file with "
               + "read_file. Works on .th, .th2 and .thconfig, and on files the project does not "
               + "reference. endLine is the block's last content line, before its closing keyword. A "
               + "file with syntax errors still outlines as far as it parsed; call get_diagnostics for "
               + "what is wrong with it.")]
    public async Task<ToolResult<FileOutline>> GetFileOutline(
        [Description("Workspace-relative path, forward slashes (e.g. 'caves/upper.th').")]
        string file,
        CancellationToken ct = default)
    {
        var (snapshot, error) = await host.TryGetSnapshotAsync(ct);
        if (error is not null) return ToolResult<FileOutline>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, file, out var full, out var reason))
            return ToolResult<FileOutline>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (!File.Exists(full))
            return ToolResult<FileOutline>.Failure(ToolErrorCodes.FileNotFound, $"No such file: {file}");

        string text;
        try
        {
            text = EncodingResolver.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<FileOutline>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        // Parsed here rather than taken from the snapshot: the snapshot carries the semantic model,
        // which has resolved symbols but not the block spans an outline is made of. The parse is
        // per-file and lenient, so a file with errors still outlines as far as it got.
        var parsed = TherionWorkspace.ParseText(full, text);
        if (parsed.Value is null)
            return ToolResult<FileOutline>.Failure(ToolErrorCodes.ReadFailed,
                $"'{file}' could not be parsed far enough to outline.");

        var entries = new List<OutlineEntry>();
        Walk(parsed.Value.Children, 0, entries);

        return ToolResult<FileOutline>.Success(new FileOutline(
            Path: WorkspacePaths.ToRelative(snapshot.Root, full),
            Entries: entries,
            TotalLines: SplitLines(text).Length));
    }

    /// <summary>
    /// Adds the structural nodes of <paramref name="nodes"/> in source order, recursing into blocks.
    /// Anything that is not a block or a structural command (shot rows, station commands, trivia) is
    /// deliberately skipped — this is a table of contents, not a second AST.
    /// </summary>
    private static void Walk(ImmutableArray<TherionNode> nodes, int depth, List<OutlineEntry> entries)
    {
        foreach (var node in nodes)
        {
            var entry = Entry(node, depth);
            if (entry is not null) entries.Add(entry);

            // Recurse even when the node itself is not listed, so a block nested inside an
            // unlisted one still surfaces. Depth only advances for nodes that made an entry.
            if (node is BlockCommand block)
                Walk(block.Children, entry is null ? depth : depth + 1, entries);
        }
    }

    // No "unterminated" flag: BlockCommand.IsTerminated is only honest for map and layout — the
    // parser hardcodes it true for centreline and computes a best-effort marker for survey. A
    // missing terminator is reported properly, with a location, by get_diagnostics
    // (UnterminatedBlock / MismatchedBlockTerminator), which is the tool that owns it.
    private static OutlineEntry? Entry(TherionNode node, int depth) => node switch
    {
        SurveyCommand s => Make("survey", s.Name, s.Span, depth, Title(s.OptionsRaw)),
        CentrelineCommand c => Make("centreline", null, c.Span, depth, Title(c.OptionsRaw)),
        GroupCommand g => Make("group", null, g.Span, depth, null),
        ScrapBlock sc => Make("scrap", sc.Id, sc.Span, depth, Title(sc.OptionsRaw)),
        MapCommand m => Make("map", m.Id, m.Span, depth, m.Projection is { } p ? $"projection {p}" : null),
        SurfaceCommand sf => Make("surface", null, sf.Span, depth, null),
        ScanCommand scan => Make("scan", null, scan.Span, depth, null),
        LayoutCommand l => Make("layout", l.Id, l.Span, depth, l.CopyFrom is { } from ? $"copy {from}" : null),
        ExportCommand e => Make("export", e.ExportType, e.Span, depth, ExportDetail(e)),
        SelectCommand sel => Make(sel.IsUnselect ? "unselect" : "select", sel.Object, sel.Span, depth, null),
        InputCommand i => Make("input", i.Path, i.Span, depth, null),
        // A .thconfig's `source`/`input`/`load` stay UnknownCommand by design (ThconfigAst keeps them
        // that way for SourceGraph), but what a thconfig pulls in is half its table of contents.
        UnknownCommand u when SourceKeywords.Contains(u.Keyword) =>
            Make(u.Keyword.ToLowerInvariant(), u.RawArguments.Trim(), u.Span, depth, null),
        _ => null,
    };

    /// <summary>The commands that pull another file into the project, whatever the file type.</summary>
    private static readonly ImmutableHashSet<string> SourceKeywords =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "source", "input", "load");

    private static OutlineEntry Make(string kind, string? name, SourceSpan span, int depth, string? detail) =>
        new(kind, string.IsNullOrWhiteSpace(name) ? null : name,
            span.Start.Line, span.End.Line, depth, detail);

    private static string? ExportDetail(ExportCommand e) =>
        (e.Format, e.Output) switch
        {
            ({ } f, { } o) => $"-fmt {f} -o {o}",
            ({ } f, null) => $"-fmt {f}",
            (null, { } o) => $"-o {o}",
            _ => null,
        };

    /// <summary>The <c>-title "…"</c> of a block, which is what a reader recognises it by.</summary>
    private static string? Title(string optionsRaw)
    {
        if (string.IsNullOrWhiteSpace(optionsRaw)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            optionsRaw, @"-title\s+""([^""]*)""|-title\s+(\S+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return null;
        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The file's lines. A trailing newline terminates the last line; it does not begin an empty one,
    /// so `a\nb\n` is two lines, not three.
    /// </summary>
    private static string[] SplitLines(string text)
    {
        var normalized = text.ReplaceLineEndings("\n");
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        return normalized.Length == 0 ? [] : normalized.Split('\n');
    }

    private static WorkspaceInfo Describe(WorkspaceSnapshot snapshot) => new(
        Loaded: true,
        Root: snapshot.Root,
        EntryPoint: WorkspacePaths.ToRelative(snapshot.Root, snapshot.EntryPointPath),
        FileCount: snapshot.LoadedFiles.Count,
        EntryPointCandidates: ThconfigDiscovery.Scan(snapshot.Root, new ThconfigSniffer())
            .Select(p => WorkspacePaths.ToRelative(snapshot.Root, p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList());

    /// <summary>
    /// .th/.th2 files under the root that no .thconfig in the project reaches. Mirrors the app's
    /// Overview ▸ Audit orphan scan, minus its user-configured directory exclusions — a headless
    /// server has no such settings, so backup and archive folders show up here.
    /// </summary>
    private static List<string> FindOrphans(string root, CancellationToken ct)
    {
        var entryPoints = ThconfigDiscovery.Scan(root, new ThconfigSniffer());
        var reachable = WorkspaceReachability.ReachableFrom(entryPoints, ct);

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true };
        var orphans = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.*", options))
        {
            ct.ThrowIfCancellationRequested();
            if (!TherionFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;

            var full = Path.GetFullPath(file);
            if (!reachable.Contains(full)) orphans.Add(full);
        }
        return orphans;
    }
}
