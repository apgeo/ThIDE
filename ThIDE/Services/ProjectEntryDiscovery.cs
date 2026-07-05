// Auto-discovers the project entry point for a file the user opened directly
// (Plan: "Project scope = entry + auto-discover parent").
//
//   * .thconfig / .thc / extensionless  → the file *is* the entry.
//   * .th / .th2                          → walk up ancestor folders looking for a
//     .thconfig/.thc whose transitive source/input set contains the file. The first
//     match wins; if none is found we root the project at the file itself (downward only).

using System;
using System.Collections.Generic;
using System.IO;
using Therion.Syntax;

namespace ThIDE.Services;

public static class ProjectEntryDiscovery
{
    private const int MaxAncestorDepth = 8;
    private const int MaxFilesScanned = 2000; // safety bound for pathological trees

    /// <summary>Returns the project entry-point path for <paramref name="filePath"/>.</summary>
    public static string FindEntryPoint(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is "" or ".thconfig" or ".thc")
            return full;

        var dir = Path.GetDirectoryName(full);
        for (int depth = 0; depth < MaxAncestorDepth && !string.IsNullOrEmpty(dir) && Directory.Exists(dir); depth++)
        {
            foreach (var candidate in ConfigCandidates(dir))
            {
                if (string.Equals(candidate, full, StringComparison.OrdinalIgnoreCase)) continue;
                if (TransitivelyIncludes(candidate, full)) return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return full; // no parent project found — root at the file itself.
    }

    private static IEnumerable<string> ConfigCandidates(string dir)
    {
        IEnumerable<string> Safe(string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern); }
            catch { return Array.Empty<string>(); }
        }

        foreach (var p in Safe("*.thconfig")) yield return p;
        foreach (var p in Safe("*.thc")) yield return p;
        var bare = Path.Combine(dir, "thconfig"); // Therion convention: extensionless config
        if (File.Exists(bare)) yield return bare;
    }

    /// <summary>BFS over <c>source</c>/<c>input</c>/<c>load</c> edges; true if it reaches <paramref name="target"/>.</summary>
    private static bool TransitivelyIncludes(string entry, string target)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(entry));
        int budget = MaxFilesScanned;

        while (queue.Count > 0 && budget-- > 0)
        {
            var path = queue.Dequeue();
            if (!visited.Add(path)) continue;
            if (string.Equals(path, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (!File.Exists(path)) continue;

            TherionFile? file;
            try { file = DocumentParser.ParseByExtension(path, File.ReadAllText(path)).Value; }
            catch { continue; }
            if (file is null) continue;

            foreach (var dep in SourceGraph.Dependencies(file, path))
                if (!visited.Contains(dep)) queue.Enqueue(dep);
        }
        return false;
    }
}
