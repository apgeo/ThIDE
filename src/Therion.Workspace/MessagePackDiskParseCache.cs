// Implementation Plan §4.5 / Decision D2 / Post-M6 follow-up D.
//
// MessagePack-backed on-disk parse cache. Same contract + envelope shape as
// `JsonDiskParseCache` so the two are drop-in interchangeable behind
// `IDiskParseCache`; selection happens at the composition root based on
// `WorkspaceOptions.DiskCacheFormat`.
//
// Like the JSON impl, only the **source text** is persisted (alongside the
// fingerprint). Full AST serialization remains future work; reparsing from the
// cached bytes still skips the encoding-prelude scan and any file-system I/O
// for hot files.

using MessagePack;
using System.Security.Cryptography;
using System.Text;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Workspace;

public sealed class MessagePackDiskParseCache : IDiskParseCache
{
    private const int SchemaVersion = 1;
    private const string FileExtension = ".msgpack";
    private readonly string _root;
    private readonly object _gate = new();
    private static readonly MessagePackSerializerOptions s_serializerOptions =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    public MessagePackDiskParseCache(string? cacheRoot = null)
    {
        _root = cacheRoot ?? JsonDiskParseCache.GetDefaultCacheRoot();
        Directory.CreateDirectory(_root);
    }

    public bool TryGet(ParseCacheKey key, out ParseResult<TherionFile> result)
    {
        result = default!;
        var path = EntryPath(key.AbsolutePath);
        if (!File.Exists(path)) return false;
        try
        {
            CacheEntry entry;
            lock (_gate)
            {
                using var fs = File.OpenRead(path);
                entry = MessagePackSerializer.Deserialize<CacheEntry>(fs, s_serializerOptions);
            }
            if (entry is null) return false;
            if (entry.Schema != SchemaVersion) return false;
            if (entry.Length != key.Length) return false;
            if (entry.LastWriteUtcTicks != key.LastWriteUtc.Ticks) return false;
            if (entry.SyntaxVersion != key.Version.ToString()) return false;

            result = Reparse(key.AbsolutePath, entry.SourceText);
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
            // EncodingResolver, not File.ReadAllText: the cached SourceText is re-parsed on a later
            // session, so reading it as UTF-8 here would resurrect the mojibake TherionWorkspace.ParseFile
            // was fixed to avoid (DECISIONS D-021) - silently, and only on a cache hit.
            string text = File.Exists(key.AbsolutePath) ? EncodingResolver.ReadAllText(key.AbsolutePath) : string.Empty;
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
                MessagePackSerializer.Serialize(fs, entry, s_serializerOptions);
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
            foreach (var file in Directory.EnumerateFiles(_root, "*" + FileExtension))
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
        return Path.Combine(_root, hex + FileExtension);
    }

    private static ParseResult<TherionFile> Reparse(string path, string text)
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

    [MessagePackObject]
    public sealed class CacheEntry
    {
        [Key(0)] public int Schema { get; set; }
        [Key(1)] public string AbsolutePath { get; set; } = string.Empty;
        [Key(2)] public long Length { get; set; }
        [Key(3)] public long LastWriteUtcTicks { get; set; }
        [Key(4)] public string SyntaxVersion { get; set; } = string.Empty;
        [Key(5)] public string SourceText { get; set; } = string.Empty;
    }
}
