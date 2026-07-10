namespace Therion.Mcp;

internal static class WorkspaceHostExtensions
{
    /// <summary>
    /// The snapshot, or the wire error explaining why there isn't one. Tools return errors rather
    /// than throwing: a model can act on <c>workspace_not_loaded</c>, but a protocol-level exception
    /// just ends its turn.
    /// </summary>
    public static async Task<(WorkspaceSnapshot? Snapshot, ToolError? Error)> TryGetSnapshotAsync(
        this IWorkspaceHost host, CancellationToken ct)
    {
        try
        {
            return (await host.GetAsync(ct), null);
        }
        catch (WorkspaceNotLoadedException ex)
        {
            return (null, new ToolError(ToolErrorCodes.WorkspaceNotLoaded, ex.Message));
        }
        catch (Workspace.WorkspaceLoadException ex)
        {
            return (null, new ToolError(ToolErrorCodes.WorkspaceLoadFailed, ex.Message));
        }
    }
}
