using Therion.Cli;
using Therion.Testing;

namespace Therion.Cli.Tests;

/// <summary>
/// <c>therion-cli format --write</c> against the shared fixture matrix: every Therion encoding and
/// line-ending style, lossy files, links, modes, read-only files and folders, a case-variant sibling.
/// Each file is rewritten exactly (BOM + re-indented text in its own encoding) or left byte-identical
/// with a non-zero exit code.
/// </summary>
public class FormatWritePathConformanceTests
{
    public static TheoryData<string> AllFixtures => WriteFixtures.Names();

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Format_write_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int exit = FormatVerb.Run(["format", "--write", placed.Path], stdout, stderr);

        WritePathConformance.Check(placed, WriterKind.Reindent, exit == 0, "therion-cli format --write");
    }

    /// <summary>--check never writes, whatever the file holds.</summary>
    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Format_check_reports_drift_and_writes_nothing(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        int exit = FormatVerb.Run(["format", "--check", placed.Path], new StringWriter(), new StringWriter());

        Assert.Equal(1, exit);   // every fixture is unindented, so every one drifts
        Assert.Equal(placed.Fixture.Bytes, File.ReadAllBytes(placed.ContentPath));
    }
}
