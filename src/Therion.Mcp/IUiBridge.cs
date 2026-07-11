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
/// <param name="VisiblePanes">Ids of the tool panes currently open in the dock (docked or floating) — the same ids <c>focus_tool</c> takes.</param>
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

/// <summary>The result of a ring-R3 UI action (T-03.4).</summary>
/// <param name="Ok">True when the action did its thing.</param>
/// <param name="Message">A short human-readable note — what happened, or why it couldn't.</param>
public sealed record UiActionResult(bool Ok, string Message);

/// <summary>One whitelisted application setting (T-03.5).</summary>
/// <param name="Key">Stable dotted key, e.g. <c>editor.wordWrap</c>.</param>
/// <param name="Value">Its current value as a string (<c>true</c>/<c>false</c>, a number, or an enum token).</param>
/// <param name="Type">One of <c>bool</c>, <c>number</c>, <c>enum</c>, <c>string</c> — how to read/write <see cref="Value"/>.</param>
/// <param name="Description">A short English note on what the setting does.</param>
/// <param name="Options">For a <c>bool</c>/<c>enum</c>, the accepted values; empty for free-form settings.</param>
public sealed record McpSettingInfo(
    string Key, string Value, string Type, string Description, IReadOnlyList<string> Options);

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

    /// <summary>
    /// True when "follow the agent" is on — the R3 <em>action</em> tools may drive the UI. Read tools
    /// ignore it. False on a UI-less host.
    /// </summary>
    bool FollowAgent { get; }

    /// <summary>The current UI snapshot, or null when there is no window to read (headless / pre-startup).</summary>
    Task<UiState?> GetUiStateAsync();

    /// <summary>The open editor documents, most-recently-active order not guaranteed. Empty when no UI.</summary>
    Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync();

    // ---- ring-R3 actions (T-03.4). Each drives the running IDE on the UI thread. ------------------

    /// <summary>Opens <paramref name="absolutePath"/> in the editor (raising its tab), optionally at <paramref name="line"/> (1-based).</summary>
    Task<UiActionResult> OpenFileAsync(string absolutePath, int? line);

    /// <summary>Ensures the tool pane with id <paramref name="toolId"/> is visible and focused.</summary>
    Task<UiActionResult> FocusToolAsync(string toolId);

    /// <summary>Navigates the editor to the declaration of <paramref name="qualifiedName"/> (a station or survey).</summary>
    Task<UiActionResult> GotoSymbolAsync(string qualifiedName);

    /// <summary>Reveals <paramref name="station"/> in the embedded 3D model viewer.</summary>
    Task<UiActionResult> ShowInThreeDAsync(string station);

    /// <summary>Shows a toast notification (the host supplies a localized title). <paramref name="kind"/> is info/success/warning/error.</summary>
    Task<UiActionResult> ShowToastAsync(string message, string kind);

    // ---- guarded R3 actions (T-03.5). Default to "unsupported" so a UI-less or partial bridge (a test
    //      fake, NullUiBridge) needs no boilerplate; the app bridge overrides each one. --------------

    /// <summary>Runs the shell command with id <paramref name="commandId"/> (a <c>ShellCommandIds</c> token). The caller has already checked the allowlist.</summary>
    Task<UiActionResult> RunCommandAsync(string commandId) => Unsupported();

    /// <summary>Saves every open file that has unsaved changes.</summary>
    Task<UiActionResult> SaveAllAsync() => Unsupported();

    /// <summary>Applies a named layout preset: <c>default</c>, <c>split2</c>, <c>split3</c>, or <c>multi-monitor</c>.</summary>
    Task<UiActionResult> SetLayoutAsync(string preset) => Unsupported();

    /// <summary>The whitelisted settings and their current values. Empty when the host has no settings surface.</summary>
    IReadOnlyList<McpSettingInfo> ListSettings() => [];

    /// <summary>One whitelisted setting by key, or null when the key is not on the whitelist.</summary>
    McpSettingInfo? GetSetting(string key) => null;

    /// <summary>Sets a whitelisted setting from its string form; fails for an unknown key or an invalid value.</summary>
    Task<UiActionResult> SetSettingAsync(string key, string value) => Unsupported();

    /// <summary>The shared "this bridge can't do guarded R3" answer.</summary>
    private static Task<UiActionResult> Unsupported() =>
        Task.FromResult(new UiActionResult(false, "This host does not support guarded UI commands."));
}

/// <summary>Null-object bridge for hosts with no UI. R3 tools are never registered alongside it.</summary>
public sealed class NullUiBridge : IUiBridge
{
    public static readonly NullUiBridge Instance = new();

    private NullUiBridge() { }

    public bool IsAvailable => false;

    /// <summary>No dispatcher exists, so the delegate runs inline. (The in-app host never uses this bridge.)</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();

    public bool FollowAgent => false;

    public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);

    public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
        Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);

    private static Task<UiActionResult> NoUi() => Task.FromResult(new UiActionResult(false, "No UI."));
    public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => NoUi();
    public Task<UiActionResult> FocusToolAsync(string toolId) => NoUi();
    public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => NoUi();
    public Task<UiActionResult> ShowInThreeDAsync(string station) => NoUi();
    public Task<UiActionResult> ShowToastAsync(string message, string kind) => NoUi();
}
