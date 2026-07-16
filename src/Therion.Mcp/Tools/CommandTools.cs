using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Therion.Mcp.Tools;

/// <param name="Id">The command id to pass to <c>run_command</c>.</param>
/// <param name="Title">A short English label.</param>
/// <param name="Category">Build, View, File, Navigate, Search, Edit, or Tools.</param>
/// <param name="Gating">"allowed" (runs immediately) or "gated" (needs <c>confirm:true</c>).</param>
public sealed record CommandInfo(string Id, string Title, string Category, string Gating);

/// <param name="Commands">The commands <c>run_command</c> can run, with their gating class.</param>
public sealed record CommandList(IReadOnlyList<CommandInfo> Commands);

/// <param name="Settings">The whitelisted settings and their current values.</param>
public sealed record SettingList(IReadOnlyList<McpSettingInfo> Settings);

/// <summary>
/// Ring R3 — guarded control of the running IDE (T-03.5): running allowlisted shell commands, reading
/// and writing a whitelist of settings, saving, and applying layout presets. In-app host only. Every
/// tool that <em>changes</em> anything honors the "follow the agent" toggle (declines with
/// <c>ui_control_disabled</c> when it is off) and needs a window (<c>ui_unavailable</c> otherwise); the
/// read tools need only the window. The command allowlist is fixed in <see cref="R3CommandPolicy"/>.
/// </summary>
[McpServerToolType]
public sealed class CommandTools(IUiBridge bridge)
{
    /// <summary>Layout presets <c>set_layout</c> accepts (validated here so a bad name is a clean caller error).</summary>
    private static readonly string[] Presets = ["default", "split2", "split3", "multi-monitor"];

    private const string FollowOffMessage =
        "'Follow the agent' is off in ThIDE, so the assistant may not drive the UI. Ask the user to turn "
        + "it on (Preferences ▸ MCP), then retry. Read-only tools still work.";
    private const string NoWindowMessage =
        "The ThIDE window is not available. These tools drive the running IDE (the in-app server).";

    /// <summary>Reads need a window; writes need a window and the user's follow-agent consent.</summary>
    private ToolError? WindowGate() =>
        !bridge.IsAvailable ? new ToolError(ToolErrorCodes.UiUnavailable, NoWindowMessage) : null;

    private ToolError? ActionGate() =>
        WindowGate() ?? (!bridge.FollowAgent
            ? new ToolError(ToolErrorCodes.UiControlDisabled, FollowOffMessage)
            : null);

    private static ToolResult<ActionOk> Wrap(UiActionResult r) =>
        r.Ok ? ToolResult<ActionOk>.Success(new ActionOk(r.Message))
             : ToolResult<ActionOk>.Failure(ToolErrorCodes.UiActionFailed, r.Message);

    // ---- commands --------------------------------------------------------------------------------

    [McpServerTool(Name = "list_commands", Title = "List runnable commands", ReadOnly = true, Idempotent = true)]
    [Description("Lists the IDE commands run_command can run, each with its id, title, category, and "
               + "gating class (allowed = runs immediately; gated = needs confirm:true). Commands that "
               + "open an OS/file dialog, and editor commands that need a focused editor, are not listed "
               + "— they cannot be driven this way; use the parameterized tools (open_file, rename_symbol, "
               + "format_file) instead.")]
    public Task<ToolResult<CommandList>> ListCommands()
    {
        if (WindowGate() is { } g) return Task.FromResult(ToolResult<CommandList>.Failure(g));
        var commands = R3CommandPolicy.RunnableCommands
            .Select(c => new CommandInfo(c.Id, c.Title, c.Category,
                c.Gate == R3CommandGate.Gated ? "gated" : "allowed"))
            .ToList();
        return Task.FromResult(ToolResult<CommandList>.Success(new CommandList(commands)));
    }

    [McpServerTool(Name = "run_command", Title = "Run an IDE command", Destructive = true)]
    [Description("Runs one IDE command by id (see list_commands). Allowed commands run at once; a gated "
               + "command (it can change your files, e.g. Save) needs confirm:true. Commands that open a "
               + "dialog, or that need a focused editor, are refused — use open_file / rename_symbol / "
               + "format_file for those. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> RunCommand(
        [Description("The command id from list_commands, e.g. 'Build' or 'ToggleDiagnostics'.")]
        string commandId,
        [Description("Set true to run a gated command (one that can change files). Ignored for allowed commands.")]
        bool confirm = false,
        CancellationToken ct = default)
    {
        if (ActionGate() is { } g) return ToolResult<ActionOk>.Failure(g);

        var command = R3CommandPolicy.Find(commandId);
        if (command is null)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown command id '{commandId}'. Call list_commands for the ids you can run.");

        if (command.Gate == R3CommandGate.Excluded)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.CommandExcluded,
                $"'{commandId}' opens a dialog or needs a focused editor, so it cannot be run this way. "
                + "Use open_file, rename_symbol or format_file instead.");

        if (command.Gate == R3CommandGate.Gated && !confirm)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.ConfirmationRequired,
                $"'{commandId}' ({command.Title}) can change your files. Re-call with confirm:true to proceed.");

        return Wrap(await bridge.RunCommandAsync(commandId).ConfigureAwait(false));
    }

    // ---- settings --------------------------------------------------------------------------------

    [McpServerTool(Name = "list_settings", Title = "List settings", ReadOnly = true, Idempotent = true)]
    [Description("Lists the IDE settings the assistant may read and change, with their current values, "
               + "type, and accepted values. This is a small curated whitelist, not every preference.")]
    public Task<ToolResult<SettingList>> ListSettings()
    {
        if (WindowGate() is { } g) return Task.FromResult(ToolResult<SettingList>.Failure(g));
        return Task.FromResult(ToolResult<SettingList>.Success(new SettingList(bridge.ListSettings())));
    }

    [McpServerTool(Name = "get_setting", Title = "Get a setting", ReadOnly = true, Idempotent = true)]
    [Description("Reads one whitelisted setting by key (see list_settings for the keys).")]
    public Task<ToolResult<McpSettingInfo>> GetSetting(
        [Description("The setting key, e.g. 'editor.wordWrap'.")] string key)
    {
        if (WindowGate() is { } g) return Task.FromResult(ToolResult<McpSettingInfo>.Failure(g));
        return Task.FromResult(bridge.GetSetting(key) is { } info
            ? ToolResult<McpSettingInfo>.Success(info)
            : ToolResult<McpSettingInfo>.Failure(ToolErrorCodes.InvalidArgument,
                $"'{key}' is not a setting you can read. Call list_settings for the keys."));
    }

    [McpServerTool(Name = "set_setting", Title = "Change a setting", Idempotent = true)]
    [Description("Changes one whitelisted setting. The value is a string in the setting's type — "
               + "'true'/'false' for a bool, a number, or one of the listed enum values. Persisted "
               + "immediately, exactly like changing it in Preferences. Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> SetSetting(
        [Description("The setting key from list_settings, e.g. 'editor.wordWrap'.")] string key,
        [Description("The new value, as a string in the setting's type (e.g. 'true', '14', 'Dark').")] string value,
        CancellationToken ct = default)
    {
        if (ActionGate() is { } g) return ToolResult<ActionOk>.Failure(g);

        // Distinguish an unknown key (nothing to set) from a bad value (key is fine, value isn't).
        if (bridge.GetSetting(key) is null)
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.InvalidArgument,
                $"'{key}' is not a setting you can change. Call list_settings for the keys.");

        var result = await bridge.SetSettingAsync(key, value).ConfigureAwait(false);
        return result.Ok
            ? ToolResult<ActionOk>.Success(new ActionOk(result.Message))
            : ToolResult<ActionOk>.Failure(ToolErrorCodes.InvalidArgument, result.Message);
    }

    // ---- save / layout ---------------------------------------------------------------------------

    [McpServerTool(Name = "save_all", Title = "Save all files", Idempotent = true)]
    [Description("Saves every open file that has unsaved changes. A no-op when nothing is dirty. "
               + "Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> SaveAll(CancellationToken ct = default)
    {
        if (ActionGate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.SaveAllAsync().ConfigureAwait(false));
    }

    [McpServerTool(Name = "set_layout", Title = "Apply a layout preset", Idempotent = true)]
    [Description("Applies a built-in window layout preset: 'default' (restore the default arrangement), "
               + "'split2' or 'split3' (split the side rails so more panels show), or 'multi-monitor' "
               + "(spread preview panels across monitors — needs a second monitor). Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> SetLayout(
        [Description("One of: default, split2, split3, multi-monitor.")] string preset,
        CancellationToken ct = default)
    {
        if (ActionGate() is { } g) return ToolResult<ActionOk>.Failure(g);

        var normalized = (preset ?? string.Empty).Trim().ToLowerInvariant();
        if (!Presets.Contains(normalized))
            return ToolResult<ActionOk>.Failure(ToolErrorCodes.InvalidArgument,
                $"Unknown layout preset '{preset}'. Use one of: {string.Join(", ", Presets)}.");

        return Wrap(await bridge.SetLayoutAsync(normalized).ConfigureAwait(false));
    }

    [McpServerTool(Name = "reset_layout", Title = "Reset the layout", Idempotent = true)]
    [Description("Restores the default window layout — the same as set_layout with 'default'. "
               + "Honors 'follow the agent'.")]
    public async Task<ToolResult<ActionOk>> ResetLayout(CancellationToken ct = default)
    {
        if (ActionGate() is { } g) return ToolResult<ActionOk>.Failure(g);
        return Wrap(await bridge.SetLayoutAsync("default").ConfigureAwait(false));
    }
}
