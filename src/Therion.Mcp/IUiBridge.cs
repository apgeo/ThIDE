namespace Therion.Mcp;

/// <summary>
/// Ring-R3 seam and UI-thread marshaller: the only way an MCP tool (or the in-app
/// <see cref="IWorkspaceHost"/>) may reach the running IDE. The headless stdio host registers
/// <see cref="NullUiBridge"/> and never registers the R3 tools; the in-app host supplies an
/// implementation that marshals every UI/session touch onto the dispatcher thread.
/// </summary>
/// <remarks>
/// The presence of a non-null bridge in the service collection is what makes <c>AddTherionMcpTools</c>
/// register the R3 catalog at all.
/// </remarks>
public interface IUiBridge
{
    /// <summary>True when a main window exists and can service UI calls.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Runs <paramref name="func"/> on the UI thread and returns its result. UI-affine state — the live
    /// session model, unsaved editor buffers — must only be read through here. A UI-free host runs the
    /// delegate inline on the calling thread (there is no dispatcher to marshal to).
    /// </summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> func);
}

/// <summary>Null-object bridge for hosts with no UI. R3 tools are never registered alongside it.</summary>
public sealed class NullUiBridge : IUiBridge
{
    public static readonly NullUiBridge Instance = new();

    private NullUiBridge() { }

    public bool IsAvailable => false;

    /// <summary>No dispatcher exists, so the delegate runs inline. (The in-app host never uses this bridge.)</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
}
