using Therion.Build;

namespace Therion.Build.Tests;

/// <summary>
/// Artefact discovery after a compile must not depend on the file system's clock agreeing with the
/// process clock: a share whose server runs a little behind, or a file system with coarse (2 s)
/// timestamps, stamps a freshly written output a moment before the compile started.
/// </summary>
public class OutputArtifactCollectorTimestampTests
{
    [Fact]
    public void Output_stamped_one_second_before_the_start_is_still_collected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thide-artifact-ts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var pdf = Path.Combine(dir, "cave.pdf");
            File.WriteAllText(pdf, "%PDF-1.4 invented");
            // a server clock one second behind, or a FAT 2 s mtime truncation
            File.SetLastWriteTimeUtc(pdf, startedAt.UtcDateTime.AddSeconds(-1));

            var found = new OutputArtifactCollector().Collect(dir, since: startedAt);

            Assert.True(found.Any(a => string.Equals(a.Path, pdf, StringComparison.Ordinal)),
                "an output written during the compile but stamped 1 s before its start was dropped");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
