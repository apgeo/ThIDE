// The Format all / Normalise preview says what changes and draws the whitespace it changes; an
// empty plan says "Nothing to change in N files" instead of closing.

using System.Globalization;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using ThIDE.Resources;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class EditPreviewTextTests
{
    [Fact]
    public void A_reformat_is_described_in_words_with_plurals()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            Assert.Equal("1 line: trailing spaces removed",
                EditPreviewViewModel.DescribeFormatChange(FormatAll.Describe("survey a  \nendsurvey\n", "survey a\nendsurvey\n")));
            Assert.Equal("2 lines: indentation changed, trailing spaces removed",
                EditPreviewViewModel.DescribeFormatChange(new FormatChange(2, FormatChangeKinds.Indentation | FormatChangeKinds.TrailingWhitespace, 1, "", "")));
            Assert.Equal("line endings changed",
                EditPreviewViewModel.DescribeFormatChange(new FormatChange(0, FormatChangeKinds.LineEndings, 1, "", "")));
        });
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () =>
        {
            Assert.StartsWith("1 linie: ", EditPreviewViewModel.DescribeFormatChange(new FormatChange(1, FormatChangeKinds.Indentation, 1, "", "")));
            Assert.StartsWith("5 linii: ", EditPreviewViewModel.DescribeFormatChange(new FormatChange(5, FormatChangeKinds.Indentation, 1, "", "")));
            Assert.StartsWith("20 de linii: ", EditPreviewViewModel.DescribeFormatChange(new FormatChange(20, FormatChangeKinds.Indentation, 1, "", "")));
        });
    }

    [Fact]
    public void Nothing_to_change_names_the_file_count_and_the_skipped_files()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            Assert.Equal("Nothing to change in 91 files.", EditPreviewViewModel.NothingToChange(91, 0));
            Assert.Equal("Nothing to change in 1 file.", EditPreviewViewModel.NothingToChange(1, 0));
            Assert.StartsWith("Nothing to change in 3 files. 2 more are left alone", EditPreviewViewModel.NothingToChange(3, 2));
        });
    }

    // The rename preview's counts: "2 occurrences across 1 file", and the Romanian forms, not "apariție(i)".
    [Fact]
    public void Counts_use_plural_forms()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
            Assert.StartsWith("2 occurrences across 1 file.",
                string.Format(Tr.Get("Rename_PreviewSummary"), Tr.Plural("Rename_Occurrences", 2), Tr.Plural("Rename_Files", 1))));
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () =>
        {
            Assert.Equal("1 apariție", Tr.Plural("Rename_Occurrences", 1));
            Assert.Equal("2 apariții", Tr.Plural("Rename_Occurrences", 2));
            Assert.Equal("101 fișiere", Tr.Plural("Rename_Files", 101));
            Assert.Equal("20 de fișiere", Tr.Plural("Rename_Files", 20));
            Assert.Equal("3 modificări", Tr.Plural("Rename_FileChanges", 3));
        });
    }

    [Fact]
    public void An_empty_plan_is_empty_and_a_described_row_shows_its_whitespace()
    {
        var empty = new EditPreviewViewModel("t", "s", new EditBatch([], [], []), [], null);
        Assert.True(empty.IsEmpty);

        var edit = new TextEdit(0, 1, "a", "b");
        var row = new EditPreviewRow("/c/a.th", 2, "\tcentreline  ", "  centreline", edit) { Description = "1 line" };
        var item = new EditPreviewViewModel("t", "s", new EditBatch([new FileTextEdits("/c/a.th", [edit])], [], []), [row], "/c").Items[0];
        Assert.True(item.HasDescription);
        Assert.Equal("→centreline··", item.Before);
        Assert.Equal("··centreline", item.After);
        Assert.Equal("a.th:2", item.Location);
    }
}
