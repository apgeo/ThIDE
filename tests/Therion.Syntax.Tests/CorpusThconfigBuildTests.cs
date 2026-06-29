// REL-02 (corpus) — "thconfig build" regression: for every .thconfig in the corpus, walk its
// cross-file SOURCE GRAPH (source/input/load, recursing into survey/centreline blocks) the way
// TherionWorkspace loads a project, parse every reachable file, and assert the assembled project
// parses without error diagnostics.
//
// Missing referenced files (generated outputs, context-dependent absolute paths, images we don't
// ship) are EXPECTED for downloaded real-world projects and are tolerated — they are reported in
// the assert message but never fail the build. Only an actual parse error in a file that exists
// fails the test. Runs against whatever corpus is present (synthetic on CI; synthetic + the
// gitignored sample_projects locally).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class CorpusThconfigBuildTests
{
    [Fact]
    public void Thconfig_projects_build_without_parse_errors()
    {
        var root = LocateCorpusRoot();
        if (root is null) return;

        var entryPoints = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsThconfig).ToList();
        if (entryPoints.Count == 0) return;

        var parseErrors = new List<string>();
        int projects = 0, filesParsed = 0, unresolved = 0;

        foreach (var entry in entryPoints)
        {
            projects++;
            var visited = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(Path.GetFullPath(entry));

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                if (!visited.Add(path)) continue;
                if (!File.Exists(path)) { unresolved++; continue; }

                filesParsed++;
                var (file, diags) = Parse(path);
                foreach (var e in diags.Where(d => d.Severity == DiagnosticSeverity.Error))
                    parseErrors.Add($"{Path.GetRelativePath(root, path)}: {e.Code.Value} {e.Message}");

                if (file is null) continue;
                foreach (var dep in SourceGraph.Dependencies(file, path))
                    foreach (var candidate in Candidates(dep))
                        if (!visited.Contains(candidate)) queue.Enqueue(candidate);
            }
        }

        Assert.True(parseErrors.Count == 0,
            $"Built {projects} thconfig project(s), parsed {filesParsed} reachable file(s), " +
            $"{unresolved} unresolved include(s) (tolerated). Parse errors:\n" +
            string.Join("\n", parseErrors.Take(50)));
    }

    // Therion source tokens may omit the extension (`input foo` → foo.th); try the common forms.
    private static IEnumerable<string> Candidates(string dep)
    {
        yield return dep;
        if (Path.GetExtension(dep).Length == 0)
        {
            yield return dep + ".th";
            yield return dep + ".th2";
            yield return dep + ".thconfig";
        }
    }

    private static bool IsThconfig(string f)
    {
        var ext = Path.GetExtension(f).ToLowerInvariant();
        return ext is ".thconfig" or ".thc" ||
               (string.IsNullOrEmpty(ext) && Path.GetFileName(f).ToLowerInvariant() == "thconfig");
    }

    private static (TherionFile?, System.Collections.Immutable.ImmutableArray<Diagnostic>) Parse(string path)
    {
        var text = EncodingResolver.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (ext is ".thconfig" or ".thc" || name == "thconfig")
        { var r = new ThconfigParser().Parse(path, text); return (r.Value, r.Diagnostics); }
        if (ext == ".th2")
        { var r = new Th2Parser().Parse(path, text); return (r.Value, r.Diagnostics); }
        var t = new ThParser().Parse(path, text);
        return (t.Value, t.Diagnostics);
    }

    private static string? LocateCorpusRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
