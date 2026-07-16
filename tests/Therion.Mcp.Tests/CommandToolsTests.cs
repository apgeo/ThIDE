// T-03.5: the guarded ring-R3 tools. Pure tests over a fake IUiBridge — the allowlist (excluded
// refused, gated needs confirm, allowed runs), the follow-agent + no-window gates, and the
// settings/save/layout surface. The fake records whether the bridge was reached, so a refusal that
// should never touch the UI is proven not to.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Therion.Mcp;
using Therion.Mcp.Tools;
using Therion.Processing.Abstractions;
using Xunit;

namespace Therion.Mcp.Tests;

public class CommandToolsTests
{
    private sealed class FakeBridge : IUiBridge
    {
        public bool IsAvailable { get; init; } = true;
        public bool FollowAgent { get; init; } = true;

        public string? RanCommand { get; private set; }
        public bool SavedAll { get; private set; }
        public string? AppliedLayout { get; private set; }
        public (string Key, string Value)? SetPair { get; private set; }

        // The whitelist the fake pretends to expose; a key outside it reads back null.
        private static readonly McpSettingInfo WordWrap =
            new("editor.wordWrap", "false", "bool", "Wrap long lines.", ["true", "false"]);

        public Task<UiActionResult> RunCommandAsync(string commandId)
        {
            RanCommand = commandId;
            return Task.FromResult(new UiActionResult(true, $"Ran '{commandId}'."));
        }

        public Task<UiActionResult> SaveAllAsync()
        {
            SavedAll = true;
            return Task.FromResult(new UiActionResult(true, "Saved."));
        }

        public Task<UiActionResult> SetLayoutAsync(string preset)
        {
            AppliedLayout = preset;
            return Task.FromResult(new UiActionResult(true, $"Applied '{preset}'."));
        }

        public IReadOnlyList<McpSettingInfo> ListSettings() => [WordWrap];

        public McpSettingInfo? GetSetting(string key) =>
            key == "editor.wordWrap" ? WordWrap : null;

        public Task<UiActionResult> SetSettingAsync(string key, string value)
        {
            SetPair = (key, value);
            // "bad" is the designated invalid value: key is known, value is not.
            return Task.FromResult(value == "bad"
                ? new UiActionResult(false, "Expected true or false, got 'bad'.")
                : new UiActionResult(true, $"Set '{key}' to '{value}'."));
        }

        // ---- base IUiBridge members (unused here) ----
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
            Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => Ok();
        public Task<UiActionResult> FocusToolAsync(string toolId) => Ok();
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Ok();
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Ok();
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Ok();
        private static Task<UiActionResult> Ok() => Task.FromResult(new UiActionResult(true, "ok"));
    }

    private static CommandTools Tools(FakeBridge bridge) => new(bridge);

    // ---- run_command allowlist ------------------------------------------------------------------

    [Fact]
    public async Task An_allowed_command_runs()
    {
        var bridge = new FakeBridge();
        var r = await Tools(bridge).RunCommand(ShellCommandIds.Build);

        Assert.True(r.Ok);
        Assert.Equal(ShellCommandIds.Build, bridge.RanCommand);
    }

    [Fact]
    public async Task An_excluded_command_is_refused_without_touching_the_ui()
    {
        var bridge = new FakeBridge();
        var open = await Tools(bridge).RunCommand(ShellCommandIds.OpenFile, confirm: true);
        var palette = await Tools(bridge).RunCommand(ShellCommandIds.CommandPalette);

        Assert.Equal(ToolErrorCodes.CommandExcluded, open.Error!.Code);
        Assert.Equal(ToolErrorCodes.CommandExcluded, palette.Error!.Code);
        Assert.Null(bridge.RanCommand);   // never reached the bridge
    }

    [Fact]
    public async Task An_editor_command_is_refused_as_excluded()
    {
        var bridge = new FakeBridge();
        var r = await Tools(bridge).RunCommand(ShellCommandIds.FormatDocument);

        Assert.Equal(ToolErrorCodes.CommandExcluded, r.Error!.Code);
        Assert.Null(bridge.RanCommand);
    }

    [Fact]
    public async Task A_gated_command_needs_confirmation()
    {
        var bridge = new FakeBridge();
        var withoutConfirm = await Tools(bridge).RunCommand(ShellCommandIds.Save);

        Assert.Equal(ToolErrorCodes.ConfirmationRequired, withoutConfirm.Error!.Code);
        Assert.Null(bridge.RanCommand);   // refused before the bridge

        var withConfirm = await Tools(bridge).RunCommand(ShellCommandIds.Save, confirm: true);
        Assert.True(withConfirm.Ok);
        Assert.Equal(ShellCommandIds.Save, bridge.RanCommand);
    }

    [Fact]
    public async Task An_unknown_command_is_an_invalid_argument()
    {
        var r = await Tools(new FakeBridge()).RunCommand("NoSuchCommand");
        Assert.Equal(ToolErrorCodes.InvalidArgument, r.Error!.Code);
    }

    // ---- gates ----------------------------------------------------------------------------------

    [Fact]
    public async Task Writes_are_declined_when_follow_agent_is_off()
    {
        var bridge = new FakeBridge { FollowAgent = false };
        var tools = Tools(bridge);

        Assert.Equal(ToolErrorCodes.UiControlDisabled, (await tools.RunCommand(ShellCommandIds.Build)).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiControlDisabled, (await tools.SaveAll()).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiControlDisabled, (await tools.SetLayout("default")).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiControlDisabled, (await tools.SetSetting("editor.wordWrap", "true")).Error!.Code);
        Assert.Null(bridge.RanCommand);
        Assert.False(bridge.SavedAll);
    }

    [Fact]
    public async Task Reads_still_work_when_follow_agent_is_off()
    {
        var tools = Tools(new FakeBridge { FollowAgent = false });

        Assert.True((await tools.ListCommands()).Ok);
        Assert.True((await tools.ListSettings()).Ok);
        Assert.True((await tools.GetSetting("editor.wordWrap")).Ok);
    }

    [Fact]
    public async Task Everything_is_ui_unavailable_without_a_window()
    {
        var tools = Tools(new FakeBridge { IsAvailable = false });

        Assert.Equal(ToolErrorCodes.UiUnavailable, (await tools.ListCommands()).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiUnavailable, (await tools.RunCommand(ShellCommandIds.Build)).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiUnavailable, (await tools.GetSetting("editor.wordWrap")).Error!.Code);
        Assert.Equal(ToolErrorCodes.UiUnavailable, (await tools.SaveAll()).Error!.Code);
    }

    // ---- list_commands --------------------------------------------------------------------------

    [Fact]
    public async Task List_commands_shows_only_runnable_commands()
    {
        var list = (await Tools(new FakeBridge()).ListCommands()).Data!.Commands;

        Assert.Contains(list, c => c.Id == ShellCommandIds.Build && c.Gating == "allowed");
        Assert.Contains(list, c => c.Id == ShellCommandIds.Save && c.Gating == "gated");
        Assert.DoesNotContain(list, c => c.Id == ShellCommandIds.OpenFile);        // excluded
        Assert.DoesNotContain(list, c => c.Id == ShellCommandIds.FormatDocument);  // editor scope
    }

    // ---- settings -------------------------------------------------------------------------------

    [Fact]
    public async Task Get_setting_reads_a_known_key_and_refuses_an_unknown_one()
    {
        var tools = Tools(new FakeBridge());

        Assert.Equal("editor.wordWrap", (await tools.GetSetting("editor.wordWrap")).Data!.Key);
        Assert.Equal(ToolErrorCodes.InvalidArgument, (await tools.GetSetting("editor.fontFamily")).Error!.Code);
    }

    [Fact]
    public async Task Set_setting_refuses_an_unknown_key_before_the_bridge()
    {
        var bridge = new FakeBridge();
        var r = await Tools(bridge).SetSetting("mcp.followAgent", "true");

        Assert.Equal(ToolErrorCodes.InvalidArgument, r.Error!.Code);
        Assert.Null(bridge.SetPair);   // the tool never asked the bridge to set it
    }

    [Fact]
    public async Task Set_setting_maps_a_bad_value_to_invalid_argument()
    {
        var r = await Tools(new FakeBridge()).SetSetting("editor.wordWrap", "bad");
        Assert.Equal(ToolErrorCodes.InvalidArgument, r.Error!.Code);
    }

    [Fact]
    public async Task Set_setting_passes_a_good_value_through()
    {
        var bridge = new FakeBridge();
        var r = await Tools(bridge).SetSetting("editor.wordWrap", "true");

        Assert.True(r.Ok);
        Assert.Equal(("editor.wordWrap", "true"), bridge.SetPair);
    }

    // ---- save / layout --------------------------------------------------------------------------

    [Fact]
    public async Task Save_all_reaches_the_bridge()
    {
        var bridge = new FakeBridge();
        Assert.True((await Tools(bridge).SaveAll()).Ok);
        Assert.True(bridge.SavedAll);
    }

    [Fact]
    public async Task Set_layout_validates_the_preset_name()
    {
        var bridge = new FakeBridge();

        Assert.Equal(ToolErrorCodes.InvalidArgument, (await Tools(bridge).SetLayout("tiled")).Error!.Code);
        Assert.Null(bridge.AppliedLayout);

        Assert.True((await Tools(bridge).SetLayout("Split2")).Ok);   // case-insensitive
        Assert.Equal("split2", bridge.AppliedLayout);
    }

    [Fact]
    public async Task Reset_layout_applies_the_default_preset()
    {
        var bridge = new FakeBridge();
        Assert.True((await Tools(bridge).ResetLayout()).Ok);
        Assert.Equal("default", bridge.AppliedLayout);
    }
}
