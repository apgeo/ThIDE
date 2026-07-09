using System.ComponentModel;
using ModelContextProtocol.Server;
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

/// <summary>Ring R1 — discovering and reading the workspace. Every path parameter is jailed to the root.</summary>
[McpServerToolType]
public sealed class WorkspaceTools(WorkspaceHost host)
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
        var (snapshot, error) = await TryGetAsync(ct);
        if (error is not null) return ToolResult<FileList>.Failure(error);

        IEnumerable<string> absolute = orphansOnly
            ? FindOrphans(snapshot!.Root, ct)
            : snapshot!.Workspace.LoadedFiles;

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
        var (snapshot, error) = await TryGetAsync(ct);
        if (error is not null) return ToolResult<FileContent>.Failure(error);

        if (!WorkspacePaths.TryResolve(snapshot!.Root, path, out var full, out var reason))
            return ToolResult<FileContent>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        if (!File.Exists(full))
            return ToolResult<FileContent>.Failure(ToolErrorCodes.FileNotFound, $"No such file: {path}");

        string[] lines;
        try
        {
            lines = EncodingResolver.ReadAllText(full).ReplaceLineEndings("\n").Split('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult<FileContent>.Failure(ToolErrorCodes.ReadFailed, ex.Message);
        }

        int start = Math.Clamp(offset, 0, lines.Length);
        int budget = ToolLimits.ClampBytes(maxBytes);
        int wanted = limit <= 0 ? lines.Length - start : Math.Min(limit, lines.Length - start);

        var text = new System.Text.StringBuilder();
        int taken = 0;
        for (; taken < wanted; taken++)
        {
            var line = lines[start + taken];
            // +1 for the newline this line will contribute once joined.
            if (text.Length + line.Length + 1 > budget) break;
            if (taken > 0) text.Append('\n');
            text.Append(line);
        }

        bool truncated = start + taken < lines.Length;
        return ToolResult<FileContent>.Success(new FileContent(
            Path: WorkspacePaths.ToRelative(snapshot.Root, full),
            Text: text.ToString(),
            Offset: start,
            LineCount: taken,
            TotalLines: lines.Length,
            Truncated: truncated));
    }

    /// <summary>Loads on demand, translating the two load failures into wire errors.</summary>
    private async Task<(WorkspaceSnapshot? Snapshot, ToolError? Error)> TryGetAsync(CancellationToken ct)
    {
        try
        {
            return (await host.GetAsync(ct), null);
        }
        catch (WorkspaceNotLoadedException ex)
        {
            return (null, new ToolError(ToolErrorCodes.WorkspaceNotLoaded, ex.Message));
        }
        catch (WorkspaceLoadException ex)
        {
            return (null, new ToolError(ToolErrorCodes.WorkspaceLoadFailed, ex.Message));
        }
    }

    private static WorkspaceInfo Describe(WorkspaceSnapshot snapshot) => new(
        Loaded: true,
        Root: snapshot.Root,
        EntryPoint: WorkspacePaths.ToRelative(snapshot.Root, snapshot.EntryPointPath),
        FileCount: snapshot.Workspace.LoadedFiles.Length,
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
