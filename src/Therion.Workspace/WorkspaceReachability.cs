// Pure filesystem reachability over Therion's source/input/load graph.
//
// Used by the Overview ▸ Audit "orphan files" scan and by the MCP `list_files orphansOnly` tool to
// decide which .th/.th2 files on disk are truly orphans — bound to *no* thconfig in the workspace,
// not merely absent from the *active* entry point's graph. Answering that means walking the source
// graph of every thconfig in the directory, which is why the audit only runs on demand (see
// ProjectAuditViewModel). This helper does the heavy walk without watchers, disk cache or semantic
// binding, so it is cheap enough to run off the UI thread. It intentionally mirrors
// ProjectEntryDiscovery's BFS (same SourceGraph edges) so the two never disagree about what
// "reachable" means.

using Therion.Syntax;

namespace Therion.Workspace;

public static class WorkspaceReachability
{
    private const int MaxFilesScanned = 20000; // safety bound for pathological trees

    /// <summary>
    /// The set of absolute file paths reachable via <c>source</c>/<c>input</c>/<c>load</c> from any of
    /// <paramref name="entryPoints"/> (each entry point itself included). Case-insensitive; missing or
    /// unparseable files are skipped.
    /// </summary>
    public static HashSet<string> ReachableFrom(IEnumerable<string> entryPoints, CancellationToken ct = default)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var e in entryPoints)
        {
            if (string.IsNullOrEmpty(e)) continue;
            try { queue.Enqueue(Path.GetFullPath(e)); } catch { /* malformed path — skip */ }
        }

        int budget = MaxFilesScanned;
        while (queue.Count > 0 && budget-- > 0)
        {
            ct.ThrowIfCancellationRequested();
            var path = queue.Dequeue();
            if (!visited.Add(path)) continue;
            if (!File.Exists(path)) continue;

            TherionFile? file;
            try { file = TherionWorkspace.ParseText(path, EncodingResolver.ReadAllText(path)).Value; }
            catch { continue; }
            if (file is null) continue;

            foreach (var dep in SourceGraph.Dependencies(file, path))
                if (!visited.Contains(dep)) queue.Enqueue(dep);
        }
        return visited;
    }
}
