// #3 — Go-to-File (Ctrl+P) data source. Gathers candidate files from the configured sources
// (history, workspace directory, and/or thconfig-connected) and ranks them for a query: name
// matches outrank path-only matches; space-separated tokens may each match any part of the name
// or path. A rooted path that exists is offered directly (file → open; directory → load workspace).
// Files the active thconfig compiles come first; the others are greyed and say so, so an archived
// copy of a survey is never mistaken for the one that is built.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using ThIDE.Resources;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.Services;

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
            Tr.Get("QuickOpen_Title"),
            Tr.Get("QuickOpen_Watermark"),
            text => Rank(candidates, text, loadFolder));
    }

    // ---- candidate gathering ----

    private List<QuickPickItem> BuildCandidates()
    {
        var sources = _settings.Current.QuickOpenSources;
        var root = _session.RootPath;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<QuickPickItem>();
        bool dir = sources.HasFlag(QuickOpenSources.Directory);
        bool connectedOnly = sources.HasFlag(QuickOpenSources.ThconfigConnected) && !dir;
        // Only with a built graph: while the thconfig is still loading nothing would count as in it.
        var config = _session.Model is not null ? _session.ActiveThconfig : null;
        string? notIn = config is null ? null : string.Format(Tr.Get("QuickOpen_NotInConfig"), Path.GetFileName(config.FullPath));

        void Add(string p)
        {
            bool outside = notIn is not null && !_session.Covers(p);
            // The connected-only scope applies to recent and open files too: a recently opened
            // archived copy would otherwise still head the list.
            if (outside && connectedOnly) return;
            list.Add(FileItem(p, root, outside ? notIn : null));
        }

        if (sources.HasFlag(QuickOpenSources.History))
        {
            foreach (var p in _settings.Current.RecentFiles)
                if (seen.Add(p) && File.Exists(p)) Add(p);
            foreach (var d in _docs.Documents)
                if (d.FilePath is { Length: > 0 } fp && seen.Add(fp) && File.Exists(fp))
                    Add(fp);
        }

        if ((dir || connectedOnly) && !string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            foreach (var p in EnumerateTherionFiles(root!))
                if (seen.Add(p)) Add(p);
        }
        // Stable: within each group the history-then-folder order is kept.
        return list.OrderBy(i => i.IsDimmed).ToList();
    }

    private static IEnumerable<string> EnumerateTherionFiles(string root)
    {
        // Not into symlinked folders: `all -> /` in a project would otherwise walk the whole disk.
        int n = 0;
        foreach (var f in ProjectFileWalk.EnumerateFiles(root, "*"))
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
        foreach (var s in scored.OrderBy(s => s.Item.IsDimmed).ThenByDescending(s => s.Score).ThenBy(s => s.Index).Take(MaxResults))
            results.Add(s.Item);
        return results;
    }

    // ---- item factories ----

    private QuickPickItem FileItem(string path, string? root, string? notInConfig)
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
        if (notInConfig is not null) detail = string.IsNullOrEmpty(detail) ? notInConfig : detail + "  " + notInConfig;

        return new QuickPickItem
        {
            Title = name,
            Detail = detail,
            IsDimmed = notInConfig is not null,
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
