// Implementation Plan §9bis.2 — output artifact list cache.
//
// The compile itself is not cached (Therion writes outputs to the filesystem),
// but the UI wants to display the *previous* output list immediately on reopen.
// We keep a small JSON file keyed by SHA-256(entryPointAbsolutePath + therionVersion).

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.Processing.Abstractions;

namespace Therion.Build;

/// <summary>
/// When an entry's output list was last built, and whether it is out of date because a later build
/// of the same entry failed (the files on disk are then from an older, successful build).
/// </summary>
public sealed record OutputListState(DateTimeOffset? BuiltUtc, bool Stale)
{
    public static OutputListState Unknown { get; } = new(null, false);
}

public interface IOutputArtifactCache
{
    ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion);
    void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts);
    void Clear(string entryPointPath, string therionVersion);

    /// <summary>Saves the output list together with its build state. Caches that keep no state save the list only.</summary>
    void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts, OutputListState state)
        => Save(entryPointPath, therionVersion, artifacts);

    /// <summary>The build state saved with the list, or <see cref="OutputListState.Unknown"/>.</summary>
    OutputListState LoadState(string entryPointPath, string therionVersion) => OutputListState.Unknown;
}

public sealed class JsonOutputArtifactCache : IOutputArtifactCache
{
    private readonly string _root;
    private readonly object _gate = new();

    public JsonOutputArtifactCache(string? cacheRoot = null)
    {
        _root = cacheRoot ?? GetDefaultCacheRoot();
        Directory.CreateDirectory(_root);
    }

    public static string GetDefaultCacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThIDE", "artifacts");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "ThIDE", "artifacts");
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var basePath = !string.IsNullOrEmpty(xdg)
            ? Path.Combine(xdg, "thide")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "thide");
        return Path.Combine(basePath, "artifacts");
    }

    public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion)
    {
        try
        {
            var entry = ReadEntry(entryPointPath, therionVersion);
            if (entry?.Artifacts is null) return ImmutableArray<OutputArtifact>.Empty;
            var builder = ImmutableArray.CreateBuilder<OutputArtifact>(entry.Artifacts.Count);
            foreach (var a in entry.Artifacts)
                builder.Add(new OutputArtifact(a.Path, a.Kind, a.SizeBytes,
                    new DateTimeOffset(a.LastWriteUtcTicks, TimeSpan.Zero)));
            return builder.ToImmutable();
        }
        catch
        {
            return ImmutableArray<OutputArtifact>.Empty;
        }
    }

    public OutputListState LoadState(string entryPointPath, string therionVersion)
    {
        try
        {
            var entry = ReadEntry(entryPointPath, therionVersion);
            if (entry is null) return OutputListState.Unknown;
            DateTimeOffset? built = entry.BuiltUtcTicks is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
            return new OutputListState(built, entry.Stale);
        }
        catch
        {
            return OutputListState.Unknown;
        }
    }

    private CacheEntry? ReadEntry(string entryPointPath, string therionVersion)
    {
        var path = EntryPath(entryPointPath, therionVersion);
        if (!File.Exists(path)) return null;
        lock (_gate)
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<CacheEntry>(fs);
        }
    }

    public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) =>
        Save(entryPointPath, therionVersion, artifacts, OutputListState.Unknown);

    public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts, OutputListState state)
    {
        try
        {
            var entry = new CacheEntry
            {
                EntryPoint = entryPointPath,
                TherionVersion = therionVersion,
                BuiltUtcTicks = state.BuiltUtc?.UtcTicks,
                Stale = state.Stale,
                Artifacts = artifacts.Select(a => new ArtifactDto
                {
                    Path = a.Path,
                    Kind = a.Kind,
                    SizeBytes = a.SizeBytes,
                    LastWriteUtcTicks = a.LastWriteUtc.UtcTicks,
                }).ToList(),
            };
            var path = EntryPath(entryPointPath, therionVersion);
            lock (_gate)
            {
                using var fs = File.Create(path);
                JsonSerializer.Serialize(fs, entry);
            }
        }
        catch
        {
            // Best effort: artifact-list cache writes never fail a compile.
        }
    }

    public void Clear(string entryPointPath, string therionVersion)
    {
        try
        {
            var path = EntryPath(entryPointPath, therionVersion);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private string EntryPath(string entryPointPath, string therionVersion)
    {
        var key = entryPointPath.ToLowerInvariant() + "|" + therionVersion;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_root, Convert.ToHexString(bytes, 0, 16) + ".json");
    }

    internal sealed class CacheEntry
    {
        [JsonPropertyName("entry")] public string EntryPoint { get; set; } = string.Empty;
        [JsonPropertyName("therion")] public string TherionVersion { get; set; } = string.Empty;
        [JsonPropertyName("artifacts")] public List<ArtifactDto> Artifacts { get; set; } = new();
        // Absent in lists written before the build state was kept: read as "unknown, not stale".
        [JsonPropertyName("built")] public long? BuiltUtcTicks { get; set; }
        [JsonPropertyName("stale")] public bool Stale { get; set; }
    }

    internal sealed class ArtifactDto
    {
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long SizeBytes { get; set; }
        [JsonPropertyName("mtime")] public long LastWriteUtcTicks { get; set; }
    }
}
