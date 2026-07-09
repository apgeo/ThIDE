namespace Therion.Mcp;

/// <summary>
/// Ring-R3 seam: the only way an MCP tool may reach the running IDE. The headless stdio host
/// registers <see cref="NullUiBridge"/> and never registers the R3 tools; the in-app host supplies
/// an implementation that marshals every UI touch onto the dispatcher thread.
/// </summary>
/// <remarks>
/// Kept deliberately bare until <c>T-03.2</c> grows it — the presence of a non-null bridge in the
/// service collection is what makes <c>AddTherionMcpTools</c> register the R3 catalog at all.
/// </remarks>
public interface IUiBridge
{
    /// <summary>True when a main window exists and can service UI calls.</summary>
    bool IsAvailable { get; }
}

/// <summary>Null-object bridge for hosts with no UI. R3 tools are never registered alongside it.</summary>
public sealed class NullUiBridge : IUiBridge
{
    public static readonly NullUiBridge Instance = new();

    private NullUiBridge() { }

    public bool IsAvailable => false;
}
