namespace Therion.Mcp;

/// <summary>A machine-readable failure. <paramref name="Code"/> is a stable, snake_case token.</summary>
public sealed record ToolError(string Code, string Message);

/// <summary>
/// The result envelope every Therion MCP tool returns. Serialized camelCase by the SDK's default
/// options (see D-012), so the wire shape is <c>{"ok":true,"data":{…}}</c> or
/// <c>{"ok":false,"error":{"code":"…","message":"…"}}</c>.
/// </summary>
/// <remarks>
/// Models cope badly with exceptions surfaced as protocol errors; a tool that cannot answer should
/// return <see cref="Failure"/> with a code the model can act on, not throw.
/// </remarks>
public sealed record ToolResult<T>
{
    public bool Ok { get; init; }

    public T? Data { get; init; }

    public ToolError? Error { get; init; }

    public static ToolResult<T> Success(T data) => new() { Ok = true, Data = data };

    public static ToolResult<T> Failure(string code, string message) => Failure(new ToolError(code, message));

    public static ToolResult<T> Failure(ToolError error) => new() { Ok = false, Error = error };
}
