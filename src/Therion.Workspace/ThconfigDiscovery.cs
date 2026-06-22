// Recursive, bounded discovery of thconfig files under a workspace root.
// A file is a candidate when its extension is .thconfig/.thc, it is the bare
// `thconfig` Therion convention file, or the sniffer rates its content Likely.
// Mirrors the safety bounds used by ProjectEntryDiscovery (no unbounded walks).

using Therion.Processing.Abstractions;

namespace Therion.Workspace;

public static class ThconfigDiscovery
{
    /// <summary>Hard cap on entries inspected, guarding against pathological trees.</summary>
    public const int MaxFilesScanned = 5000;

    private static readonly string[] ConfigExtensions = { ".thconfig", ".thc" };

    /// <summary>
    /// Returns absolute paths of thconfig files found under <paramref name="rootDir"/>,
    /// ordered case-insensitively. Never throws; unreadable directories are skipped.
    /// </summary>
    public static IReadOnlyList<string> Scan(string rootDir, IThconfigSniffer sniffer)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir)) return results;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int budget = MaxFilesScanned;
        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(rootDir));

        while (stack.Count > 0 && budget > 0)
        {
            var dir = stack.Pop();

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                if (budget-- <= 0) break;
                if (IsCandidate(file, sniffer) && seen.Add(Path.GetFullPath(file)))
                    results.Add(Path.GetFullPath(file));
            }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    stack.Push(sub);
            }
            catch { /* skip unreadable directory */ }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    /// <summary>True when <paramref name="path"/> looks like a thconfig (extension or content sniff).</summary>
    public static bool IsCandidate(string path, IThconfigSniffer sniffer)
    {
        var ext = Path.GetExtension(path);
        foreach (var configExt in ConfigExtensions)
            if (string.Equals(ext, configExt, StringComparison.OrdinalIgnoreCase)) return true;

        // Bare extensionless `thconfig` (Therion convention).
        if (ext.Length == 0 &&
            string.Equals(Path.GetFileName(path), "thconfig", StringComparison.OrdinalIgnoreCase))
            return true;

        // Other extensionless / unusual names: fall back to the content sniffer.
        if (ext.Length == 0)
            return sniffer.Probe(path) == SnifferVerdict.Likely;

        return false;
    }
}
