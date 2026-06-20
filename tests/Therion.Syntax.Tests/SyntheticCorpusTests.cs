using System.IO;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

/// <summary>
/// Implementation Plan §11 — corpus tests. Asserts every bundled sample
/// file parses with zero error diagnostics in lenient mode.
/// </summary>
public class SyntheticCorpusTests
{
    public static TheoryData<string> CorpusFiles
    {
        get
        {
            var data = new TheoryData<string>();
            var root = LocateCorpusRoot();
            if (root is null) return data;

            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                var name = Path.GetFileName(f).ToLowerInvariant();
                bool isTherion =
                    ext is ".th" or ".th2" or ".thconfig" or ".thc" ||
                    string.IsNullOrEmpty(ext) && name == "thconfig";
                if (isTherion) data.Add(f);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Corpus_file_parses_without_errors(string path)
    {
        var text = EncodingResolver.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileName(path).ToLowerInvariant();

        var (file, diagnostics) = (ext, name) switch
        {
            (".thconfig" or ".thc", _) => RunThconfig(path, text),
            (_, "thconfig")            => RunThconfig(path, text),
            _                          => RunTh(path, text),
        };

        Assert.NotNull(file);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0,
            $"Expected no errors but got:\n{string.Join("\n", errors.Select(e => e.ToString()))}");
    }

    private static (TherionFile?, System.Collections.Immutable.ImmutableArray<Diagnostic>) RunThconfig(string p, string t)
    {
        var r = new ThconfigParser().Parse(p, t);
        return (r.Value, r.Diagnostics);
    }

    private static (TherionFile?, System.Collections.Immutable.ImmutableArray<Diagnostic>) RunTh(string p, string t)
    {
        var r = new ThParser().Parse(p, t);
        return (r.Value, r.Diagnostics);
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
