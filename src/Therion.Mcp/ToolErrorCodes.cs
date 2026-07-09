namespace Therion.Mcp;

/// <summary>
/// Stable error tokens. A model branches on these, so they are part of the wire contract: adding is
/// free, renaming is a breaking change (TOOL-REGISTRY changelog).
/// </summary>
public static class ToolErrorCodes
{
    public const string WorkspaceNotLoaded = "workspace_not_loaded";
    public const string WorkspaceLoadFailed = "workspace_load_failed";
    public const string PathOutsideWorkspace = "path_outside_workspace";
    public const string FileNotFound = "file_not_found";
    public const string InvalidArgument = "invalid_argument";
    public const string ReadFailed = "read_failed";
}

/// <summary>
/// Result-size and paging caps. Local hosts run small context windows: a tool that dumps a whole
/// workspace into one message costs the model the very context it needs to act on the answer.
/// </summary>
public static class ToolLimits
{
    /// <summary>Default byte budget for a single text payload (~100 KB, per TOOL-REGISTRY).</summary>
    public const int DefaultMaxBytes = 100_000;

    /// <summary>Ceiling a caller may raise <c>maxBytes</c> to.</summary>
    public const int HardMaxBytes = 1_000_000;

    /// <summary>Default page size for list-returning tools.</summary>
    public const int DefaultPageLimit = 200;

    /// <summary>Ceiling a caller may raise <c>limit</c> to.</summary>
    public const int MaxPageLimit = 2_000;

    /// <summary>Clamps a caller-supplied page size; non-positive means "use the default".</summary>
    public static int ClampLimit(int limit) =>
        limit <= 0 ? DefaultPageLimit : Math.Min(limit, MaxPageLimit);

    /// <summary>Clamps a caller-supplied byte budget; non-positive means "use the default".</summary>
    public static int ClampBytes(int maxBytes) =>
        maxBytes <= 0 ? DefaultMaxBytes : Math.Min(maxBytes, HardMaxBytes);
}
