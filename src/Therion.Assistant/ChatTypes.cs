namespace Therion.Assistant;

/// <param name="Endpoint">OpenAI-compatible base URL ending in <c>/v1</c> (trailing slash tolerated),
/// e.g. LM Studio's <c>http://127.0.0.1:1234/v1</c>.</param>
/// <param name="ApiKey">Bearer token per request, or null — LM Studio needs none.</param>
/// <param name="MaxTurns">Model↔tool round-trips before the engine gives up on a run.</param>
public sealed record ChatEngineOptions(string Endpoint, string Model)
{
    public string? ApiKey { get; init; }
    public int MaxTurns { get; init; } = 10;
    public double Temperature { get; init; }
}

/// <param name="ArgumentsJson">The raw arguments string the model produced (pretty enough for a
/// confirmation card; may be invalid JSON, in which case the call never executes).</param>
/// <param name="ReadOnly">From the tool's descriptor; false for a name no descriptor matches.</param>
public sealed record ToolCallInfo(string Tool, string ArgumentsJson, bool ReadOnly);

/// <summary>A live-progress notification, for rendering a transcript while the loop runs.</summary>
public abstract record ChatUpdate;

/// <summary>The model asked for a tool; execution (or the approval pause) is about to start.</summary>
public sealed record ToolCallStarted(ToolCallInfo Call) : ChatUpdate;

/// <summary>The call finished (or was screened out / declined); <paramref name="Content"/> is what
/// the model will see as the result.</summary>
public sealed record ToolCallFinished(ToolCallInfo Call, bool Ok, string Content) : ChatUpdate;

/// <summary>The model produced its final text for this run (including the gave-up notice).</summary>
public sealed record AssistantAnswered(string Text) : ChatUpdate;

/// <summary>Hooks a host (the Assistant pane, the eval harness) gives the engine. All optional.</summary>
public sealed record ChatCallbacks
{
    /// <summary>
    /// Awaited before any non-read-only tool executes; return false to decline the call (the
    /// model is told "The user declined this action." and keeps its turn). Null allows everything —
    /// the eval harness's behaviour.
    /// </summary>
    public Func<ToolCallInfo, CancellationToken, Task<bool>>? ApproveAsync { get; init; }

    /// <summary>Fired as the loop progresses. Invoked on the engine's (thread-pool) context.</summary>
    public Action<ChatUpdate>? OnUpdate { get; init; }
}

/// <param name="SchemaValid">The call named a real tool and its arguments parsed as JSON — how a
/// hallucinated tool is caught.</param>
/// <param name="Ok">The tool ran and reported success. False for failures, screening misses, and declines.</param>
/// <param name="Declined">The approval hook rejected the call (a subset of <c>Ok:false</c>).</param>
public sealed record ChatToolCall(string Tool, bool SchemaValid, bool Ok, bool Declined = false);

/// <param name="FinalText">The model's last assistant message (no tool calls), or the engine's
/// "(no response …)" / "(gave up …)" notice.</param>
/// <param name="Turns">Model↔tool round-trips.</param>
/// <param name="Tokens">Total tokens the endpoint reported (0 if it didn't).</param>
public sealed record ChatResult(
    string FinalText, IReadOnlyList<ChatToolCall> Calls, int Turns, int Tokens);
