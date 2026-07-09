// One blessed way to open a workspace from a path, shared by every headless consumer
// (therion-cli, therion-mcp). Resolves the entry point — a file is taken as-is, a folder is
// scanned per ProjectEntryPointResolver — then loads and parses the source graph.

using Therion.Processing.Abstractions;

namespace Therion.Workspace;

/// <summary>Entry-point resolution failed: the path is missing, or a folder holds no single config.</summary>
public sealed class WorkspaceLoadException : Exception
{
    public WorkspaceLoadException(string message, IReadOnlyList<string> candidates) : base(message) =>
        Candidates = candidates;

    /// <summary>Entry-point candidates found, when the failure was an ambiguous folder.</summary>
    public IReadOnlyList<string> Candidates { get; }
}

public static class WorkspaceLoader
{
    /// <summary>
    /// Opens the workspace rooted at <paramref name="pathOrFolder"/>. A file is used directly; a
    /// folder must resolve to exactly one entry-point candidate.
    /// </summary>
    /// <exception cref="WorkspaceLoadException">The path is missing, or a folder is empty/ambiguous.</exception>
    public static async ValueTask<TherionWorkspace> OpenAsync(
        string pathOrFolder,
        WorkspaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string entryPoint = await ResolveEntryPointAsync(pathOrFolder, options, cancellationToken).ConfigureAwait(false);

        var workspace = new TherionWorkspace(options: options);
        try
        {
            await workspace.LoadAsync(entryPoint, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return workspace;
    }

    /// <summary>Resolves a file-or-folder path to the single entry point a workspace should load.</summary>
    /// <exception cref="WorkspaceLoadException">The path is missing, or a folder is empty/ambiguous.</exception>
    public static async ValueTask<string> ResolveEntryPointAsync(
        string pathOrFolder,
        WorkspaceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrFolder))
            throw new WorkspaceLoadException("No path given.", []);

        if (File.Exists(pathOrFolder)) return Path.GetFullPath(pathOrFolder);

        if (!Directory.Exists(pathOrFolder))
            throw new WorkspaceLoadException($"Path not found: {pathOrFolder}", []);

        var resolver = new ProjectEntryPointResolver(new ThconfigSniffer(), options);
        var resolution = await resolver.ResolveAsync(pathOrFolder, cancellationToken).ConfigureAwait(false);

        if (resolution.Selected is { } selected) return selected;

        throw new WorkspaceLoadException(
            resolution.Candidates.Length == 0
                ? $"No Therion configuration file found in '{pathOrFolder}'."
                : $"'{pathOrFolder}' has {resolution.Candidates.Length} entry-point candidates; name one explicitly.",
            resolution.Candidates);
    }
}
