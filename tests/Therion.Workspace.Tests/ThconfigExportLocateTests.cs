using Therion.Workspace;

namespace Therion.Workspace.Tests;

/// <summary>
/// Finding an export target, listed from a thconfig as it was, in the thconfig as it is now: the
/// lines above it may have changed, the exports may have been reordered, or the export itself edited.
/// </summary>
public class ThconfigExportLocateTests
{
    private const string Before = "source cave.th\nexport map -o plan.pdf\nexport model -o cave.lox\n";

    private static ExportTargetInfo Listed(string output) =>
        ThconfigExportEditor.ParseExports(Before).Single(e => e.Output == output);

    [Fact]
    public void An_unchanged_export_is_found_where_it_was()
    {
        var found = ThconfigExportEditor.Locate(Before, Listed("plan.pdf"));
        Assert.Equal(("plan.pdf", 2), (found?.Output, found?.StartLine));
    }

    [Fact]
    public void An_export_moved_by_a_line_above_it_is_found_by_its_title()
    {
        var now = "source cave.th\n# a note\nexport map -o plan.pdf\nexport model -o cave.lox\n";
        var found = ThconfigExportEditor.Locate(now, Listed("plan.pdf"));
        Assert.Equal(("plan.pdf", 3), (found?.Output, found?.StartLine));
    }

    [Fact]
    public void Another_export_moved_onto_its_lines_is_not_taken_for_it()
    {
        var now = "source cave.th\nexport model -o cave.lox\nexport map -o plan.pdf\n";
        var found = ThconfigExportEditor.Locate(now, Listed("plan.pdf"));
        Assert.Equal(("plan.pdf", 3), (found?.Output, found?.StartLine));
    }

    [Fact]
    public void An_export_edited_where_it_stands_is_found_at_its_lines()
    {
        var now = "source cave.th\nexport map -o plan2.pdf\nexport model -o cave.lox\n";
        var found = ThconfigExportEditor.Locate(now, Listed("plan.pdf"));
        Assert.Equal(("plan2.pdf", 2), (found?.Output, found?.StartLine));
    }

    [Fact]
    public void An_export_both_moved_and_edited_is_not_found()
    {
        var now = "source cave.th\n# a note\nexport map -o plan2.pdf\nexport model -o cave.lox\n";
        Assert.Null(ThconfigExportEditor.Locate(now, Listed("plan.pdf")));
    }

    [Fact]
    public void Two_exports_with_its_title_elsewhere_are_ambiguous()
    {
        var now = "# a note\nsource cave.th\nexport map -o plan.pdf\nexport map -o plan.pdf\n";
        Assert.Null(ThconfigExportEditor.Locate(now, Listed("plan.pdf")));
    }
}
