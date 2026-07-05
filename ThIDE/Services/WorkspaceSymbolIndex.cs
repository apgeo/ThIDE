// persistent workspace symbol index.
//
// The disk parse cache (IDiskParseCache) already makes reopening individual files fast, but the
// cross-file semantic model still has to be rebuilt (now in the background) before
// symbol search ("Go to Symbol in Workspace", Ctrl+Shift+P #) has anything to show. This index
// persists a flat snapshot of the project's symbols (surveys / scraps / maps / stations + their
// declaration spans) to disk, keyed by workspace root, so symbol search is available *instantly*
// on reopen — warmed before the graph finishes rebuilding.
//
// It deliberately stores only what symbol navigation needs (name, kind, span), not the whole
// model, and caps the station count so the file stays small on huge caves.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Therion.Core;
using Therion.Semantics;

namespace ThIDE.Services;

/// <summary>One indexed symbol: enough to display it and navigate to its declaration.</summary>
public sealed record IndexedSymbol(
    string Name, string Kind, string File,
    int StartLine, int StartColumn, int EndLine, int EndColumn, int StartOffset, int Length)
{
    /// <summary>Reconstructs the navigable declaration span.</summary>
    public SourceSpan ToSpan() => new(
        File, new SourceLocation(StartLine, StartColumn), new SourceLocation(EndLine, EndColumn),
        StartOffset, Length);
}

/// <summary>A persisted snapshot of a workspace's symbols, keyed by its root directory.</summary>
public sealed class WorkspaceSymbolIndex
{
    public string? Root { get; init; }
    public IReadOnlyList<IndexedSymbol> Symbols { get; init; } = Array.Empty<IndexedSymbol>();
    public static WorkspaceSymbolIndex Empty { get; } = new();
}

public interface IWorkspaceSymbolIndexStore
{
    /// <summary>Loads the persisted index for a root (null/none if absent).</summary>
    WorkspaceSymbolIndex? Load(string? root);
    /// <summary>Persists an index to disk (no-op when its root is null).</summary>
    void Save(WorkspaceSymbolIndex index);
    /// <summary>Builds an index from a freshly-computed semantic model.</summary>
    WorkspaceSymbolIndex Build(string? root, WorkspaceSemanticModel model, int stationLimit = 20000);
}

public sealed class WorkspaceSymbolIndexStore : IWorkspaceSymbolIndexStore
{
    private readonly string _dir;
    private readonly ILogger? _logger;
    // file paths + kinds repeat across thousands of station symbols — intern them so the
    // in-memory index holds one instance each instead of one per symbol.
    private readonly StringInterner _interner = new();

    public WorkspaceSymbolIndexStore(ILogger<WorkspaceSymbolIndexStore>? logger = null) : this(DefaultDir(), logger) { }

    public WorkspaceSymbolIndexStore(string dir, ILogger<WorkspaceSymbolIndexStore>? logger = null)
    {
        _dir = dir;
        _logger = logger;
    }

    public WorkspaceSymbolIndex Build(string? root, WorkspaceSemanticModel model, int stationLimit = 20000)
    {
        var symbols = new List<IndexedSymbol>();
        foreach (var kv in model.SurveysByFullName) Add(symbols, kv.Key, "survey", kv.Value.DeclarationSpan);
        foreach (var kv in model.ScrapsById)        Add(symbols, kv.Key, "scrap",  kv.Value.DeclarationSpan);
        foreach (var kv in model.MapsById)          Add(symbols, kv.Key, "map",    kv.Value.DeclarationSpan);
        int n = 0;
        foreach (var kv in model.StationsByQn)
        {
            Add(symbols, kv.Key, "station", kv.Value.DeclarationSpan);
            if (++n >= stationLimit) break;   // bound the index on huge caves
        }
        return new WorkspaceSymbolIndex { Root = root, Symbols = symbols };
    }

    private void Add(List<IndexedSymbol> list, string name, string kind, SourceSpan span)
    {
        if (string.IsNullOrEmpty(name) || span.IsEmpty || string.IsNullOrEmpty(span.FilePath)) return;
        list.Add(new IndexedSymbol(
            name, _interner.Intern(kind), _interner.Intern(span.FilePath),
            span.Start.Line, span.Start.Column, span.End.Line, span.End.Column, span.StartOffset, span.Length));
    }

    public WorkspaceSymbolIndex? Load(string? root)
    {
        if (string.IsNullOrEmpty(root)) return null;
        try
        {
            var path = PathFor(root);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<WorkspaceSymbolIndex>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load symbol index for {Root}.", root);
            return null;
        }
    }

    public void Save(WorkspaceSymbolIndex index)
    {
        if (string.IsNullOrEmpty(index.Root)) return;   // can't key a rootless (single-file) index
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(PathFor(index.Root!),
                JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist symbol index for {Root}.", index.Root);
        }
    }

    private string PathFor(string root)
    {
        string key;
        try { key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); }
        catch { key = root; }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToLowerInvariant())))[..16];
        return Path.Combine(_dir, hash + ".json");
    }

    private static string DefaultDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "symbol-index");
    }
}
