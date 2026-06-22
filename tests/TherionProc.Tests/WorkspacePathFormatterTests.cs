using System.IO;
using TherionProc.Services;

namespace TherionProc.Tests;

public class WorkspacePathFormatterTests
{
    [Fact]
    public void Relativize_returns_relative_path_when_inside_root()
    {
        var root = Path.GetFullPath(Path.Combine("proj"));
        var file = Path.Combine(root, "sub", "cave.thconfig");

        var result = WorkspacePathFormatter.Relativize(root, file);

        Assert.Equal(Path.Combine("sub", "cave.thconfig"), result);
    }

    [Fact]
    public void Relativize_returns_full_path_when_outside_root()
    {
        var root = Path.GetFullPath(Path.Combine("proj"));
        var outside = Path.GetFullPath(Path.Combine("elsewhere", "cave.thconfig"));

        var result = WorkspacePathFormatter.Relativize(root, outside);

        Assert.Equal(outside, result);
    }

    [Fact]
    public void Relativize_returns_input_when_root_is_null()
    {
        const string file = "/some/path/cave.thconfig";
        Assert.Equal(file, WorkspacePathFormatter.Relativize(null, file));
    }

    [Fact]
    public void Truncate_leaves_short_text_unchanged()
    {
        const string text = "sub/cave.thconfig";
        Assert.Equal(text, WorkspacePathFormatter.Truncate(text, 48));
    }

    [Fact]
    public void Truncate_keeps_filename_and_inserts_middle_ellipsis()
    {
        var text = "a/very/deeply/nested/directory/structure/that/is/long/cave.thconfig";

        var result = WorkspacePathFormatter.Truncate(text, 30);

        Assert.True(result.Length <= 30);
        Assert.Contains("…", result);
        Assert.EndsWith("cave.thconfig", result); // filename always preserved
    }

    [Fact]
    public void Truncate_shows_filename_tail_when_filename_alone_exceeds_budget()
    {
        var text = "dir/a-really-extremely-long-file-name.thconfig";

        var result = WorkspacePathFormatter.Truncate(text, 12);

        Assert.True(result.Length <= 12);
        Assert.StartsWith("…", result);
    }

    [Fact]
    public void Display_combines_relativize_and_truncate()
    {
        var root = Path.GetFullPath("proj");
        var file = Path.Combine(root, "cave.thconfig");

        Assert.Equal("cave.thconfig", WorkspacePathFormatter.Display(root, file));
        Assert.Equal(string.Empty, WorkspacePathFormatter.Display(root, ""));
    }
}
