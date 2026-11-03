using Therion.Processing.Abstractions;

namespace Therion.Workspace.Tests;

/// <summary>
/// The content sniffer decides whether an extensionless file (and, for the folder resolver, any
/// non-config extension) is a thconfig. A single config keyword anywhere in the text must not be
/// enough: `source`, `input`, `encoding`, `text`, `log` and `system` are ordinary words and ordinary
/// survey-file commands.
/// </summary>
public class ThconfigSnifferTests
{
    [Fact]
    public void Prose_is_not_a_thconfig()
    {
        using var dir = new TempDir();
        var readme = Path.Combine(dir.Path, "README");
        File.WriteAllText(readme, "Import the source files.\n");

        var verdict = new ThconfigSniffer().Probe(readme);

        Assert.Equal(SnifferVerdict.Unlikely, verdict);
    }

    [Fact]
    public void A_survey_file_that_declares_its_encoding_is_not_a_thconfig()
    {
        using var dir = new TempDir();
        var th = Path.Combine(dir.Path, "cave.th");
        File.WriteAllText(th, "encoding utf-8\nsurvey cave\n  centreline\n  endcentreline\nendsurvey\n");

        var verdict = new ThconfigSniffer().Probe(th);

        Assert.Equal(SnifferVerdict.Unlikely, verdict);
    }

    [Fact]
    public void Two_config_commands_at_line_start_make_a_thconfig()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "build");
        File.WriteAllText(cfg, "# cave build\nsource cave.th\n  export map -o cave.pdf\n");

        Assert.Equal(SnifferVerdict.Likely, new ThconfigSniffer().Probe(cfg));
    }

    [Fact]
    public void Config_words_inside_a_line_do_not_count()
    {
        using var dir = new TempDir();
        var notes = Path.Combine(dir.Path, "NOTES");
        File.WriteAllText(notes, "Remember to export the map and select the source survey.\nsource is in git.\n");

        Assert.Equal(SnifferVerdict.Unlikely, new ThconfigSniffer().Probe(notes));
    }
}
