// The Assistant pane's engine room (AI-07, D-043): owns the OpenAI-compatible chat loop
// (Therion.Assistant.ChatEngine) and reaches the project tools by self-connecting as an MCP
// client to the IDE's own loopback server — the --connect shim's path, minus the discovery file
// (the port+token come straight from IMcpHostService). The panel is a *host*: the server keeps
// every safety rail (profiles, dry-run, path jail, file_dirty, FollowAgent), and this class adds
// nothing but transport and conversation state.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Therion.Assistant;
using ThIDE.Resources;

namespace ThIDE.Services;

/// <summary>Why the assistant cannot run right now — the pane turns these into affordances.</summary>
public enum AssistantUnavailableReason
{
    /// <summary>The <c>EnableMcpServer</c> setting is off; offer to turn it on.</summary>
    ServerDisabled,
    /// <summary>The setting is on but the listener isn't up (failed start); point at the Log pane.</summary>
    ServerNotListening,
}

public sealed class AssistantUnavailableException(AssistantUnavailableReason reason)
    : InvalidOperationException($"assistant unavailable: {reason}")
{
    public AssistantUnavailableReason Reason { get; } = reason;
}

/// <summary>The in-app assistant: one conversation at a time against the local model + the IDE's tools.</summary>
public interface IAssistantService : IAsyncDisposable
{
    /// <summary>Runs one user turn. Throws <see cref="AssistantUnavailableException"/> when the
    /// in-app tools server is off/down, <see cref="OperationCanceledException"/> on Stop, and
    /// <see cref="InvalidOperationException"/> when the endpoint misbehaves.</summary>
    Task<ChatResult> SendAsync(string userMessage, ChatCallbacks callbacks, CancellationToken ct);

    /// <summary>Drops the conversation history (and any cached tool connection stays).</summary>
    void NewConversation();

    /// <summary>The visible dialogue persisted from a previous run, for the pane to redraw on
    /// startup. Empty when there is nothing saved.</summary>
    IReadOnlyList<ConversationTurn> RestoredConversation();

    /// <summary>"model @ endpoint", for the pane's status line.</summary>
    string ModelLabel { get; }
}

public sealed class AssistantService : IAssistantService
{
    private readonly IAppSettingsService _settings;
    private readonly IMcpHostService _host;
    private readonly ILogService _log;

    // One HttpClient for the model endpoint's lifetime; generous timeout — a 30B model on CPU
    // offload can legitimately take minutes per completion (same budget as the eval harness).
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // One turn at a time; the pane gates the UI, this gate is the backstop.
    private readonly SemaphoreSlim _turn = new(1, 1);

    // Where the running conversation is mirrored so it survives a restart.
    private readonly string _conversationPath;
    // The conversation loaded from that file at construction, used to seed the first turn and to
    // redraw the transcript; cleared once a New Chat or a fresh turn takes over.
    private ChatSession? _restored;

    private McpClient? _client;
    private McpToolCatalog? _catalog;
    private string? _connectedToken;
    private ChatSession? _session;
    private bool _disposed;

    public AssistantService(IAppSettingsService settings, IMcpHostService host, ILogService log)
        : this(settings, host, log, DefaultConversationPath()) { }

    // Test seam (InternalsVisibleTo ThIDE.Tests): a conversation file under a temp dir keeps a test
    // from reading or writing the developer's real %AppData%/ThIDE conversation.
    internal AssistantService(IAppSettingsService settings, IMcpHostService host, ILogService log, string conversationPath)
    {
        _settings = settings;
        _host = host;
        _log = log;
        _conversationPath = conversationPath;
        _restored = LoadConversation(conversationPath);
    }

    public string ModelLabel => $"{_settings.Current.AssistantModel} @ {_settings.Current.AssistantEndpoint}";

    public async Task<ChatResult> SendAsync(string userMessage, ChatCallbacks callbacks, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_turn.Wait(0))
            throw new InvalidOperationException("The assistant is already answering; wait or press Stop.");
        try
        {
            var catalog = await EnsureConnectedAsync(ct);
            var current = _settings.Current;
            var engine = new ChatEngine(_http, new ChatEngineOptions(current.AssistantEndpoint, current.AssistantModel)
            {
                MaxTurns = current.AssistantMaxTurns,
                SynthesizeFinalAnswer = current.AssistantSynthesizeFinalAnswer,
                Stream = current.AssistantStreaming,
            });
            // Continue the restored conversation on the first turn after a restart; otherwise start a
            // fresh one, seeding the workspace context card when the setting asks for it (CD-02).
            _session ??= _restored ?? await NewSessionAsync(current, ct);
            _restored = null;

            var result = await engine.RunAsync(_session, userMessage, catalog, callbacks, ct);
            _log.Info($"[Assistant] turn done: {result.Calls.Count} tool call(s), {result.Tokens} tokens.");
            SaveConversation();
            return result;
        }
        finally
        {
            _turn.Release();
        }
    }

    public IReadOnlyList<ConversationTurn> RestoredConversation() =>
        _restored?.Dialogue() ?? Array.Empty<ConversationTurn>();

    public void NewConversation()
    {
        _session = null;
        _restored = null;
        TryDeleteConversation();
    }

    /// <summary>
    /// Self-connects to the in-app server (starting it when the setting is on but it hasn't come
    /// up yet), reusing the cached session while the token — one per server start — still matches.
    /// </summary>
    private async Task<McpToolCatalog> EnsureConnectedAsync(CancellationToken ct)
    {
        if (!_settings.Current.EnableMcpServer)
            throw new AssistantUnavailableException(AssistantUnavailableReason.ServerDisabled);

        if (_host.Endpoint is null)
            await _host.ApplySettingAsync(ct).ConfigureAwait(false);
        var endpoint = _host.Endpoint
            ?? throw new AssistantUnavailableException(AssistantUnavailableReason.ServerNotListening);

        if (_catalog is not null && _connectedToken == endpoint.Token) return _catalog;

        await DisconnectAsync().ConfigureAwait(false);

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint.Url),
            TransportMode = HttpTransportMode.AutoDetect,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {endpoint.Token}" },
            Name = "ThIDE assistant pane",
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
        try
        {
            var tools = (await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false)).ToList();
            _catalog = new McpToolCatalog(client, tools);
            _client = client;
            _connectedToken = endpoint.Token;
            _log.Info($"[Assistant] connected to the in-app tools server ({tools.Count} tools).");
            return _catalog;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task DisconnectAsync()
    {
        var client = _client;
        _client = null;
        _catalog = null;
        _connectedToken = null;
        if (client is not null)
        {
            try { await client.DisposeAsync().ConfigureAwait(false); }
            catch { /* the server may already be gone */ }
        }
    }

    /// <summary>
    /// English on purpose (D-008: model-facing text is English; only rendered UI is localized).
    /// The dry-run sentence is the R-001 lesson: Qwen previewed an edit and declared victory, so
    /// the prompt must spell out that a preview is not an applied change. The prose-summary clause
    /// (AI-08.1, gated by <see cref="AppSettings.AssistantRequireProseSummary"/>) steers tool-happy
    /// coder models toward answering in natural language instead of dumping JSON.
    /// </summary>
    private static string SystemPrompt(AppSettings settings)
    {
        var prompt =
            "You are the assistant built into ThIDE, an IDE for Therion cave-survey projects. "
            + "Use the provided tools to inspect and modify the open project; do not guess or invent tools. "
            + "Editing tools preview by default: a change is applied only when a call with dryRun:false "
            + "succeeds — preview first, then apply, then report the result (including any new diagnostics). ";

        if (settings.AssistantRequireProseSummary)
            prompt +=
                "After using tools, always reply to the user in clear natural language: summarize what you "
                + "found or did and reference the relevant objects by name. The full tool results (lists of "
                + "stations, symbols, diagnostics, …) are already shown to the user in the panel, so do NOT "
                + "paste raw JSON or repeat long lists — describe and highlight the relevant items instead. ";

        return prompt + "When you have the answer, reply directly and concisely.";
    }

    /// <summary>
    /// A fresh conversation seeded with the persona prompt, plus — when <see cref="AppSettings.AssistantContextMode"/>
    /// is Card or Pack — the workspace context digest as a second system message (CD-02). The digest is
    /// read from the in-app server's <c>therion://context/*</c> resource, the same one external hosts
    /// attach, so there is one generator behind both consumers. A failed read (or an error envelope,
    /// e.g. no workspace loaded) is swallowed: context is a nice-to-have, never a reason a turn can't run.
    /// </summary>
    private async Task<ChatSession> NewSessionAsync(AppSettings settings, CancellationToken ct)
    {
        var session = new ChatSession(SystemPrompt(settings));
        var uri = settings.AssistantContextMode switch
        {
            AssistantContextMode.Card => "therion://context/card",
            AssistantContextMode.Pack => "therion://context/pack",
            _ => null,
        };
        if (uri is null || _client is null) return session;

        try
        {
            var result = await _client.ReadResourceAsync(uri, cancellationToken: ct).ConfigureAwait(false);
            var text = string.Concat(result.Contents.OfType<TextResourceContents>().Select(c => c.Text));
            // The card/pack is markdown starting with '#'; a leading '{' is the {ok:false,error} envelope
            // the resource returns when there is no workspace — not context worth injecting.
            if (!string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith('{'))
            {
                session.AppendSystem(text);
                _log.Info($"[Assistant] injected {settings.AssistantContextMode} context ({text.Length} chars).");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"[Assistant] context ({settings.AssistantContextMode}) unavailable: {ex.Message}");
        }
        return session;
    }

    // ---- conversation persistence -----------------------------------------------------------

    /// <summary>%AppData%/ThIDE/assistant-conversation.json (XDG fallback on POSIX).</summary>
    private static string DefaultConversationPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "assistant-conversation.json");
    }

    private static ChatSession? LoadConversation(string path)
    {
        try { return File.Exists(path) ? ChatSession.Restore(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    private void SaveConversation()
    {
        if (_session is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_conversationPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_conversationPath, _session.Serialize());
        }
        catch (Exception ex)
        {
            _log.Warning($"[Assistant] could not persist the conversation: {ex.Message}");
        }
    }

    private void TryDeleteConversation()
    {
        try { if (File.Exists(_conversationPath)) File.Delete(_conversationPath); }
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _http.Dispose();
        _turn.Dispose();
    }
}
