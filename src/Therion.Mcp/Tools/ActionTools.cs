using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Workspace;

namespace Therion.Mcp.Tools;

/// <param name="Message">What happened.</param>
public sealed record ActionOk(string Message);

/// <summary>
/// Ring R3 — driving the running IDE (T-03.4). In-app host only. These are the benign, reversible
/// actions — open a file, focus a pane, navigate, reveal in 3D, notify; nothing here opens a modal
/// dialog (doc 03 §C.3). Every tool honors the "follow the agent" toggle: it declines with
/// <c>ui_control_disabled</c> when the toggle is off, and returns <c>ui_unavailable</c> when there is no
/// window. (The read tools, by contrast, ignore the toggle.)
/// </summary>
[McpServerToolType]
public sealed class ActionTools(IUiBridge bridge, IWorkspaceHost host)
{
    private const string UiUnavailableMessage =
        "The ThIDE window is not available. These tools drive the running IDE (the in-app server).";
    private const string FollowOffMessage =
        "'Follow the agent' is off in ThIDE, so the assistant may not drive the UI. Ask the user to turn "
        + "it on (Preferences), then retry. Read-only tools still work.";

    /// <summary>The gate every action passes: window present, and the user is following the agent.</summary>
    private ToolError? Gate() =>
        !bridge.IsAvailable ? new ToolError(ToolErrorCodes.UiUnavailable, UiUnavailableMessage)
        : !bridge.FollowAgent ? new ToolError(ToolErrorCodes.UiControlDisabled, FollowOffMessage)
        : null;

    private static ToolResult<ActionOk> Wrap(UiActionResult r) =>
        r.Ok ? ToolResult<ActionOk>.Success(new ActionOk(r.Message))
             : ToolResult<ActionOk>.Failure(ToolErrorCodes.UiActionFailed, r.Message);

    [McpServerTool(Name = "open_file", Title = "Open file in editor", Idempotent = true)]
    [Description("Opens a project file in the editor and raises its tab, optionally scrolling to a "
               + "1-based line. The path is workspace-relative and jailed to the project — no OS file "
               + "dialog. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> OpenFile(
        [Description("Workspace-relative path, forward slashes (e.g. 'caves/upper.th').")]
        string path,
        [Description("1-based line to scroll to. Omit to leave the caret where it was.")]
        int? line = null,
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        if (host.Root is not { } root)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.WorkspaceNotLoaded, "No project is open in the IDE.");
        if (!WorkspacePaths.TryResolve(root, path, out var full, out var reason))
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        return Wrap(await bridge.OpenFileAsync(full, line).ConfigureAwait(false));
    }

    [McpServerTool(Name = "focus_tool", Title = "Focus a tool pane", Idempotent = true)]
    [Description("Makes a tool pane visible and focused (ensure-visible; it does not toggle it off). Pass "
               + "the pane id — get_ui_state lists the open panes, and an unknown id comes back with the "
               + "available ids. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> FocusTool(
        [Description("Tool pane id, e.g. 'Diagnostics', 'ObjectBrowser', 'LivePreview', 'Model3DViewer'.")]
        string id,
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.FocusToolAsync(id).ConfigureAwait(false));
    }

    [McpServerTool(Name = "goto_symbol", Title = "Go to symbol", Idempotent = true)]
    [Description("Opens the file that declares a station or survey and scrolls the editor to it. Give a "
               + "qualified name — '1@upper', 'upper.fuoco-fossile'. Use find_references / goto_definition "
               + "first if you are unsure of the exact name. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> GotoSymbol(
        [Description("Qualified station or survey name (e.g. '1@upper').")]
        string qualifiedName,
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.GotoSymbolAsync(qualifiedName).ConfigureAwait(false));
    }

    [McpServerTool(Name = "show_in_3d", Title = "Show station in 3D", Idempotent = true)]
    [Description("Reveals a station in the embedded 3D model viewer, by full station name. Needs a built "
               + ".lox/.3d model loaded in the viewer (run_build first if there is none). Honors "
               + "'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> ShowInThreeD(
        [Description("Full station name (survey-qualified, e.g. 'upper.12').")]
        string station,
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.ShowInThreeDAsync(station).ConfigureAwait(false));
    }

    [McpServerTool(Name = "show_toast", Title = "Show a notification")]
    [Description("Shows a toast notification to the user — the polite way to tell them something (a "
               + "finding, a heads-up). It does not block or ask a question. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> ShowToast(
        [Description("The message text to show the user.")]
        string text,
        [Description("Severity: info (default), success, warning, or error.")]
        string kind = "info",
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.ShowToastAsync(text, kind).ConfigureAwait(false));
    }

    [McpServerTool(Name = "set_active_thconfig", Title = "Set the active build config", Idempotent = true)]
    [Description("Switches the IDE's active thconfig — the file 'run the build' compiles and the project "
               + "the rest of the UI targets. Give a workspace-relative path to one of the project's "
               + "thconfig files; call workspace_info for entryPointCandidates. Use this instead of "
               + "run_build(entryPoint) so 'the current project' stays consistent and the output lands "
               + "where the user expects. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> SetActiveThconfig(
        [Description("Workspace-relative path to a thconfig, e.g. 'caves/deep.thconfig'.")]
        string path,
        CancellationToken ct = default)
    {
        if (Gate() is { } g) return ToolResult<ActionOk>.Failure(g);
        if (host.Root is not { } root)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.WorkspaceNotLoaded, "No project is open in the IDE.");
        if (!WorkspacePaths.TryResolve(root, path, out var full, out var reason))
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.PathOutsideWorkspace, reason);

        // Only a real entry point may be activated — the same set workspace_info reports.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool isCandidate = ThconfigDiscovery.Scan(root, new ThconfigSniffer())
            .Any(c => string.Equals(WorkspacePaths.Canonicalize(c), full, comparison));
        if (!isCandidate)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.InvalidArgument,
                $"'{path}' is not one of this project's build entry points. Call workspace_info for entryPointCandidates.");

        return Wrap(await bridge.SetActiveThconfigAsync(full).ConfigureAwait(false));
    }
}
