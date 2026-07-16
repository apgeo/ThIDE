// The real-world corpus under tests/Corpus/sample_projects is gitignored (local-only): it is
// present on a developer checkout and absent on CI. Cases that need it are marked [CorpusFact]
// so they run locally and report as *skipped* on CI, rather than failing there.

namespace Therion.Blender.Tests;

public sealed class CorpusFactAttribute : FactAttribute
{
    public CorpusFactAttribute()
    {
        if (!TestCorpus.SampleProjectsAvailable)
            Skip = "Real-world corpus (tests/Corpus/sample_projects) is local-only and not present here.";
    }
}
