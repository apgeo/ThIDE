namespace Therion.Blender.Sources;

/// <summary>How a resolved model source was obtained.</summary>
public enum ModelSourceKind
{
    /// <summary>An explicit external file the user chose.</summary>
    ExternalFile,
    /// <summary>An artifact discovered from the workspace's last build.</summary>
    WorkspaceArtifact,
    /// <summary>A fresh artifact produced by re-running Therion.</summary>
    ReExported,
}

/// <summary>The model file the module will convert, plus provenance and freshness.</summary>
public sealed record ResolvedModelSource
{
    /// <summary>Absolute path to the file to convert.</summary>
    public required string Path { get; init; }

    /// <summary>The file's format.</summary>
    public required CaveSourceFormat Format { get; init; }

    /// <summary>How it was obtained.</summary>
    public required ModelSourceKind Kind { get; init; }

    /// <summary>False when a workspace artifact is older than the sources and no
    /// re-export happened (the caller may warn the user).</summary>
    public bool IsFresh { get; init; } = true;

    /// <summary>Human-readable reason the source is stale, when <see cref="IsFresh"/>
    /// is false; null otherwise.</summary>
    public string? StalenessReason { get; init; }

    /// <summary>The underlying discovered artifact, when the source came from discovery
    /// or re-export; null for an external file.</summary>
    public ModelArtifact? Artifact { get; init; }
}
