// The real-world corpus under tests/Corpus/sample_projects is gitignored (local-only): it is
// present on a developer checkout and absent on CI. Cases that need it are marked [CorpusFact]
// so they run locally and report as *skipped* on CI, rather than failing there.
// (Therion.Blender.Tests carries its own copy — test assemblies don't reference each other.)

namespace Therion.Cli.Tests;

internal static class TestCorpus
{
    /// <summary>Walks up from the test output dir to a non-empty tests/Corpus/sample_projects.</summary>
    public static string? SampleProjectsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Corpus", "sample_projects");
            if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static bool Available => SampleProjectsRoot() is not null;

    /// <summary>The small real-world .lox export used as the CLI export smoke input.</summary>
    public static string AvCerbulLox()
    {
        var root = SampleProjectsRoot();
        Assert.True(root is not null, "tests/Corpus/sample_projects not found above the test output dir.");
        var path = Path.Combine(root!, "av_cerbul_de_aur", "therion", "rez", "av_cerbul_de_aur_20260505_v1.lox");
        Assert.True(File.Exists(path), $"Expected corpus file missing: {path}");
        return path;
    }
}

public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!TestCorpus.Available)
            Skip = "Real-world corpus (tests/Corpus/sample_projects) is local-only and not present here.";
    }
}
