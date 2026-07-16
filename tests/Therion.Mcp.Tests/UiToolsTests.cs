// T-03.3: the ring-R3 read tools. Pure tests over a fake IUiBridge — get_ui_state / get_open_documents
// pass the bridge's snapshot through, and return ui_unavailable (not an exception) when there is no
// window to read. The in-app registration is exercised end-to-end in McpHostServiceTests (ThIDE.Tests).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Therion.Mcp;
using Therion.Mcp.Tools;
using Xunit;

namespace Therion.Mcp.Tests;

public class UiToolsTests
{
    private sealed class FakeUiBridge : IUiBridge
    {
        public bool IsAvailable { get; init; }
        public bool FollowAgent { get; init; } = true;
        public UiState? State { get; init; }
        public IReadOnlyList<OpenDocumentInfo> Docs { get; init; } = [];

        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult(State);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() => Task.FromResult(Docs);
        private static Task<UiActionResult> Ok() => Task.FromResult(new UiActionResult(true, ""));
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => Ok();
        public Task<UiActionResult> FocusToolAsync(string toolId) => Ok();
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Ok();
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Ok();
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Ok();
    }

    [Fact]
    public async Task Get_ui_state_is_ui_unavailable_when_no_window()
    {
        var result = await new UiTools(new FakeUiBridge { IsAvailable = false }).GetUiState();
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UiUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task Get_ui_state_passes_the_bridge_snapshot_through()
    {
        var state = new UiState(
            ActiveDocument: "a.th", FocusedDocument: "a.th",
            CaretLine: 12, CaretColumn: 3, SelectionLength: 5,
            VisiblePanes: ["Diagnostics", "Log"], UnsavedDocuments: ["b.th"], FollowAgent: true);

        var result = await new UiTools(new FakeUiBridge { IsAvailable = true, State = state }).GetUiState();

        Assert.True(result.Ok);
        Assert.Equal("a.th", result.Data!.ActiveDocument);
        Assert.Equal(12, result.Data.CaretLine);
        Assert.Equal(3, result.Data.CaretColumn);
        Assert.Contains("Diagnostics", result.Data.VisiblePanes);
        Assert.Contains("b.th", result.Data.UnsavedDocuments);
        Assert.True(result.Data.FollowAgent);
    }

    [Fact]
    public async Task Get_ui_state_is_ui_unavailable_when_the_bridge_returns_null()
    {
        // Available at the check, gone by the time the snapshot is gathered (a race) → clean miss.
        var result = await new UiTools(new FakeUiBridge { IsAvailable = true, State = null }).GetUiState();
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UiUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task Get_open_documents_reports_unavailable_and_lists_when_up()
    {
        var down = await new UiTools(new FakeUiBridge { IsAvailable = false }).GetOpenDocuments();
        Assert.False(down.Ok);
        Assert.Equal(ToolErrorCodes.UiUnavailable, down.Error!.Code);

        var bridge = new FakeUiBridge
        {
            IsAvailable = true,
            Docs = [new OpenDocumentInfo("a.th", true, false), new OpenDocumentInfo("b.th", false, true)],
        };
        var up = await new UiTools(bridge).GetOpenDocuments();

        Assert.True(up.Ok);
        Assert.Equal(2, up.Data!.Total);
        Assert.Contains(up.Data.Documents, d => d.Path == "b.th" && d.Dirty);
        Assert.Contains(up.Data.Documents, d => d.Path == "a.th" && d.Active);
    }
}
