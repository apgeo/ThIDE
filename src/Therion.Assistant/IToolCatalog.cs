namespace Therion.Assistant;

/// <summary>
/// A tool the engine may advertise to the model.
/// </summary>
/// <param name="ParametersJson">The tool's input schema as raw JSON Schema text (what an
/// OpenAI-compatible endpoint expects under <c>function.parameters</c>).</param>
/// <param name="ReadOnly">True when the tool cannot change anything (MCP <c>readOnlyHint</c>).
/// Non-read-only calls are what the engine's approval hook gates.</param>
public sealed record ToolDescriptor(string Name, string? Description, string ParametersJson, bool ReadOnly);

/// <param name="Content">The text fed back to the model as the tool result.</param>
/// <param name="Ok">False when the tool answered but reported failure (<c>ok:false</c> / isError).</param>
public sealed record ToolOutcome(string Content, bool Ok);

/// <summary>
/// Where the engine's tool calls land. Implementations: <see cref="McpToolCatalog"/> (the real
/// catalog over an MCP client), and test fakes.
/// </summary>
public interface IToolCatalog
{
    IReadOnlyList<ToolDescriptor> Tools { get; }

    /// <summary>
    /// Executes one call. Only called with a name from <see cref="Tools"/> and arguments that
    /// parsed as a JSON object — the engine screens both first (that screening is the
    /// schema-validity signal). May throw; the engine turns the exception into an error result.
    /// </summary>
    Task<ToolOutcome> CallAsync(
        string name, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}
