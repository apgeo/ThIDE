// Owns the one TherionWorkspace a headless MCP server answers questions about (02 §B.4).
// Tool calls arrive one at a time from local hosts, so a single gate is enough and keeps
// load/reload/read strictly ordered — no tool ever observes a half-loaded workspace.

using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Mcp;

/// <summary>
/// An immutable view of the loaded workspace, safe to hand to a tool and read from any thread after the
/// gate is released. Carries the file list as a captured array rather than the mutable
/// <c>TherionWorkspace</c>, so the in-app host can snapshot a live, concurrently-mutated session without a
/// cross-thread read race (T-03.2).
/// </summary>
/// <param name="DirtyFiles">
/// Absolute paths of files that are open in the IDE with unsaved edits (empty for the headless host).
/// A by-offset edit to one of these would splice buffer offsets into disk bytes, so the mutation engine
/// refuses it with <c>file_dirty</c> (the dirty-file policy, Q-01 → D-039: refuse, don't fork the buffer).
/// </param>
public sealed record WorkspaceSnapshot(
    IReadOnlyList<string> LoadedFiles,
    WorkspaceSemanticModel Model,
    string Root,
    string EntryPointPath,
    IReadOnlyCollection<string> DirtyFiles)
{
    private Dictionary<string, string>? _byCanonical;

    // `with` must not carry the lookup over: it was built from the original's file list.
    private WorkspaceSnapshot(WorkspaceSnapshot original)
    {
        LoadedFiles = original.LoadedFiles;
        Model = original.Model;
        Root = original.Root;
        EntryPointPath = original.EntryPointPath;
        DirtyFiles = original.DirtyFiles;
    }

    /// <summary>
    /// The project's own spelling of <paramref name="canonicalPath"/> — the key <see cref="LoadedFiles"/>
    /// and <c>Model.PerFile</c> use — or null when the file is not part of the project. The jail hands
    /// tools symlink-resolved paths, while the loader keys files by the path it walked, so a file reached
    /// through a symlinked root or subdirectory is only found by comparing canonical forms. Built once per
    /// snapshot.
    /// </summary>
    public string? FindLoadedFile(string canonicalPath)
    {
        var map = _byCanonical;
        if (map is null)
        {
            map = new Dictionary<string, string>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var file in LoadedFiles.Concat(Model.PerFile.Keys))
                map.TryAdd(WorkspacePaths.Canonicalize(file), file);
            _byCanonical = map;
        }
        return map.GetValueOrDefault(canonicalPath);
    }
}

/// <summary>Raised when a tool needs the workspace and none has been loaded.</summary>
public sealed class WorkspaceNotLoadedException() : Exception(
    "No workspace is loaded. Call load_workspace with a .thconfig or .th path first, "
    + "or start the server with --workspace.");

/// <summary>
/// The workspace a set of MCP tools answers questions about. The headless hosts use the disk-backed
/// <see cref="WorkspaceHost"/> (one <c>TherionWorkspace</c> the server owns); the in-app host supplies a
/// live implementation backed by the running IDE's session and its unsaved editor buffers (T-03.2).
/// </summary>
public interface IWorkspaceHost
{
    /// <summary>True when a workspace is available (a model has been built).</summary>
    bool IsLoaded { get; }

    /// <summary>Workspace root directory, or <c>null</c> when nothing is loaded.</summary>
    string? Root { get; }

    /// <summary>Absolute path of the loaded entry point, or <c>null</c> when nothing is loaded.</summary>
    string? EntryPointPath { get; }

    /// <summary>The current snapshot. Throws <see cref="WorkspaceNotLoadedException"/> when nothing is loaded.</summary>
    ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default);

    /// <summary>Opens (or rebinds to) a workspace at an entry point or project folder.</summary>
    ValueTask<WorkspaceSnapshot> LoadAsync(string pathOrFolder, CancellationToken ct = default);

    /// <summary>Re-reads the current workspace, picking up changes made since the last snapshot.</summary>
    ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default);
}

public sealed class WorkspaceHost : IWorkspaceHost, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _initialPath;

    private TherionWorkspace? _workspace;
    private WorkspaceSemanticModel? _model;
    private string? _root;
    private string? _entryPointPath;

    /// <param name="initialWorkspacePath">
    /// Entry point (or folder) to open lazily on first use — the server's <c>--workspace</c> argument.
    /// </param>
    public WorkspaceHost(string? initialWorkspacePath = null) => _initialPath = initialWorkspacePath;

    public bool IsLoaded => _model is not null;

    /// <summary>Workspace root directory, or <c>null</c> when nothing is loaded.</summary>
    public string? Root => _root;

    /// <summary>Absolute path of the loaded entry point, or <c>null</c> when nothing is loaded.</summary>
    public string? EntryPointPath => _entryPointPath;

    /// <summary>
    /// The current snapshot, opening <c>--workspace</c> on first call if one was configured.
    /// </summary>
    /// <exception cref="WorkspaceNotLoadedException">Nothing is loaded and no default was configured.</exception>
    /// <exception cref="WorkspaceLoadException">The configured default failed to open.</exception>
    public async ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Retried on every call rather than once: a --workspace that fails to open must keep saying
            // *why* it failed. Remembering "we tried" turns an actionable "two entry-point candidates"
            // into "no workspace is loaded — start the server with --workspace", which it did.
            if (_model is null && _initialPath is not null)
                await LoadCoreAsync(_initialPath, ct).ConfigureAwait(false);

            return Snapshot() ?? throw new WorkspaceNotLoadedException();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Opens (or reopens) a workspace at an entry point or project folder.</summary>
    /// <exception cref="WorkspaceLoadException">The path is missing, or a folder is empty/ambiguous.</exception>
    public async ValueTask<WorkspaceSnapshot> LoadAsync(string pathOrFolder, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(pathOrFolder, ct).ConfigureAwait(false);
            return Snapshot()!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-parses the loaded entry point from disk, picking up edits made outside the server.</summary>
    public async ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_entryPointPath is null) throw new WorkspaceNotLoadedException();
            await LoadCoreAsync(_entryPointPath, ct).ConfigureAwait(false);
            return Snapshot()!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Caller must hold the gate.</summary>
    private async ValueTask LoadCoreAsync(string pathOrFolder, CancellationToken ct)
    {
        // Resolve symlinks up front so the root, every path the jail hands back, and every FilePath the
        // model carries are all in the same canonical form. Otherwise a workspace opened through a
        // symlinked directory compares unequal to itself, and both the jail and the diagnostics filter
        // silently answer "no".
        var entryPoint = WorkspacePaths.Canonicalize(
            await WorkspaceLoader.ResolveEntryPointAsync(pathOrFolder, cancellationToken: ct).ConfigureAwait(false));

        // The entry point's directory becomes the jail every other path argument is held to, so only a
        // Therion project may define it. The shared loader is lenient on purpose (the CLI opens whatever
        // it is given); here one model-chosen path would otherwise move the jail to any directory.
        if (RefusedEntryPoint(entryPoint) is { } refusal)
            throw new WorkspaceLoadException(refusal, []);

        var opened = await WorkspaceLoader.OpenAsync(entryPoint, cancellationToken: ct).ConfigureAwait(false);

        var previous = _workspace;
        _workspace = opened;
        _model = opened.BuildSemanticModel();
        _entryPointPath = opened.EntryPointPath!;
        _root = Path.GetDirectoryName(_entryPointPath)!;

        if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Why <paramref name="entryPoint"/> (canonical) may not define the workspace, or null when it may: it
    /// must be a .th or a thconfig (by extension, Therion's bare <c>thconfig</c> name, or content sniff),
    /// and its directory must not be the filesystem root or the user's home.
    /// </summary>
    private static string? RefusedEntryPoint(string entryPoint)
    {
        bool isSurvey = Path.GetExtension(entryPoint).Equals(".th", StringComparison.OrdinalIgnoreCase);
        if (!isSurvey && !ThconfigDiscovery.IsCandidate(entryPoint, new ThconfigSniffer()))
            return $"'{entryPoint}' is not a Therion project file. Give a .thconfig or .th file, "
                 + "or a folder that holds one.";

        var root = Path.GetDirectoryName(entryPoint);
        if (string.IsNullOrEmpty(root) || string.Equals(root, Path.GetPathRoot(root), PathComparison))
            return "A project at the filesystem root cannot be opened: its folder would become the workspace.";

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)
            && string.Equals(TrimSeparators(root), TrimSeparators(WorkspacePaths.Canonicalize(home)), PathComparison))
            return "A project directly in the home folder cannot be opened: the whole home folder would become "
                 + "the workspace. Move it into a folder of its own.";

        return null;
    }

    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Caller must hold the gate.</summary>
    private WorkspaceSnapshot? Snapshot() =>
        _workspace is not null && _model is not null && _root is not null && _entryPointPath is not null
            ? new WorkspaceSnapshot(_workspace.LoadedFiles, _model, _root, _entryPointPath, DirtyFiles: [])
            : null;

    public async ValueTask DisposeAsync()
    {
        if (_workspace is not null) await _workspace.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
