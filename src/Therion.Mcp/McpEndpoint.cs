using System.Text.Json;
using System.Text.Json.Serialization;

namespace Therion.Mcp;

/// <summary>
/// The MCP discovery file the in-app host writes and the <c>--connect</c> shim reads
/// (<c>%AppData%/ThIDE/mcp-endpoint.json</c>, XDG fallback on POSIX). One definition, shared by the
/// writer (<c>McpHostService</c>) and the reader (the stdio↔HTTP shim) so the on-wire format cannot
/// drift. Fields are camelCase (D-012); a reader tolerates a stale <see cref="Pid"/>.
/// </summary>
/// <param name="Port">Loopback TCP port the in-app server is bound to.</param>
/// <param name="Token">Bearer token required on every request (cleartext — the file is owner-only on POSIX).</param>
/// <param name="Pid">The IDE process id that wrote the file; may be stale if the IDE died uncleanly.</param>
/// <param name="StartedUtc">When the listener started, ISO-8601.</param>
/// <param name="Url">The base URL a client connects to, e.g. <c>http://127.0.0.1:1234/</c>.</param>
public sealed record McpEndpoint(int Port, string Token, int Pid, string StartedUtc, string Url)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    /// <summary>Absolute path of the discovery file under the user's app-data dir (XDG <c>~/.config</c> fallback).</summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "mcp-endpoint.json");
    }

    /// <summary>Serializes to the camelCase JSON the discovery file holds.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Reads and parses the discovery file, or returns null when it is absent, unreadable, malformed, or
    /// missing the fields a client needs — the shim treats every one of those as "no server running".
    /// </summary>
    public static McpEndpoint? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var endpoint = JsonSerializer.Deserialize<McpEndpoint>(File.ReadAllText(path), Json);
            return endpoint is { Port: > 0 }
                   && !string.IsNullOrEmpty(endpoint.Token)
                   && !string.IsNullOrEmpty(endpoint.Url)
                ? endpoint
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
