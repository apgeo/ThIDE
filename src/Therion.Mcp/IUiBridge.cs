namespace Therion.Mcp;

/// <summary>
/// A read-only snapshot of what the IDE is currently showing the user (T-03.3). Paths are absolute —
/// open documents may live outside the workspace. All fields reflect the moment the call was serviced.
/// </summary>
/// <param name="ActiveDocument">Absolute path of the document in the focused editor tab, or null.</param>
/// <param name="FocusedDocument">Absolute path of the last-focused editor (the one caret/selection describe).</param>
/// <param name="CaretLine">1-based caret line in the focused editor (0 when no editor is focused).</param>
/// <param name="CaretColumn">1-based caret column in the focused editor (0 when none).</param>
/// <param name="SelectionLength">Characters selected in the focused editor (0 for none).</param>
/// <param name="VisiblePanes">Titles of the tool panes currently open in the dock (docked or floating).</param>
/// <param name="UnsavedDocuments">Absolute paths of open documents with unsaved changes.</param>
/// <param name="FollowAgent">Whether "follow the agent" is on — R3 <em>actions</em> are enabled (T-03.5).</param>
public sealed record UiState(
    string? ActiveDocument,
    string? FocusedDocument,
    int CaretLine,
    int CaretColumn,
    int SelectionLength,
    IReadOnlyList<string> VisiblePanes,
    IReadOnlyList<string> UnsavedDocuments,
    bool FollowAgent);

/// <summary>One open editor document.</summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="Active">True for the currently-active document.</param>
/// <param name="Dirty">True when the buffer has unsaved changes.</param>
public sealed record OpenDocumentInfo(string Path, bool Active, bool Dirty);

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

    /// <summary>The current UI snapshot, or null when there is no window to read (headless / pre-startup).</summary>
    Task<UiState?> GetUiStateAsync();

    /// <summary>The open editor documents, most-recently-active order not guaranteed. Empty when no UI.</summary>
    Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync();
}

/// <summary>Null-object bridge for hosts with no UI. R3 tools are never registered alongside it.</summary>
public sealed class NullUiBridge : IUiBridge
{
    public static readonly NullUiBridge Instance = new();

    private NullUiBridge() { }

    public bool IsAvailable => false;

    /// <summary>No dispatcher exists, so the delegate runs inline. (The in-app host never uses this bridge.)</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();

    public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);

    public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
        Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
}
