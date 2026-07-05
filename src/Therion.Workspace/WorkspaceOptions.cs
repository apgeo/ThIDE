// Implementation Plan §6, §6.1, §6.2. Workspace options exposed via DI.
// Decision #14: sidecar `.thp.json` overrides user profile.

namespace Therion.Workspace;

/// <summary>Strategy for resolving a project entry-point (see §6.1).</summary>
public enum EntryPointDiscoveryMode
{
    /// <summary>User picked a specific file.</summary>
    OpenFile,
    /// <summary>User picked a folder; auto-detect.</summary>
    OpenFolder,
    /// <summary>Explicit path supplied via CLI/sidecar.</summary>
    Explicit,
}

/// <summary>Per-workspace runtime options.</summary>
public sealed record WorkspaceOptions
{
    /// <summary>Default discovery strategy when none is specified.</summary>
    public EntryPointDiscoveryMode EntryPointDiscovery { get; init; } = EntryPointDiscoveryMode.OpenFile;

    /// <summary>Recurse into sub-directories when scanning a folder for entry points (Decision #20).</summary>
    public bool RecursiveFolderScan { get; init; } = false;

    /// <summary>Maximum recursion depth when <see cref="RecursiveFolderScan"/> is on (Decision #20).</summary>
    public int MaxFolderScanDepth { get; init; } = 3;

    /// <summary>
    /// Disable on-disk cache (Decision #8). Default is <c>true</c> as of Post-M6 follow-up D:
    /// the disk cache is now opt-in to keep startup cold-path deterministic and to avoid
    /// surprising disk writes when the app first runs. Enable explicitly via
    /// <see cref="EnableDiskCacheEnvVar"/>=1 or by constructing <see cref="WorkspaceOptions"/>
    /// with <c>DisableDiskCache = false</c>.
    /// </summary>
    public bool DisableDiskCache { get; init; } = true;

    /// <summary>Serialization format for the disk cache (Plan §4.5 / D2 / Post-M6 D).</summary>
    public DiskCacheFormat DiskCacheFormat { get; init; } = DiskCacheFormat.MessagePack;

    /// <summary>Filename of the sidecar settings file (Decision #14).</summary>
    public string SidecarFileName { get; init; } = ".thp.json";

    /// <summary>
    /// Environment variable that disables disk caching when set to a non-empty value.
    /// Mirrors Decision #8 / Plan §4.5. Kept for back-compat; redundant now that
    /// disk cache defaults to disabled.
    /// </summary>
    public const string DisableDiskCacheEnvVar = "THIDE_NO_CACHE";

    /// <summary>
    /// Environment variable that opts in to disk caching (Post-M6 D). Set to a
    /// non-empty value other than <c>"0"</c> to enable.
    /// </summary>
    public const string EnableDiskCacheEnvVar = "THIDE_DISK_CACHE";

    /// <summary>
    /// Environment variable selecting the disk-cache backend. Accepts
    /// <c>"json"</c> or <c>"messagepack"</c> (case-insensitive).
    /// </summary>
    public const string DiskCacheFormatEnvVar = "THIDE_DISK_CACHE_FORMAT";

    /// <summary>
    /// Materializes options from defaults + environment variables. Honors
    /// <see cref="DisableDiskCacheEnvVar"/>, <see cref="EnableDiskCacheEnvVar"/>,
    /// and <see cref="DiskCacheFormatEnvVar"/>.
    /// </summary>
    public static WorkspaceOptions FromEnvironment(WorkspaceOptions? baseline = null)
    {
        var opts = baseline ?? new WorkspaceOptions();

        var enableFlag = Environment.GetEnvironmentVariable(EnableDiskCacheEnvVar);
        if (!string.IsNullOrEmpty(enableFlag) && enableFlag != "0")
            opts = opts with { DisableDiskCache = false };

        var disableFlag = Environment.GetEnvironmentVariable(DisableDiskCacheEnvVar);
        if (!string.IsNullOrEmpty(disableFlag) && disableFlag != "0")
            opts = opts with { DisableDiskCache = true };

        var fmt = Environment.GetEnvironmentVariable(DiskCacheFormatEnvVar);
        if (!string.IsNullOrEmpty(fmt))
        {
            opts = fmt.ToLowerInvariant() switch
            {
                "json"        => opts with { DiskCacheFormat = DiskCacheFormat.Json },
                "messagepack" => opts with { DiskCacheFormat = DiskCacheFormat.MessagePack },
                "msgpack"     => opts with { DiskCacheFormat = DiskCacheFormat.MessagePack },
                _             => opts,
            };
        }
        return opts;
    }
}

/// <summary>Disk-cache serialization backend (Plan §4.5 / Post-M6 D).</summary>
public enum DiskCacheFormat
{
    Json,
    MessagePack,
}
