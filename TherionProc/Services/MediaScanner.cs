// MEDIA-02 — background-scan / media manager.
//
// Builds a list of the project's scan assets: the .xvi files referenced by scraps, with grid /
// georeferencing info and how many scraps trace each. Flags any referenced file that is missing on
// disk. MEDIA-03 extends this with a disk scan for present-but-unreferenced (orphan) media.
//
// Walks the project, so the app gates it behind the EnableMediaScan setting.

using System;
using System.Collections.Generic;
using System.IO;
using Therion.Semantics;

namespace TherionProc.Services;

public enum MediaStatus { Referenced, Missing, Orphan }

public sealed record MediaItem(
    string Path, string Type, MediaStatus Status, string Resolution, int ReferencingScraps, bool Georeferenced)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string StatusText => Status.ToString();
    public string Georef => Georeferenced ? "yes" : "—";
}

public static class MediaScanner
{
    /// <summary>The .xvi scans referenced by the project's scraps, flagging any missing on disk.</summary>
    public static List<MediaItem> ScanReferenced(WorkspaceSemanticModel? workspace)
    {
        var items = new List<MediaItem>();
        if (workspace is null) return items;

        foreach (var sym in workspace.Xvi.ByPath.Values)
        {
            var path = TryFull(sym.ResolvedXviPath);
            bool exists = SafeExists(path);
            var grid = sym.File?.Grid is { } g ? $"{g.CountX}×{g.CountY} grid" : "—";
            items.Add(new MediaItem(
                path, "xvi", exists ? MediaStatus.Referenced : MediaStatus.Missing,
                grid, sym.ReferencingScraps.Length, Georeferenced: sym.File?.Grid is not null));
        }
        return items;
    }

    internal static bool SafeExists(string path) { try { return File.Exists(path); } catch { return false; } }
    internal static string TryFull(string p) { try { return Path.GetFullPath(p); } catch { return p; } }
}
