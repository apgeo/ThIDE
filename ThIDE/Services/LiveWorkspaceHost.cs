// The in-app MCP host's window onto the *running* IDE (T-03.2). Every tool that reads the workspace
// gets the live WorkspaceSessionService model with the current unsaved editor buffers overlaid, so the
// agent sees what the user sees — not stale disk state. All session/buffer access is marshalled onto the
// UI thread through IUiBridge; the snapshot it returns is fully immutable (a captured file list + an
// immutable semantic model), safe to read on the Kestrel handler thread after the marshal completes.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Mcp;

namespace ThIDE.Services;

/// <summary>An <see cref="IWorkspaceHost"/> backed by the live <see cref="IWorkspaceSession"/>.</summary>
public sealed class LiveWorkspaceHost : IWorkspaceHost
{
    private readonly IWorkspaceSession _session;
    private readonly IUnsavedBufferProvider _buffers;
    private readonly IUiBridge _bridge;
    // Serializes snapshot builds so a burst of concurrent tool calls can't stack buffer revalidations.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LiveWorkspaceHost(IWorkspaceSession session, IUnsavedBufferProvider buffers, IUiBridge bridge)
    {
        _session = session;
        _buffers = buffers;
        _bridge = bridge;
    }

    // Reference reads of session state; benign off the UI thread, and ServerInfoTool wants them sync.
    public bool IsLoaded => _session.Model is not null;
    public string? Root => _session.RootPath;
    public string? EntryPointPath => _session.ActiveThconfig?.FullPath;

    public async ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await _bridge.InvokeAsync(async () =>
            {
                // Overlay every open, dirty, in-graph buffer — the same set the app's own live validation
                // uses (DocumentService) — so diagnostics/symbols reflect the editor, not the last save.
                var buffers = _buffers.DirtyBuffers();
                if (buffers.Count > 0)
                    await _session.RevalidateBuffersAsync(buffers, ct).ConfigureAwait(true);

                var model = _session.Model ?? throw new WorkspaceNotLoadedException();
                var root = _session.RootPath ?? throw new WorkspaceNotLoadedException();
                var entry = _session.ActiveThconfig?.FullPath ?? root;
                // Capture the file list as an array here, on the UI thread, so the snapshot stays immutable
                // and safe to read after we leave the dispatcher.
                var files = model.PerFile.Keys.ToArray();
                return new WorkspaceSnapshot(files, model, root, entry);
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The in-app server follows the project open in ThIDE; an agent cannot switch it through MCP (that
    /// would be a UI action for a later ring). Surfaces as a clean <c>workspace_load_failed</c>.
    /// </summary>
    public ValueTask<WorkspaceSnapshot> LoadAsync(string pathOrFolder, CancellationToken ct = default) =>
        throw new Therion.Workspace.WorkspaceLoadException(
            "The in-app MCP server follows the project currently open in ThIDE. "
            + "Open a different project in the IDE to change it; it cannot be switched over MCP.",
            Array.Empty<string>());

    /// <summary>Re-reads the live session (there is no separate on-disk copy to reload).</summary>
    public ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default) => GetAsync(ct);
}
