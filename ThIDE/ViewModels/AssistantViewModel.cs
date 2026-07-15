// The Assistant pane's content VM (AI-07, D-043): a transcript of user/assistant/tool items, an
// input box, Stop, and the host-side Allow/Deny gate for tools that write. The chat loop itself
// lives in Therion.Assistant; this VM only renders its callbacks and owns the cancellation token.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Assistant;
using Therion.Core;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public enum ToolCallState { Running, WaitingApproval, Ok, Failed, Declined }

public abstract class ChatItemViewModel : ObservableObject;

public sealed class UserChatItem(string text) : ChatItemViewModel
{
    public string Text { get; } = text;
}

public sealed partial class AssistantChatItem : ChatItemViewModel
{
    public AssistantChatItem(string text) => _text = text;

    /// <summary>Mutable so a streamed answer can grow in place as deltas arrive (AI-08.2).</summary>
    [ObservableProperty] private string _text;
}

/// <summary>A grey status/error line in the transcript (stop notices, endpoint failures).</summary>
public sealed class NoteChatItem(string text) : ChatItemViewModel
{
    public string Text { get; } = text;
}

/// <summary>One tool call: name + arguments, its state, and — while the engine is paused on the
/// approval hook — the Allow/Deny buttons that complete <see cref="Decision"/>. Read-only calls
/// collapse to a one-line summary so the prose answer leads the transcript (AI-08.3).</summary>
public sealed partial class ToolCallChatItem : ChatItemViewModel
{
    public ToolCallChatItem(string tool, string argumentsJson, bool readOnly = false)
    {
        Tool = tool;
        ArgumentsJson = argumentsJson;
        ReadOnly = readOnly;
        // Writing tools (edit_file …) stay open — their preview and the Allow/Deny gate must be seen;
        // read-only lookups start collapsed to a single line and expand on click.
        _isExpanded = !readOnly;
    }

    public string Tool { get; }
    public string ArgumentsJson { get; }

    /// <summary>Read-only lookups collapse by default; writing tools do not.</summary>
    public bool ReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingApproval))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CollapsedSummary))]
    private ToolCallState _state = ToolCallState.Running;

    [ObservableProperty] private string _resultPreview = string.Empty;

    /// <summary>Whether the card's details (arguments, object list, raw JSON) are shown (AI-08.3).</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Navigable objects parsed out of the result (list_symbols &amp; friends). Empty for
    /// tool results that carry no symbol list — those show only the raw <see cref="ResultPreview"/>.</summary>
    public ObservableCollection<NavigableSymbolViewModel> Symbols { get; } = [];

    public bool HasSymbols => Symbols.Count > 0;

    /// <summary>A one-line "N objects" header for the list; empty when there are none.</summary>
    public string SymbolsHeader =>
        HasSymbols ? string.Format(Tr.Get("Assistant_Objects"), Symbols.Count) : string.Empty;

    /// <summary>The one-liner shown when collapsed: the object count if there is a list, else the state.</summary>
    public string CollapsedSummary => HasSymbols
        ? string.Format(Tr.Get("Assistant_Objects"), Symbols.Count)
        : State switch
        {
            ToolCallState.Ok => Tr.Get("Assistant_ToolOk"),
            ToolCallState.Failed => Tr.Get("Assistant_ToolFailed"),
            ToolCallState.Declined => Tr.Get("Assistant_ToolDeclined"),
            _ => Tr.Get("Assistant_ToolRunning"),
        };

    /// <summary>Fills the navigable-object list (raises the derived flags). Called once, on finish.</summary>
    public void SetSymbols(IEnumerable<NavigableSymbolViewModel> symbols)
    {
        Symbols.Clear();
        foreach (var s in symbols) Symbols.Add(s);
        OnPropertyChanged(nameof(HasSymbols));
        OnPropertyChanged(nameof(SymbolsHeader));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public bool IsWaitingApproval => State == ToolCallState.WaitingApproval;
    public bool IsRunning => State == ToolCallState.Running;

    // The Allow/Deny gate must be visible, so always open the card when it pauses for approval.
    partial void OnStateChanged(ToolCallState value)
    {
        if (value == ToolCallState.WaitingApproval) IsExpanded = true;
    }

    internal TaskCompletionSource<bool>? Decision { get; set; }

    [RelayCommand] private void ToggleExpand() => IsExpanded = !IsExpanded;
    [RelayCommand] private void Allow() => Decision?.TrySetResult(true);
    [RelayCommand] private void Deny() => Decision?.TrySetResult(false);
}

/// <summary>
/// A clickable object in a tool-result list: its display text and the command that opens its
/// declaration. <see cref="Kind"/> + <see cref="QualifiedName"/> keep the semantic identity so a
/// later feature (reveal in 3D, add to a set…) can resolve the object without re-parsing.
/// </summary>
public sealed partial class NavigableSymbolViewModel : ObservableObject
{
    private readonly NavigableSymbol _model;
    private readonly Func<NavigableSymbol, Task> _navigate;

    public NavigableSymbolViewModel(NavigableSymbol model, Func<NavigableSymbol, Task> navigate)
    {
        _model = model;
        _navigate = navigate;
    }

    /// <summary>The row's primary text: the bare object name for a qualified symbol/station (the
    /// survey prefix moves to <see cref="Subtitle"/>), or the whole prose label — truncated — for a
    /// TODO/diagnostic.</summary>
    public string Name => _model.FreeText ? Truncate(_model.Name, 100) : _model.Leaf;

    /// <summary>
    /// For an object list: kind plus the parent survey (or the declaring file when the name has no
    /// parent). For a prose entry (TODO/diagnostic): kind plus the file:line. For an occurrence list
    /// (find_references): the role — declaration / reference / equate / map — plus that file:line.
    /// </summary>
    public string Subtitle =>
        _model.Role is { } role ? $"{role} · {_model.RelativeFile}:{_model.Line}"
        : _model.FreeText ? $"{_model.Kind} · {_model.RelativeFile}:{_model.Line}"
        : $"{_model.Kind} · {_model.Parent ?? _model.RelativeFile}";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>The semantic kind (station, survey, map…), kept for later resolution.</summary>
    public string Kind => _model.Kind;

    /// <summary>The fully-qualified name — the handle back into the semantic model.</summary>
    public string QualifiedName => _model.Name;

    [RelayCommand]
    private Task Navigate() => _navigate(_model);
}

public sealed partial class AssistantViewModel : ObservableObject
{
    /// <summary>Longest tool-result excerpt kept in the transcript — the model sees it all; the
    /// card is a receipt, not a data grid.</summary>
    private const int ResultPreviewChars = 1500;

    private readonly IAssistantService _assistant;
    private readonly IAppSettingsService _settings;
    private readonly IMcpHostService _host;
    // Optional so the headless VM tests can construct the pane without the whole doc/workspace graph;
    // when absent, symbol lists still render but clicking them is a no-op.
    private readonly IDocumentService? _documents;
    private readonly IWorkspaceSession? _session;

    // The engine hands the SAME ToolCallInfo instance to Started/Approve/Finished — reference
    // identity is the correlation key (value equality would collide on two identical calls).
    private readonly Dictionary<ToolCallInfo, ToolCallChatItem> _cards = new(ReferenceEqualityComparer.Instance);

    private CancellationTokenSource? _cts;

    public ObservableCollection<ChatItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _input = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isBusy;

    /// <summary>True shows the "enable the in-app tools server" affordance instead of the input.</summary>
    [ObservableProperty] private bool _serverOff;

    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Live one-line progress while a turn runs ("Thinking…", "Running list_stations…",
    /// "Writing the answer…"); empty when idle (AI-08.2).</summary>
    [ObservableProperty] private string _activity = string.Empty;

    // The assistant bubble currently being streamed into, or null between turns (AI-08.2).
    private AssistantChatItem? _streaming;

    public AssistantViewModel(
        IAssistantService assistant,
        IAppSettingsService settings,
        IMcpHostService host,
        IDocumentService? documents = null,
        IWorkspaceSession? session = null)
    {
        _assistant = assistant;
        _settings = settings;
        _host = host;
        _documents = documents;
        _session = session;
        _serverOff = !settings.Current.EnableMcpServer;
        _status = assistant.ModelLabel;
    }

    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input);
    private bool CanStop() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        Input = string.Empty;
        IsBusy = true;
        Activity = Tr.Get("Assistant_Thinking");
        _streaming = null;
        Items.Add(new UserChatItem(text));
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _assistant.SendAsync(text, new ChatCallbacks
            {
                ApproveAsync = ApproveAsync,
                OnUpdate = OnUpdate,
            }, _cts.Token);
            Status = $"{_assistant.ModelLabel} · {result.Tokens} tokens";
        }
        catch (AssistantUnavailableException ex)
        {
            if (ex.Reason == AssistantUnavailableReason.ServerDisabled)
            {
                ServerOff = true;
                Items.Add(new NoteChatItem(Tr.Get("Assistant_ServerOff")));
            }
            else
            {
                Items.Add(new NoteChatItem(Tr.Get("Assistant_ServerDown")));
            }
        }
        catch (OperationCanceledException)
        {
            Items.Add(new NoteChatItem(Tr.Get("Assistant_Stopped")));
        }
        catch (Exception ex)
        {
            Items.Add(new NoteChatItem(string.Format(Tr.Get("Assistant_Error"), ex.Message)));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _cards.Clear();
            _streaming = null;
            Activity = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    [RelayCommand]
    private void NewChat()
    {
        _assistant.NewConversation();
        Items.Clear();
        _cards.Clear();
    }

    /// <summary>The "server is off" affordance: flip the setting, start the listener, let the user resend.</summary>
    [RelayCommand]
    private async Task EnableServerAsync()
    {
        _settings.Save(_settings.Current with { EnableMcpServer = true });
        try
        {
            await _host.ApplySettingAsync();
            ServerOff = _host.Endpoint is null;
            if (!ServerOff) Items.Add(new NoteChatItem(Tr.Get("Assistant_ServerEnabled")));
            else Items.Add(new NoteChatItem(Tr.Get("Assistant_ServerDown")));
        }
        catch (Exception)
        {
            Items.Add(new NoteChatItem(Tr.Get("Assistant_ServerDown")));
        }
    }

    // ---- navigable object lists (list_symbols &c.) -------------------------------------------

    private IEnumerable<NavigableSymbolViewModel> BuildSymbolItems(IReadOnlyList<NavigableSymbol> symbols)
    {
        foreach (var s in symbols)
            yield return new NavigableSymbolViewModel(s, NavigateToSymbolAsync);
    }

    /// <summary>
    /// Opens a listed object's declaration in the editor. The tool reports files workspace-relative
    /// with forward slashes (see Location), so we rejoin them onto the live root the MCP host serves
    /// from. Runs on the UI thread (invoked from the item's click command).
    /// </summary>
    private async Task NavigateToSymbolAsync(NavigableSymbol symbol)
    {
        if (_documents is null || _session?.RootPath is not { Length: > 0 } root) return;
        try
        {
            var absolute = Path.GetFullPath(
                Path.Combine(root, symbol.RelativeFile.Replace('/', Path.DirectorySeparatorChar)));
            int endLine = symbol.EndLine >= symbol.Line ? symbol.EndLine : symbol.Line;
            var span = new SourceSpan(
                absolute,
                new SourceLocation(symbol.Line, symbol.Column),
                new SourceLocation(endLine, symbol.EndColumn),
                StartOffset: 0,
                Length: 1);   // non-empty so NavigateToSpanAsync/ScrollTo act; they recompute from line/col
            await _documents.NavigateToSpanAsync(span);
        }
        catch (Exception ex)
        {
            Items.Add(new NoteChatItem(string.Format(Tr.Get("Assistant_Error"), ex.Message)));
        }
    }

    // ---- engine callbacks (arrive on thread-pool threads) ------------------------------------

    private void OnUpdate(ChatUpdate update) => OnUi(() =>
    {
        switch (update)
        {
            case ToolCallStarted started:
                Activity = string.Format(Tr.Get("Assistant_RunningTool"), started.Call.Tool);
                var card = new ToolCallChatItem(started.Call.Tool, started.Call.ArgumentsJson, started.Call.ReadOnly);
                _cards[started.Call] = card;
                Items.Add(card);
                break;

            case ToolCallFinished finished:
                // Screening misses (unknown tool, bad JSON) fire Finished without Started.
                if (!_cards.TryGetValue(finished.Call, out var existing))
                {
                    existing = new ToolCallChatItem(finished.Call.Tool, finished.Call.ArgumentsJson, finished.Call.ReadOnly);
                    _cards[finished.Call] = existing;
                    Items.Add(existing);
                }
                existing.State = finished.Content == "The user declined this action."
                    ? ToolCallState.Declined
                    : finished.Ok ? ToolCallState.Ok : ToolCallState.Failed;
                existing.ResultPreview = finished.Content.Length <= ResultPreviewChars
                    ? finished.Content
                    : finished.Content[..ResultPreviewChars] + "…";
                // A result that lists project objects (list_symbols &c.) renders as a clickable,
                // go-to-definition list; anything else parses to nothing and shows only the JSON.
                var symbols = AssistantSymbolList.Parse(finished.Content);
                if (symbols.Count > 0)
                    existing.SetSymbols(BuildSymbolItems(symbols));
                break;

            case AssistantDelta delta:
                // Grow the streamed bubble in place, creating it on the first chunk (AI-08.2).
                Activity = Tr.Get("Assistant_Writing");
                if (_streaming is null)
                {
                    _streaming = new AssistantChatItem(delta.Text);
                    Items.Add(_streaming);
                }
                else
                {
                    _streaming.Text = delta.Text;
                }
                break;

            case AssistantAnswered answered:
                // Finalize the streamed bubble if we have one; otherwise add the answer whole. A mute
                // model (empty content, no synthesis) must never leave a blank bubble (AI-08.1).
                if (_streaming is { } streaming)
                {
                    if (string.IsNullOrWhiteSpace(answered.Text))
                    {
                        Items.Remove(streaming);
                        Items.Add(new NoteChatItem(Tr.Get("Assistant_NoText")));
                    }
                    else
                    {
                        streaming.Text = answered.Text;
                    }
                    _streaming = null;
                }
                else if (string.IsNullOrWhiteSpace(answered.Text))
                {
                    Items.Add(new NoteChatItem(Tr.Get("Assistant_NoText")));
                }
                else
                {
                    Items.Add(new AssistantChatItem(answered.Text));
                }
                break;
        }
    });

    /// <summary>The engine's approval pause: surface Allow/Deny on the tool card and await the click.</summary>
    private async Task<bool> ApproveAsync(ToolCallInfo call, CancellationToken ct)
    {
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            // The Started update was queued before this, so the card exists on the UI thread.
            if (!_cards.TryGetValue(call, out var card))
            {
                card = new ToolCallChatItem(call.Tool, call.ArgumentsJson, call.ReadOnly);
                _cards[call] = card;
                Items.Add(card);
            }
            card.Decision = decision;
            card.State = ToolCallState.WaitingApproval;
        });

        using (ct.Register(() => decision.TrySetCanceled(ct)))
        {
            var allowed = await decision.Task.ConfigureAwait(false);
            OnUi(() =>
            {
                if (_cards.TryGetValue(call, out var card) && card.State == ToolCallState.WaitingApproval)
                    card.State = ToolCallState.Running;
            });
            return allowed;
        }
    }

    /// <summary>Test seam (InternalsVisibleTo ThIDE.Tests): the headless tests have no Avalonia
    /// dispatcher loop, so they marshal inline instead.</summary>
    internal Action<Action>? UiMarshalOverride { get; set; }

    private void OnUi(Action action)
    {
        if (UiMarshalOverride is { } marshal) { marshal(action); return; }
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
