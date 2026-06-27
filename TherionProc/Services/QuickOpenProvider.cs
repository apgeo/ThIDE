// #3 — Go-to-File (Ctrl+P) data source. Gathers candidate files from the configured sources
// (history, workspace directory, and/or thconfig-connected) and ranks them for a query: name
// matches outrank path-only matches; space-separated tokens may each match any part of the name
// or path. A rooted path that exists is offered directly (file → open; directory → load workspace).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using TherionProc.ViewModels.QuickPick;

namespace TherionProc.Services;

public sealed class QuickOpenProvider
{
    private static readonly string[] TherionExtensions =
        { ".th", ".th2", ".thconfig", ".thc", ".thl", ".xvi" };
    private const int MaxScan = 8000;
    private const int MaxResults = 500;

    private readonly IDocumentService _docs;
    private readonly IWorkspaceSession _session;
    private readonly IAppSettingsService _settings;
    private readonly IFileIconProvider _icons;

    public QuickOpenProvider(IDocumentService docs, IWorkspaceSession session,
        IAppSettingsService settings, IFileIconProvider icons)
    {
        _docs = docs;
        _session = session;
        _settings = settings;
        _icons = icons;
    }

    /// <summary>Builds the Ctrl+P palette. <paramref name="loadFolder"/> handles a typed directory path.</summary>
    public QuickPickViewModel CreatePalette(Func<string, Task> loadFolder)
    {
        var candidates = BuildCandidates();
        return new QuickPickViewModel(
            "Go to File",
            "Search files by name or path…  (space = match parts)",
            text => Rank(candidates, text, loadFolder));
    }

    // ---- candidate gathering ----

    private List<QuickPickItem> BuildCandidates()
    {
        var sources = _settings.Current.QuickOpenSources;
        var root = _session.RootPath;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<QuickPickItem>();

        if (sources.HasFlag(QuickOpenSources.History))
        {
            foreach (var p in _settings.Current.RecentFiles)
                if (seen.Add(p) && File.Exists(p)) list.Add(FileItem(p, root));
            foreach (var d in _docs.Documents)
                if (d.FilePath is { Length: > 0 } fp && seen.Add(fp) && File.Exists(fp))
                    list.Add(FileItem(fp, root));
        }

        bool dir = sources.HasFlag(QuickOpenSources.Directory);
        bool connectedOnly = sources.HasFlag(QuickOpenSources.ThconfigConnected) && !dir;
        if ((dir || connectedOnly) && !string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            foreach (var p in EnumerateTherionFiles(root!))
            {
                if (seen.Contains(p)) continue;
                if (connectedOnly && !_session.Covers(p)) continue;
                if (seen.Add(p)) list.Add(FileItem(p, root));
            }
        }
        return list;
    }

    private static IEnumerable<string> EnumerateTherionFiles(string root)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", opts); }
        catch { yield break; }
        int n = 0;
        foreach (var f in files)
        {
            if (Array.IndexOf(TherionExtensions, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
            yield return f;
            if (++n >= MaxScan) yield break;
        }
    }

    // ---- ranking ----

    private IReadOnlyList<QuickPickItem> Rank(List<QuickPickItem> candidates, string text, Func<string, Task> loadFolder)
    {
        var query = (text ?? string.Empty).Trim();
        var results = new List<QuickPickItem>();

        // A rooted path that exists is offered directly, even when it's not in the list.
        if (query.Length > 2 && Path.IsPathRooted(query))
        {
            if (File.Exists(query)) results.Add(OpenPathItem(query));
            else if (Directory.Exists(query)) results.Add(LoadFolderItem(query, loadFolder));
        }

        if (query.Length == 0)
        {
            results.AddRange(candidates.Take(MaxResults));
            return results;
        }

        var lc = query.ToLowerInvariant();
        var scored = new List<(QuickPickItem Item, int Score, int Index)>();
        for (int i = 0; i < candidates.Count; i++)
            if (QuickPickMatcher.Score(lc, candidates[i].NameLower, candidates[i].PathLower) is { } sc)
                scored.Add((candidates[i], sc, i));
        foreach (var s in scored.OrderByDescending(s => s.Score).ThenBy(s => s.Index).Take(MaxResults))
            results.Add(s.Item);
        return results;
    }

    // ---- item factories ----

    private QuickPickItem FileItem(string path, string? root)
    {
        var (icon, key) = IconFor(path);
        var name = Path.GetFileName(path);
        string? detail = null;
        if (!string.IsNullOrEmpty(root))
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetRelativePath(root!, path));
                detail = string.IsNullOrEmpty(dir) || dir == "." ? null : dir;
            }
            catch { detail = Path.GetDirectoryName(path); }
        }
        else detail = Path.GetDirectoryName(path);

        return new QuickPickItem
        {
            Title = name,
            Detail = detail,
            Icon = icon,
            IconKey = key,
            NameLower = name.ToLowerInvariant(),
            PathLower = path.ToLowerInvariant(),
            Payload = path,
            Run = () => _docs.OpenFileAsync(path),
        };
    }

    private QuickPickItem OpenPathItem(string path) => new()
    {
        Title = Path.GetFileName(path),
        Detail = $"Open  {path}",
        IconKey = IconFor(path).Key ?? "Icon.File",
        Icon = IconFor(path).Icon,
        Run = () => _docs.OpenFileAsync(path),
    };

    private static QuickPickItem LoadFolderItem(string path, Func<string, Task> loadFolder) => new()
    {
        Title = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        Detail = $"Open folder as workspace  {path}",
        IconKey = "Icon.FolderOpen",
        Run = () => loadFolder(path),
    };

    private (IImage? Icon, string? Key) IconFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".thconfig" or ".thc" or ".thl") return (null, "Icon.Config");
        if (ext == ".th2") return (null, "Icon.Map");
        var native = _icons.GetIcon(path, false);
        return native is not null ? (native, null) : (null, "Icon.File");
    }
}
