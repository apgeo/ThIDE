using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Therion.Mcp.Tools;

/// <param name="Documents">The open editor documents.</param>
/// <param name="Total">How many are open.</param>
public sealed record OpenDocumentList(IReadOnlyList<OpenDocumentInfo> Documents, int Total);

/// <summary>
/// Ring R3 — reading the running IDE's UI. Registered only by the in-app host (where a real
/// <see cref="IUiBridge"/> is present); the headless server never exposes these. Reads are always
/// allowed — the "follow the agent" gate (T-03.5) governs the UI-<em>action</em> tools, not these — but
/// each returns <c>ui_unavailable</c> rather than throwing when there is no window to read.
/// </summary>
[McpServerToolType]
public sealed class UiTools(IUiBridge bridge)
{
    private const string UiUnavailableMessage =
        "The ThIDE window is not available. These tools work only against the running IDE (the in-app "
        + "server), and only once its main window has opened.";

    [McpServerTool(Name = "get_ui_state", Title = "Get UI state", ReadOnly = true, Idempotent = true)]
    [Description("What the IDE is showing right now: the active and focused documents, the caret "
               + "line/column and selection length in the focused editor, which tool panes are open, the "
               + "unsaved documents, and whether 'follow the agent' is on (which gates the UI-action "
               + "tools). Returns ui_unavailable when the IDE window is not up.")]
    public async Task<ToolResult<UiState>> GetUiState(CancellationToken ct = default)
    {
        if (!bridge.IsAvailable)
            return ToolResult<UiState>.Failure(ToolErrorCodes.UiUnavailable, UiUnavailableMessage);

        var state = await bridge.GetUiStateAsync().ConfigureAwait(false);
        return state is null
            ? ToolResult<UiState>.Failure(ToolErrorCodes.UiUnavailable, UiUnavailableMessage)
            : ToolResult<UiState>.Success(state);
    }

    [McpServerTool(Name = "get_open_documents", Title = "Get open documents", ReadOnly = true, Idempotent = true)]
    [Description("The files currently open in the editor — each with its absolute path, whether it is "
               + "the active tab, and whether it has unsaved changes. Returns ui_unavailable when the "
               + "IDE window is not up.")]
    public async Task<ToolResult<OpenDocumentList>> GetOpenDocuments(CancellationToken ct = default)
    {
        if (!bridge.IsAvailable)
            return ToolResult<OpenDocumentList>.Failure(ToolErrorCodes.UiUnavailable, UiUnavailableMessage);

        var docs = await bridge.GetOpenDocumentsAsync().ConfigureAwait(false);
        return ToolResult<OpenDocumentList>.Success(new OpenDocumentList(docs, docs.Count));
    }
}
