namespace ThIDE.Tests;

public class StartupFileArgsTests
{
    [Fact]
    public void Absolute_paths_are_kept_on_unix()
    {
        if (OperatingSystem.IsWindows()) return; // '/' introduces an option there
        var file = Path.GetTempFileName();
        try
        {
            Assert.Equal(new[] { file }, App.ParseFileArgs(new[] { file }));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Options_and_missing_files_are_skipped()
    {
        var file = Path.GetTempFileName();
        try
        {
            var result = App.ParseFileArgs(new[] { "--verbose", "-x", "", Path.Combine(Path.GetTempPath(), "no-such-file.th"), file });
            Assert.Equal(new[] { Path.GetFullPath(file) }, result);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Slash_options_are_skipped_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Empty(App.ParseFileArgs(new[] { "/safe" }));
    }
}
