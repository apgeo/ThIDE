// T-03.4: the ring-R3 action tools. Pure tests over fake IUiBridge + IWorkspaceHost — the follow-agent
// gate (ui_control_disabled), the no-window gate (ui_unavailable), path jailing on open_file, and the
// success/ui_action_failed mapping. Driving the real IDE is exercised manually / in host testing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Therion.Mcp;
using Therion.Mcp.Tools;
using Xunit;

namespace Therion.Mcp.Tests;

public class ActionToolsTests
{
    private sealed class FakeBridge : IUiBridge
    {
        public bool IsAvailable { get; init; } = true;
        public bool FollowAgent { get; init; } = true;
        public UiActionResult Result { get; init; } = new(true, "done");
        public string? LastOpenedPath { get; private set; }
        public int? LastLine { get; private set; }

        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
            Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line)
        {
            LastOpenedPath = absolutePath;
            LastLine = line;
            return Task.FromResult(Result);
        }
        public Task<UiActionResult> FocusToolAsync(string toolId) => Task.FromResult(Result);
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Task.FromResult(Result);
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Task.FromResult(Result);
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Task.FromResult(Result);
    }

    private sealed class FakeHost : IWorkspaceHost
    {
        public string? Root { get; init; }
        public bool IsLoaded => Root is not null;
        public string? EntryPointPath => null;
        public ValueTask<WorkspaceSnapshot> GetAsync(CancellationToken ct = default) => throw new WorkspaceNotLoadedException();
        public ValueTask<WorkspaceSnapshot> LoadAsync(string p, CancellationToken ct = default) => throw new WorkspaceNotLoadedException();
        public ValueTask<WorkspaceSnapshot> ReloadAsync(CancellationToken ct = default) => throw new WorkspaceNotLoadedException();
    }

    private static readonly string Root = Path.GetTempPath();

    private static ActionTools Tools(FakeBridge bridge, string? root = null) =>
        new(bridge, new FakeHost { Root = root ?? Root });

    [Fact]
    public async Task Actions_are_ui_unavailable_when_no_window()
    {
        var r = await Tools(new FakeBridge { IsAvailable = false }).ShowToast("hi");
        Assert.False(r.Ok);
        Assert.Equal(ToolErrorCodes.UiUnavailable, r.Error!.Code);
    }

    [Fact]
    public async Task Actions_are_declined_when_follow_agent_is_off()
    {
        var bridge = new FakeBridge { FollowAgent = false };
        var focus = await Tools(bridge).FocusTool("Diagnostics");
        var open = await Tools(bridge).OpenFile("caves/a.th");

        Assert.Equal(ToolErrorCodes.UiControlDisabled, focus.Error!.Code);
        Assert.Equal(ToolErrorCodes.UiControlDisabled, open.Error!.Code);
    }

    [Fact]
    public async Task Open_file_jails_the_path_and_needs_a_workspace()
    {
        var escape = await Tools(new FakeBridge()).OpenFile("../escape.th");
        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, escape.Error!.Code);

        var noWorkspace = await new ActionTools(new FakeBridge(), new FakeHost { Root = null }).OpenFile("a.th");
        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, noWorkspace.Error!.Code);
    }

    [Fact]
    public async Task Open_file_resolves_under_the_root_and_forwards_the_line()
    {
        var bridge = new FakeBridge();
        var r = await Tools(bridge).OpenFile("caves/a.th", 12);

        Assert.True(r.Ok);
        Assert.NotNull(bridge.LastOpenedPath);
        Assert.EndsWith("a.th", bridge.LastOpenedPath);
        Assert.True(Path.IsPathRooted(bridge.LastOpenedPath));   // jailed to an absolute path under root
        Assert.Equal(12, bridge.LastLine);
    }

    [Fact]
    public async Task A_bridge_failure_maps_to_ui_action_failed()
    {
        var bridge = new FakeBridge { Result = new(false, "Unknown tool id 'Nope'.") };
        var r = await Tools(bridge).FocusTool("Nope");

        Assert.False(r.Ok);
        Assert.Equal(ToolErrorCodes.UiActionFailed, r.Error!.Code);
        Assert.Contains("Nope", r.Error.Message);
    }

    [Fact]
    public async Task Show_toast_succeeds_through_the_gate_without_a_workspace()
    {
        var r = await new ActionTools(new FakeBridge(), new FakeHost { Root = null }).ShowToast("done", "success");
        Assert.True(r.Ok);
        Assert.Equal("done", r.Data!.Message);
    }
}
