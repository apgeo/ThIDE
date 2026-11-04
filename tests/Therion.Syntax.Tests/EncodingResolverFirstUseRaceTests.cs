// The first legacy-encoding decode in a process registers the code-page provider. Two threads that
// get there together must both see it registered before they decode, or one falls back to UTF-8 and
// the file opens as mojibake. Registration is process-global (and other tests here register the
// provider themselves), so each round runs a fresh child process: tests/probes/EncodingRaceProbe.

using System.Diagnostics;
using System.Reflection;

namespace Therion.Syntax.Tests;

public class EncodingResolverFirstUseRaceTests
{
    private static string ProbeDll { get; } =
        typeof(EncodingResolverFirstUseRaceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EncodingRaceProbeDll").Value
        ?? throw new InvalidOperationException("EncodingRaceProbeDll assembly metadata is missing.");

    private static (int ExitCode, string Output) RunProbe()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { ProbeDll, "16" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "the probe did not exit");
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [Fact]
    public void Concurrent_first_decodes_of_a_code_page_file_all_get_the_legacy_text()
    {
        Assert.True(File.Exists(ProbeDll), $"Probe not built at {ProbeDll}. Build the solution first.");

        // 20 fresh processes, four at a time: each is one first use with 16 threads at a barrier.
        var rounds = Enumerable.Range(0, 20).AsParallel().WithDegreeOfParallelism(4)
            .Select(_ => RunProbe()).ToList();

        var failed = rounds.Where(r => r.ExitCode != 0).ToList();
        Assert.True(failed.Count == 0,
            $"{failed.Count} of {rounds.Count} rounds decoded differently:\n" +
            string.Join("\n", failed.Select(f => $"exit {f.ExitCode}: {f.Output.Trim()}")));
    }
}
