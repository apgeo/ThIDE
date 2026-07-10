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
    public const string UnknownDiagnosticCode = "unknown_diagnostic_code";
    public const string SymbolNotFound = "symbol_not_found";
    public const string ModelUnavailable = "model_unavailable";

    // ---- mutations (T-02.1) --------------------------------------------------------------------

    /// <summary>A planned edit no longer matches the text it was planned against. Re-plan.</summary>
    public const string StalePlan = "stale_plan";

    /// <summary>The caller's <c>expectedSha256</c> does not match the file on disk.</summary>
    public const string FileChanged = "file_changed";

    /// <summary>A create step would overwrite something. Nothing is ever overwritten by a create.</summary>
    public const string FileExists = "file_exists";

    public const string WriteFailed = "write_failed";

    /// <summary>The new text holds a character the file's declared encoding cannot represent.</summary>
    public const string UnrepresentableCharacter = "unrepresentable_character";

    /// <summary>The requested new name is already taken in the same scope.</summary>
    public const string NameCollision = "name_collision";

    /// <summary>The file does not parse, so its tree cannot be re-emitted without losing text.</summary>
    public const string ParseErrors = "parse_errors";

    /// <summary>The source file could not be read as the survey format it claims to be.</summary>
    public const string ImportFailed = "import_failed";

    /// <summary>The project holds nothing of the kind the export would contain.</summary>
    public const string NothingToExport = "nothing_to_export";

    /// <summary>
    /// The target is open in the IDE with unsaved changes, so writing disk would fork the user's state.
    /// Only the in-app host can raise this (Q-01, resolved at T-03.6); the headless server cannot know.
    /// </summary>
    public const string FileDirty = "file_dirty";
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

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in <paramref name="budget"/> UTF-8
    /// bytes, never splitting a surrogate pair — half an emoji is not text.
    /// </summary>
    public static string Utf8Prefix(string text, int budget)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) <= budget) return text;

        int bytes = 0, i = 0;
        while (i < text.Length)
        {
            int width = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            int cost = System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(i, width));
            if (bytes + cost > budget) break;
            bytes += cost;
            i += width;
        }
        return text[..i];
    }
}
