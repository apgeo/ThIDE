// T-07.3: the Assistant pane's view-model against a scripted assistant service — no model, no
// server, no Avalonia dispatcher (the VM's UiMarshalOverride test seam runs marshals inline).
// Covers the transcript flow, the Allow/Deny approval round-trip, Stop, and the two
// server-unavailable affordances.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Assistant;
using Therion.Mcp;
using ThIDE.Services;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class AssistantViewModelTests
{
    private sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler? Changed;
        public void Save(AppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeHost : IMcpHostService
    {
        public bool IsListening => Endpoint is not null;
        public int? Port => Endpoint?.Port;
        public McpEndpoint? Endpoint { get; set; }
        public event EventHandler? StateChanged { add { } remove { } }
        public int ApplyCalls { get; private set; }
        public McpEndpoint? EndpointAfterApply { get; set; }

        public Task ApplySettingAsync(CancellationToken ct = default)
        {
            ApplyCalls++;
            Endpoint = EndpointAfterApply;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;
        public void RequestShutdown() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAssistant(
        Func<string, ChatCallbacks, CancellationToken, Task<ChatResult>> onSend) : IAssistantService
    {
        public int NewConversationCalls { get; private set; }
        public IReadOnlyList<ConversationTurn> Restored { get; init; } = Array.Empty<ConversationTurn>();
        public string ModelLabel => "test-model @ http://test/v1";
        public Task<ChatResult> SendAsync(string userMessage, ChatCallbacks callbacks, CancellationToken ct) =>
            onSend(userMessage, callbacks, ct);
        public void NewConversation() => NewConversationCalls++;
        public IReadOnlyList<ConversationTurn> RestoredConversation() => Restored;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSnippetEditor : ISnippetEditor
    {
        public SnippetOutcome InsertResult { get; set; } = SnippetOutcome.Applied;
        public SnippetOutcome ReplaceResult { get; set; } = SnippetOutcome.Applied;
        public string? InsertedText { get; private set; }
        public string? ReplacedText { get; private set; }
        public SnippetOutcome InsertAtActiveCaret(string text) { InsertedText = text; return InsertResult; }
        public SnippetOutcome ReplaceActiveSelection(string text) { ReplacedText = text; return ReplaceResult; }
    }

    /// <summary>A scripted assistant that finalizes a single answer (used by the code-block tests).</summary>
    private static FakeAssistant AnswerOf(string text) => new((_, callbacks, _) =>
    {
        callbacks.OnUpdate!(new AssistantAnswered(text));
        return Task.FromResult(new ChatResult(text, Array.Empty<ChatToolCall>(), 0, 0));
    });

    private static AssistantViewModel NewVm(
        IAssistantService assistant, FakeSettings? settings = null, FakeHost? host = null, ISnippetEditor? snippets = null)
    {
        var vm = new AssistantViewModel(assistant, settings ?? new FakeSettings(), host ?? new FakeHost(), snippets: snippets)
        {
            UiMarshalOverride = a => a(),
        };
        return vm;
    }

    [Fact]
    public async Task Send_RendersUser_ToolCard_AndAnswer()
    {
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("survey_stats", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, """{"ok":true}"""));
            callbacks.OnUpdate!(new AssistantAnswered("Four stations."));
            return Task.FromResult(new ChatResult("Four stations.",
                [new ChatToolCall("survey_stats", true, true)], 1, 321));
        });
        var vm = NewVm(assistant);
        vm.Input = "how many stations?";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Collection(vm.Items,
            i => Assert.Equal("how many stations?", Assert.IsType<UserChatItem>(i).Text),
            i =>
            {
                var card = Assert.IsType<ToolCallChatItem>(i);
                Assert.Equal("survey_stats", card.Tool);
                Assert.Equal(ToolCallState.Ok, card.State);
                Assert.Equal("""{"ok":true}""", card.ResultPreview);
            },
            i => Assert.Equal("Four stations.", Assert.IsType<AssistantChatItem>(i).Text));
        Assert.False(vm.IsBusy);
        Assert.Contains("321", vm.Status);
        Assert.Equal(string.Empty, vm.Input);
    }

    [Fact]
    public async Task ToolResult_WithSymbolList_RendersClickableObjects()
    {
        const string result = """
            {"ok":true,"data":{"symbols":[
              {"kind":"station","name":"cave.upper.1","declaration":{"file":"date/x.th","line":12,"column":5,"endLine":12,"endColumn":27},"detail":"shot"},
              {"kind":"survey","name":"cave.upper","declaration":{"file":"date/x.th","line":1,"column":1,"endLine":1,"endColumn":10},"detail":"Upper level"},
              {"kind":"map","name":"overview","declaration":{"file":"maps/m.th","line":3,"column":1,"endLine":3,"endColumn":8}}
            ]}}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("list_symbols", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            callbacks.OnUpdate!(new AssistantAnswered("Here they are."));
            return Task.FromResult(new ChatResult("Here they are.",
                [new ChatToolCall("list_symbols", true, true)], 1, 10));
        });
        var vm = NewVm(assistant);
        vm.Input = "list the surveys";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.HasSymbols);
        Assert.Equal(3, card.Symbols.Count);
        // Bare leaf name up front; the survey prefix (or file, for a parentless name) in the subtitle.
        Assert.Equal("1", card.Symbols[0].Name);
        Assert.Equal("cave.upper.1", card.Symbols[0].QualifiedName);
        Assert.Equal("station · cave.upper", card.Symbols[0].Subtitle);
        // The bare map name has no parent survey, so its file stands in.
        Assert.Equal("overview", card.Symbols[2].Name);
        Assert.Equal("map · maps/m.th", card.Symbols[2].Subtitle);
    }

    [Fact]
    public async Task ToolResult_WithStationList_RendersClickableObjects()
    {
        // list_stations has its own DTO shape (stations[], nested declaration) — the generic reader
        // must light it up just like list_symbols, which is the panel bug this covers.
        const string result = """
            {"ok":true,"data":{"stations":[
              {"name":"GrindDrumAcoperisVerificare.71","kind":"shot","flags":[],"declaration":{"file":"date/G_drumuiri/x.th","line":367,"column":5,"endLine":367,"endColumn":29}},
              {"name":"grind.G0","kind":"fix","flags":[],"declaration":{"file":"grind.th","line":169,"column":2,"endLine":169,"endColumn":48}}
            ],"total":2,"offset":0,"truncated":false}}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("list_stations", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            return Task.FromResult(new ChatResult("2 stations.", [new ChatToolCall("list_stations", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "list stations";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.HasSymbols);
        Assert.Equal(2, card.Symbols.Count);
        Assert.Equal("71", card.Symbols[0].Name);
        Assert.Equal("shot · GrindDrumAcoperisVerificare", card.Symbols[0].Subtitle);
        Assert.Equal("G0", card.Symbols[1].Name);
        Assert.Equal("fix · grind", card.Symbols[1].Subtitle);
    }

    [Fact]
    public async Task ToolResult_WithDiagnostics_RendersProseRowsWithFlatLocation()
    {
        // get_diagnostics carries its location flat (file/line/column on the item) and its label is
        // prose (a message) — shown whole with a severity·file:line subtitle, not dot-split.
        const string result = """
            {"ok":true,"data":{"diagnostics":[
              {"code":"TH0123","severity":"error","message":"Station a.b is not connected.","file":"date/x.th","line":5,"column":3,"hint":"equate it"}
            ],"total":1,"offset":0,"truncated":false,"errors":1,"warnings":0}}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("get_diagnostics", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            return Task.FromResult(new ChatResult("1 problem.", [new ChatToolCall("get_diagnostics", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "any errors?";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.HasSymbols);
        var row = Assert.Single(card.Symbols);
        Assert.Equal("Station a.b is not connected.", row.Name);   // prose, not split on the dot
        Assert.Equal("error · date/x.th:5", row.Subtitle);
    }

    [Fact]
    public async Task ToolResult_WithReferenceList_RendersNavigableOccurrences()
    {
        // find_references: one object at many places — declaration, a plain reference, an equate.
        const string result = """
            {"ok":true,"data":{
              "name":"cave.upper.1",
              "definition":{"file":"date/x.th","line":12,"column":5,"endLine":12,"endColumn":27},
              "references":[
                {"location":{"file":"date/x.th","line":12,"column":5,"endLine":12,"endColumn":27},"isDeclaration":true},
                {"location":{"file":"date/y.th","line":40,"column":9,"endLine":40,"endColumn":11},"isDeclaration":false}
              ],
              "aggregations":[{"kind":"equate","location":{"file":"date/z.th","line":3,"column":1,"endLine":3,"endColumn":20}}]
            }}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("find_references", """{"name":"1"}""", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            return Task.FromResult(new ChatResult("Three.", [new ChatToolCall("find_references", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "where is 1 used?";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.HasSymbols);
        Assert.Equal(3, card.Symbols.Count);
        // Declaration first, then the plain reference, then the equate aggregation — each row is the
        // same object, distinguished by its role and file:line.
        Assert.Equal("1", card.Symbols[0].Name);
        Assert.Equal("declaration · date/x.th:12", card.Symbols[0].Subtitle);
        Assert.Equal("reference · date/y.th:40", card.Symbols[1].Subtitle);
        Assert.Equal("equate · date/z.th:3", card.Symbols[2].Subtitle);
    }

    [Fact]
    public async Task ToolResult_WithFilePaths_RendersClickableFileRows()
    {
        // list_files returns a plain string[] of workspace-relative paths (not objects) — each row
        // should open the file, showing the file name up front and its folder as the subtitle.
        const string result = """
            {"ok":true,"data":{"files":["date/G_drumuiri/x.th","thconfig_grind.thconfig"],"total":2,"offset":0,"truncated":false}}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("list_files", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            return Task.FromResult(new ChatResult("2 files.", [new ChatToolCall("list_files", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "list files";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.HasSymbols);
        Assert.Equal(2, card.Symbols.Count);
        Assert.Equal("x.th", card.Symbols[0].Name);
        Assert.Equal("file · date/G_drumuiri", card.Symbols[0].Subtitle);
        Assert.Equal("date/G_drumuiri/x.th", card.Symbols[0].QualifiedName);   // full path in the tooltip
        // A root-level file has no folder, so the subtitle is just the kind.
        Assert.Equal("thconfig_grind.thconfig", card.Symbols[1].Name);
        Assert.Equal("file", card.Symbols[1].Subtitle);
    }

    [Fact]
    public async Task ToolResult_WithoutSymbolList_HasNoObjects()
    {
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("survey_stats", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, """{"ok":true,"data":{"stations":4}}"""));
            return Task.FromResult(new ChatResult("done", [new ChatToolCall("survey_stats", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "stats";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.False(card.HasSymbols);
        Assert.Empty(card.Symbols);
    }

    [Fact]
    public async Task ReadOnlyToolCard_StartsCollapsed_WithAnObjectCountSummary()
    {
        const string result = """
            {"ok":true,"data":{"stations":[
              {"name":"a.1","kind":"shot","declaration":{"file":"x.th","line":1,"column":1,"endLine":1,"endColumn":3}},
              {"name":"a.2","kind":"shot","declaration":{"file":"x.th","line":2,"column":1,"endLine":2,"endColumn":3}}
            ]}}
            """;
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            var info = new ToolCallInfo("list_stations", "{}", ReadOnly: true);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, result));
            return Task.FromResult(new ChatResult("2 stations.", [new ChatToolCall("list_stations", true, true)], 1, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "list";

        await vm.SendCommand.ExecuteAsync(null);

        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.ReadOnly);
        Assert.False(card.IsExpanded);                       // read-only lookups fold away
        Assert.Equal("2 objects — click to open", card.CollapsedSummary);
    }

    [Fact]
    public async Task WritingToolCard_StartsExpanded_AndApprovalKeepsItOpen()
    {
        var assistant = new FakeAssistant(async (_, callbacks, ct) =>
        {
            var info = new ToolCallInfo("edit_file", """{"path":"a.th"}""", ReadOnly: false);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            await callbacks.ApproveAsync!(info, ct);
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, "edited"));
            return new ChatResult("Done.", [new ChatToolCall("edit_file", true, true)], 1, 0);
        });
        var vm = NewVm(assistant);
        vm.Input = "edit";

        var send = vm.SendCommand.ExecuteAsync(null);
        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.False(card.ReadOnly);
        Assert.True(card.IsExpanded);   // writing tools stay open (preview + Allow/Deny must show)
        card.AllowCommand.Execute(null);
        await send;
        Assert.True(card.IsExpanded);
    }

    [Fact]
    public async Task Streaming_GrowsOneBubbleInPlace_ThenFinalizes()
    {
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            callbacks.OnUpdate!(new AssistantDelta("Hel", "Hel"));
            callbacks.OnUpdate!(new AssistantDelta("lo.", "Hello."));
            callbacks.OnUpdate!(new AssistantAnswered("Hello."));
            return Task.FromResult(new ChatResult("Hello.", [], 0, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "hi";

        await vm.SendCommand.ExecuteAsync(null);

        // Exactly one assistant bubble, carrying the assembled text — deltas grew it in place.
        var bubble = Assert.Single(vm.Items.OfType<AssistantChatItem>());
        Assert.Equal("Hello.", bubble.Text);
        Assert.Empty(vm.Activity);   // cleared when the turn ends
    }

    [Fact]
    public async Task EmptyAnswer_RendersAPlaceholderNote_NotABlankBubble()
    {
        var assistant = new FakeAssistant((_, callbacks, _) =>
        {
            callbacks.OnUpdate!(new AssistantAnswered("   "));   // whitespace-only "answer"
            return Task.FromResult(new ChatResult("", [], 0, 0));
        });
        var vm = NewVm(assistant);
        vm.Input = "hi";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Items, i => i is AssistantChatItem);
        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public async Task Send_WhenServerDisabled_ShowsTheEnableAffordance()
    {
        var assistant = new FakeAssistant((_, _, _) =>
            throw new AssistantUnavailableException(AssistantUnavailableReason.ServerDisabled));
        var vm = NewVm(assistant);
        vm.Input = "hi";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.ServerOff);
        Assert.Contains(vm.Items, i => i is NoteChatItem);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Stop_CancelsTheTurn_AndNotes()
    {
        var assistant = new FakeAssistant(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var vm = NewVm(assistant);
        vm.Input = "long question";

        var send = vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        vm.StopCommand.Execute(null);
        await send;

        Assert.False(vm.IsBusy);
        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public async Task Approval_DenyDeclinesTheCall()
    {
        bool? decision = null;
        var assistant = new FakeAssistant(async (_, callbacks, ct) =>
        {
            var info = new ToolCallInfo("edit_file", """{"path":"a.th"}""", ReadOnly: false);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            decision = await callbacks.ApproveAsync!(info, ct);
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: false, "The user declined this action."));
            callbacks.OnUpdate!(new AssistantAnswered("Understood, not changing anything."));
            return new ChatResult("Understood, not changing anything.",
                [new ChatToolCall("edit_file", true, false, Declined: true)], 1, 0);
        });
        var vm = NewVm(assistant);
        vm.Input = "fix the file";

        var send = vm.SendCommand.ExecuteAsync(null);
        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        Assert.True(card.IsWaitingApproval);

        card.DenyCommand.Execute(null);
        await send;

        Assert.False(decision!.Value);
        Assert.Equal(ToolCallState.Declined, card.State);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Approval_AllowRunsTheCall()
    {
        bool? decision = null;
        var assistant = new FakeAssistant(async (_, callbacks, ct) =>
        {
            var info = new ToolCallInfo("edit_file", "{}", ReadOnly: false);
            callbacks.OnUpdate!(new ToolCallStarted(info));
            decision = await callbacks.ApproveAsync!(info, ct);
            callbacks.OnUpdate!(new ToolCallFinished(info, Ok: true, "edited"));
            return new ChatResult("Done.", [new ChatToolCall("edit_file", true, true)], 1, 0);
        });
        var vm = NewVm(assistant);
        vm.Input = "fix it";

        var send = vm.SendCommand.ExecuteAsync(null);
        var card = vm.Items.OfType<ToolCallChatItem>().Single();
        card.AllowCommand.Execute(null);
        await send;

        Assert.True(decision!.Value);
        Assert.Equal(ToolCallState.Ok, card.State);
    }

    [Fact]
    public async Task EnableServer_FlipsTheSetting_AndApplies()
    {
        var settings = new FakeSettings();
        var host = new FakeHost
        {
            EndpointAfterApply = new McpEndpoint(1234, "tok", 1, "now", "http://127.0.0.1:1234/"),
        };
        var vm = NewVm(new FakeAssistant((_, _, _) => Task.FromResult(new ChatResult("", [], 0, 0))),
            settings, host);
        Assert.True(vm.ServerOff); // EnableMcpServer defaults to off

        await vm.EnableServerCommand.ExecuteAsync(null);

        Assert.True(settings.Current.EnableMcpServer);
        Assert.Equal(1, host.ApplyCalls);
        Assert.False(vm.ServerOff);
    }

    [Fact]
    public void RestoredConversation_RedrawsTheTranscript_OnConstruction()
    {
        var assistant = new FakeAssistant((_, _, _) => Task.FromResult(new ChatResult("", [], 0, 0)))
        {
            Restored =
            [
                new ConversationTurn(IsUser: true, "how many stations?"),
                new ConversationTurn(IsUser: false, "There are 4."),
            ],
        };
        var vm = NewVm(assistant);

        Assert.Collection(vm.Items,
            i => Assert.Equal("how many stations?", Assert.IsType<UserChatItem>(i).Text),
            i => Assert.Equal("There are 4.", Assert.IsType<AssistantChatItem>(i).Text));
    }

    [Fact]
    public async Task InputHistory_UpAndDown_RecallSentMessages()
    {
        var assistant = new FakeAssistant((_, _, _) => Task.FromResult(new ChatResult("ok", [], 0, 0)));
        var vm = NewVm(assistant);

        vm.Input = "first"; await vm.SendCommand.ExecuteAsync(null);
        vm.Input = "second"; await vm.SendCommand.ExecuteAsync(null);

        // Compose a draft, then walk back through history and forward again to the draft.
        vm.Input = "draft";
        Assert.True(vm.RecallPreviousInput());  Assert.Equal("second", vm.Input);
        Assert.True(vm.RecallPreviousInput());  Assert.Equal("first", vm.Input);
        Assert.False(vm.RecallPreviousInput()); Assert.Equal("first", vm.Input);   // nothing older
        Assert.True(vm.RecallNextInput());      Assert.Equal("second", vm.Input);
        Assert.True(vm.RecallNextInput());      Assert.Equal("draft", vm.Input);   // back to the draft
        Assert.False(vm.RecallNextInput());     Assert.Equal("draft", vm.Input);   // nothing newer
    }

    [Fact]
    public void InputHistory_SeededFromRestoredUserMessages()
    {
        var assistant = new FakeAssistant((_, _, _) => Task.FromResult(new ChatResult("", [], 0, 0)))
        {
            Restored = [new ConversationTurn(IsUser: true, "old question"), new ConversationTurn(IsUser: false, "old answer")],
        };
        var vm = NewVm(assistant);

        Assert.True(vm.RecallPreviousInput());
        Assert.Equal("old question", vm.Input);   // the assistant answer is not part of input history
    }

    // ---- code-block actions (CAP-03) --------------------------------------------------------

    [Fact]
    public async Task Answer_WithFencedCode_RendersProseBubbleThenCodeCard()
    {
        var vm = NewVm(AnswerOf("Add this survey:\n```therion\nsurvey a\nendsurvey\n```"));
        vm.Input = "make a survey";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("Add this survey:", Assert.Single(vm.Items.OfType<AssistantChatItem>()).Text);
        var code = Assert.Single(vm.Items.OfType<CodeBlockChatItem>());
        Assert.Equal("therion", code.Language);
        Assert.Equal("survey a\nendsurvey", code.Code);
    }

    [Fact]
    public async Task CodeCard_Insert_AppliesThroughTheEditor_WithNoNote()
    {
        var snippets = new FakeSnippetEditor();
        var vm = NewVm(AnswerOf("```\nx 1 2\n```"), snippets: snippets);
        vm.Input = "x"; await vm.SendCommand.ExecuteAsync(null);

        vm.Items.OfType<CodeBlockChatItem>().Single().InsertCommand.Execute(null);

        Assert.Equal("x 1 2", snippets.InsertedText);
        Assert.DoesNotContain(vm.Items, i => i is NoteChatItem);   // Applied → nothing to say
    }

    [Fact]
    public async Task CodeCard_Insert_WithNoEditor_PostsANote()
    {
        var snippets = new FakeSnippetEditor { InsertResult = SnippetOutcome.NoEditor };
        var vm = NewVm(AnswerOf("```\nx\n```"), snippets: snippets);
        vm.Input = "x"; await vm.SendCommand.ExecuteAsync(null);

        vm.Items.OfType<CodeBlockChatItem>().Single().InsertCommand.Execute(null);

        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public async Task CodeCard_Insert_WithNoSnippetService_PostsANote()
    {
        var vm = NewVm(AnswerOf("```\nz\n```"));   // snippets null (headless VM without an editor seam)
        vm.Input = "z"; await vm.SendCommand.ExecuteAsync(null);

        vm.Items.OfType<CodeBlockChatItem>().Single().InsertCommand.Execute(null);

        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public async Task CodeCard_Replace_WithNoSelection_PostsANote()
    {
        var snippets = new FakeSnippetEditor { ReplaceResult = SnippetOutcome.NoSelection };
        var vm = NewVm(AnswerOf("```\ny\n```"), snippets: snippets);
        vm.Input = "y"; await vm.SendCommand.ExecuteAsync(null);

        vm.Items.OfType<CodeBlockChatItem>().Single().ReplaceCommand.Execute(null);

        Assert.Equal("y", snippets.ReplacedText);
        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public async Task CodeCard_Copy_PostsACopiedNote()
    {
        var vm = NewVm(AnswerOf("```\nc\n```"));
        vm.Input = "c"; await vm.SendCommand.ExecuteAsync(null);

        vm.Items.OfType<CodeBlockChatItem>().Single().CopyCommand.Execute(null);   // clipboard is a no-op headless

        Assert.Contains(vm.Items, i => i is NoteChatItem);
    }

    [Fact]
    public void NewChat_ClearsTheTranscript_AndTheConversation()
    {
        var assistant = new FakeAssistant((_, _, _) => Task.FromResult(new ChatResult("", [], 0, 0)));
        var vm = NewVm(assistant);
        vm.Items.Add(new UserChatItem("old"));

        vm.NewChatCommand.Execute(null);

        Assert.Empty(vm.Items);
        Assert.Equal(1, assistant.NewConversationCalls);
    }
}
