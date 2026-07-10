namespace Therion.Blender.Sources;

/// <summary>
/// What model to acquire. Either points at an external file, or asks the resolver to
/// discover the workspace's build artifact (with an optional freshness check and
/// re-export fallback). See <see cref="ModelSourceResolver"/>.
/// </summary>
public sealed record ModelSourceRequest
{
    /// <summary>An explicit external file to use (FR-01b). When set, discovery and
    /// re-export are skipped.</summary>
    public string? ExternalFilePath { get; init; }

    /// <summary>Prefer a <c>.lox</c> artifact over a <c>.3d</c> one when both exist
    /// (a <c>.lox</c> carries walls; D-06/R-15).</summary>
    public bool PreferLox { get; init; } = true;

    /// <summary>Newest modification time across the workspace source files. A discovered
    /// artifact older than this is considered stale. Null ⇒ freshness can't be judged
    /// (the artifact is accepted as-is).</summary>
    public DateTimeOffset? SourceModifiedUtc { get; init; }

    /// <summary>Allow a Therion re-export when no artifact exists or the best one is
    /// stale (FR-01c). Requires an <see cref="IModelReExporter"/>.</summary>
    public bool AllowReExport { get; init; }

    /// <summary>A request for an explicit external file.</summary>
    public static ModelSourceRequest ForExternalFile(string path) => new() { ExternalFilePath = path };

    /// <summary>A request that discovers the workspace artifact, optionally re-exporting.</summary>
    public static ModelSourceRequest ForWorkspace(
        DateTimeOffset? sourceModifiedUtc = null, bool allowReExport = false, bool preferLox = true) => new()
    {
        SourceModifiedUtc = sourceModifiedUtc,
        AllowReExport = allowReExport,
        PreferLox = preferLox,
    };
}
