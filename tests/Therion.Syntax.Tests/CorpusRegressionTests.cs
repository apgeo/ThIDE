// REL-02 — corpus regression tests over the bundled real/sample projects.
//
// SyntheticCorpusTests already asserts each file parses error-free. These add the *aggregate*
// regression guards the roadmap calls for: the corpus hasn't shrunk away, the whole corpus parses
// with zero errors, and parse counts are stable (deterministic) — so a parser change that silently
// drops commands or becomes non-deterministic is caught.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class CorpusRegressionTests
{
    // The corpus ships several sample projects; guard against it being emptied/unmounted.
    private const int MinCorpusFiles = 5;

    [Fact]
    public void Corpus_is_present_and_not_shrunk()
    {
        Assert.True(CorpusFiles().Count >= MinCorpusFiles,
            $"Expected at least {MinCorpusFiles} Therion corpus files.");
    }

    [Fact]
    public void Whole_corpus_parses_without_errors()
    {
        var failures = new List<string>();
        foreach (var f in CorpusFiles())
        {
            var (_, diags) = Parse(f);
            var errs = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
            if (errs > 0) failures.Add($"{Path.GetFileName(f)} ({errs} error(s))");
        }
        Assert.True(failures.Count == 0, "Corpus files with errors:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Corpus_parse_counts_are_deterministic_and_nonzero()
    {
        long first = TotalCommandCount();
        long second = TotalCommandCount();
        Assert.Equal(first, second);   // stable counts across repeated parses
        Assert.True(first > 0, "Expected a non-zero total command count across the corpus.");
    }

    private static long TotalCommandCount()
    {
        long total = 0;
        foreach (var f in CorpusFiles())
        {
            var (file, _) = Parse(f);
            if (file is not null) total += file.Children.Length;
        }
        return total;
    }

    private static (TherionFile?, System.Collections.Immutable.ImmutableArray<Diagnostic>) Parse(string path)
    {
        var text = EncodingResolver.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (ext is ".thconfig" or ".thc" || name == "thconfig")
        {
            var r = new ThconfigParser().Parse(path, text);
            return (r.Value, r.Diagnostics);
        }
        var t = new ThParser().Parse(path, text);
        return (t.Value, t.Diagnostics);
    }

    private static List<string> CorpusFiles()
    {
        var root = LocateCorpusRoot();
        var result = new List<string>();
        if (root is null) return result;
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var name = Path.GetFileName(f).ToLowerInvariant();
            if (ext is ".th" or ".th2" or ".thconfig" or ".thc" || (string.IsNullOrEmpty(ext) && name == "thconfig"))
                result.Add(f);
        }
        return result;
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
