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

public interface IOutputArtifactCache
{
    ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion);
    void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts);
    void Clear(string entryPointPath, string therionVersion);
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
                "TherionProc", "artifacts");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "TherionProc", "artifacts");
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var basePath = !string.IsNullOrEmpty(xdg)
            ? Path.Combine(xdg, "therionproc")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "therionproc");
        return Path.Combine(basePath, "artifacts");
    }

    public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion)
    {
        var path = EntryPath(entryPointPath, therionVersion);
        if (!File.Exists(path)) return ImmutableArray<OutputArtifact>.Empty;
        try
        {
            CacheEntry? entry;
            lock (_gate)
            {
                using var fs = File.OpenRead(path);
                entry = JsonSerializer.Deserialize<CacheEntry>(fs);
            }
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

    public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts)
    {
        try
        {
            var entry = new CacheEntry
            {
                EntryPoint = entryPointPath,
                TherionVersion = therionVersion,
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
    }

    internal sealed class ArtifactDto
    {
        [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("size")] public long SizeBytes { get; set; }
        [JsonPropertyName("mtime")] public long LastWriteUtcTicks { get; set; }
    }
}
