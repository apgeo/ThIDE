// Implementation Plan §4.5 — JSON-backed on-disk cache.
//
// Stores a per-file fingerprint + the original source text so the workspace
// can re-parse instantly on startup from the cached bytes without hitting the
// real file system (and without re-reading the encoding-prelude scan, etc.).
// Full AST serialization is deferred to a MessagePack-backed implementation;
// the interface (<see cref="IDiskParseCache"/>) keeps that swap source-compatible.
//
// Each entry is a single `.json` file named after a stable hash of the absolute
// path. The on-disk schema version is bumped to invalidate older layouts.

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Workspace;

public sealed class JsonDiskParseCache : IDiskParseCache
{
    private const int SchemaVersion = 1;
    private readonly string _root;
    private readonly object _gate = new();

    public JsonDiskParseCache(string? cacheRoot = null)
    {
        _root = cacheRoot ?? GetDefaultCacheRoot();
        Directory.CreateDirectory(_root);
    }

    public static string GetDefaultCacheRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThIDE", "cache");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "ThIDE");
        // Linux + others: XDG default.
        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "thide");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "thide");
    }

    public bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result)
    {
        result = default!;
        var path = EntryPath(key.AbsolutePath);
        if (!File.Exists(path)) return false;
        try
        {
            CacheEntry? entry;
            lock (_gate)
            {
                using var fs = File.OpenRead(path);
                entry = JsonSerializer.Deserialize<CacheEntry>(fs);
            }
            if (entry is null) return false;
            if (entry.Schema != SchemaVersion) return false;
            if (entry.Length != key.Length) return false;
            if (entry.LastWriteUtcTicks != key.LastWriteUtc.Ticks) return false;
            if (entry.SyntaxVersion != key.Version.ToString()) return false;

            var parsed = Reparse(key.AbsolutePath, entry.SourceText, key.Version);
            result = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Set(ParseCacheKey key, ParseResult<TherionFile> result)
    {
        try
        {
            string text = File.Exists(key.AbsolutePath) ? File.ReadAllText(key.AbsolutePath) : string.Empty;
            var entry = new CacheEntry
            {
                Schema = SchemaVersion,
                AbsolutePath = key.AbsolutePath,
                Length = key.Length,
                LastWriteUtcTicks = key.LastWriteUtc.Ticks,
                SyntaxVersion = key.Version.ToString(),
                SourceText = text,
            };
            var path = EntryPath(key.AbsolutePath);
            lock (_gate)
            {
                using var fs = File.Create(path);
                JsonSerializer.Serialize(fs, entry);
            }
        }
        catch
        {
            // Disk-cache writes are best effort — never fail a parse.
        }
    }

    public void Invalidate(string absolutePath)
    {
        try
        {
            var path = EntryPath(absolutePath);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    public void InvalidateAll()
    {
        try
        {
            if (!Directory.Exists(_root)) return;
            foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

    private string EntryPath(string absolutePath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(absolutePath.ToLowerInvariant()));
        var hex = Convert.ToHexString(bytes, 0, 16);
        return Path.Combine(_root, hex + ".json");
    }

    private static ParseResult<TherionFile> Reparse(string path, string text, TherionSyntaxVersion version)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".th2"      => new Th2Parser().Parse(path, text),
            ".thconfig" => new ThconfigParser().Parse(path, text),
            ".thc"      => new ThconfigParser().Parse(path, text),
            ".th"       => new ThParser().Parse(path, text),
            _           => new ThconfigParser().Parse(path, text),
        };
    }

    internal sealed class CacheEntry
    {
        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("path")] public string AbsolutePath { get; set; } = string.Empty;
        [JsonPropertyName("length")] public long Length { get; set; }
        [JsonPropertyName("mtime")] public long LastWriteUtcTicks { get; set; }
        [JsonPropertyName("syntaxVersion")] public string SyntaxVersion { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string SourceText { get; set; } = string.Empty;
    }
}
