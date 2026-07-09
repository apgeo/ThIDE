// Owns the one TherionWorkspace a headless MCP server answers questions about (02 §B.4).
// Tool calls arrive one at a time from local hosts, so a single gate is enough and keeps
// load/reload/read strictly ordered — no tool ever observes a half-loaded workspace.

using Therion.Semantics;
using Therion.Workspace;

namespace Therion.Mcp;

/// <summary>An immutable view of the loaded workspace, safe to hand to a tool after the gate is released.</summary>
public sealed record WorkspaceSnapshot(
    TherionWorkspace Workspace,
    WorkspaceSemanticModel Model,
    string Root,
    string EntryPointPath);

/// <summary>Raised when a tool needs the workspace and none has been loaded.</summary>
public sealed class WorkspaceNotLoadedException() : Exception(
    "No workspace is loaded. Call load_workspace with a .thconfig or .th path first, "
    + "or start the server with --workspace.");

public sealed class WorkspaceHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _initialPath;

    private TherionWorkspace? _workspace;
    private WorkspaceSemanticModel? _model;
    private string? _root;
    private string? _entryPointPath;
    private bool _triedInitialLoad;

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
            if (_model is null && _initialPath is not null && !_triedInitialLoad)
            {
                _triedInitialLoad = true;
                await LoadCoreAsync(_initialPath, ct).ConfigureAwait(false);
            }

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
            _triedInitialLoad = true;
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
        var opened = await WorkspaceLoader.OpenAsync(pathOrFolder, cancellationToken: ct).ConfigureAwait(false);

        var previous = _workspace;
        _workspace = opened;
        _model = opened.BuildSemanticModel();
        _entryPointPath = opened.EntryPointPath!;
        _root = Path.GetDirectoryName(_entryPointPath)!;

        if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Caller must hold the gate.</summary>
    private WorkspaceSnapshot? Snapshot() =>
        _workspace is not null && _model is not null && _root is not null && _entryPointPath is not null
            ? new WorkspaceSnapshot(_workspace, _model, _root, _entryPointPath)
            : null;

    public async ValueTask DisposeAsync()
    {
        if (_workspace is not null) await _workspace.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
