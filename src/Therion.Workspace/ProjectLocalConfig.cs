// Project-local configuration: the snippets, templates, hooks, tasks and macros a club keeps with a
// cave, as opposed to the per-user *state* under ProjectSidecar (leads, metadata). By default it is a
// `.thide/` directory at the project root, committed with the survey; a user who wants it private
// keeps the same files under app data instead.

using System.Text.Json;

namespace Therion.Workspace;

public enum ProjectConfigLocation
{
    /// <summary><c>&lt;root&gt;/.thide/</c> — shared with everyone who has the project.</summary>
    ProjectFolder,
    /// <summary>Under the app-data directory, keyed by project root — private to this computer.</summary>
    AppData,
}

/// <summary>The outcome of reading one config file: a value, nothing (no file), or an error to show.</summary>
public sealed record ConfigRead<T>(T? Value, string? Error) where T : class
{
    public static ConfigRead<T> Missing { get; } = new(null, null);
}

public static class ProjectLocalConfig
{
    public const string DirectoryName = ".thide";

    /// <summary>Prefix of files the app generates beside a thconfig; discovery never offers them.</summary>
    public const string GeneratedFilePrefix = ".thide-";

    /// <summary>
    /// The directory holding a project's config files, or null without a root.
    /// </summary>
    /// <param name="appDataProjectsDir">Override for the app-data <c>projects</c> directory (tests).</param>
    public static string? DirectoryFor(string? root, ProjectConfigLocation location, string? appDataProjectsDir = null)
    {
        if (string.IsNullOrEmpty(root)) return null;
        return location switch
        {
            ProjectConfigLocation.AppData =>
                Path.Combine(appDataProjectsDir ?? ProjectSidecar.DirectoryFor("projects"), ProjectSidecar.KeyFor(root)!, "config"),
            _ => Path.Combine(root, DirectoryName),
        };
    }

    /// <summary>A file inside the config directory, e.g. <c>PathFor(root, loc, "hooks.json")</c>.</summary>
    public static string? PathFor(string? root, ProjectConfigLocation location, string relative, string? appDataProjectsDir = null) =>
        DirectoryFor(root, location, appDataProjectsDir) is { } dir ? Path.Combine(dir, relative) : null;

    /// <summary>
    /// Reads a JSON config file. Hand-edited files are expected, so comments and trailing commas are
    /// accepted and property names are case-insensitive. Never throws: a broken file is an error value
    /// the caller shows once, and the feature carries on without it.
    /// </summary>
    public static ConfigRead<T> TryReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return ConfigRead<T>.Missing;
        try
        {
            using var stream = File.OpenRead(path);
            var value = JsonSerializer.Deserialize<T>(stream, JsonOptions);
            return value is null ? new(null, $"{Path.GetFileName(path)}: the file is empty.") : new(value, null);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : "";
            return new(null, $"{Path.GetFileName(path)}{where}: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(null, $"{Path.GetFileName(path)}: {ex.Message}");
        }
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
}
