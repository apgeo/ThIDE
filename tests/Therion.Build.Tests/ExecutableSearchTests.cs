// A program named without a folder must never be found in ThIDE's current directory (often a project
// folder) on Windows. The rules are tested with Windows paths on every platform; the real start is
// checked on Windows only.

using Therion.Build.Automation;

namespace Therion.Build.Tests;

public class ExecutableSearchTests
{
    private const string System32 = @"C:\Windows\System32";
    private const string Windows = @"C:\Windows";
    private const string Project = @"C:\caves\demo";

    private static string? Resolve(string name, string? cwd, string? path, params string[] existing)
    {
        var files = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return ExecutableSearch.ResolveOnWindows(name, cwd, path, [System32, Windows], files.Contains);
    }

    [Fact]
    public void A_bare_name_is_found_on_PATH_and_not_in_the_working_directory()
    {
        var found = Resolve("python", Project, @"C:\Python312;C:\Tools",
            @"C:\caves\demo\python.exe", @"C:\Python312\python.exe");

        Assert.Equal(@"C:\Python312\python.exe", found);
    }

    [Fact]
    public void A_bare_name_found_only_in_the_working_directory_is_refused()
        => Assert.Null(Resolve("python", Project, @"C:\Tools", @"C:\caves\demo\python.exe"));

    [Theory]
    [InlineData(".")]
    [InlineData(@".\bin")]
    [InlineData("bin")]
    [InlineData(@"C:relative")]
    [InlineData(@"\rooted-on-current-drive")]
    public void A_relative_PATH_entry_is_never_searched(string entry)
        => Assert.Null(Resolve("git", Project, entry, @"C:\caves\demo\git.exe", @"C:\caves\demo\bin\git.exe",
            @"bin\git.exe", @".\git.exe", @"C:relative\git.exe", @"\rooted-on-current-drive\git.exe"));

    [Fact]
    public void The_system_folders_come_before_PATH()
        => Assert.Equal(@"C:\Windows\System32\where.exe",
            Resolve("where", Project, @"C:\Tools", @"C:\Tools\where.exe", @"C:\Windows\System32\where.exe"));

    [Fact]
    public void A_quoted_PATH_entry_is_searched_without_its_quotes()
        => Assert.Equal(@"C:\Program Files\Git\cmd\git.exe",
            Resolve("git", Project, "\"C:\\Program Files\\Git\\cmd\";C:\\Tools", @"C:\Program Files\Git\cmd\git.exe"));

    [Fact]
    public void A_name_with_an_extension_is_looked_up_as_written()
        => Assert.Equal(@"C:\Tools\build.cmd", Resolve("build.cmd", Project, @"C:\Tools", @"C:\Tools\build.cmd", @"C:\Tools\build.cmd.exe"));

    [Fact]
    public void A_full_path_is_used_as_given_when_it_exists()
    {
        Assert.Equal(@"D:\therion\therion.exe", Resolve(@"D:\therion\therion.exe", Project, null, @"D:\therion\therion.exe"));
        Assert.Equal(@"\\server\share\tool.exe", Resolve(@"\\server\share\tool.exe", Project, null, @"\\server\share\tool.exe"));
        Assert.Null(Resolve(@"D:\therion\therion.exe", Project, @"C:\Tools", @"C:\Tools\therion.exe"));
    }

    [Fact]
    public void A_relative_path_with_a_folder_is_taken_from_the_folder_the_program_starts_in()
    {
        Assert.Equal(@"C:\caves\demo\tools\build.cmd", Resolve(@"tools\build.cmd", Project, null, @"C:\caves\demo\tools\build.cmd"));
        Assert.Null(Resolve(@"tools\build.cmd", null, null, @"tools\build.cmd"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void An_empty_name_resolves_to_nothing(string name)
        => Assert.Null(Resolve(name, Project, @"C:\Tools", @"C:\Tools\.exe"));

    [Fact]
    public void Off_Windows_the_name_is_left_to_the_platform()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("sh", ExecutableSearch.Resolve("sh", "/tmp"));
    }

    [Fact]
    public async Task On_Windows_a_program_beside_the_task_is_not_started_by_bare_name()
    {
        if (!OperatingSystem.IsWindows()) return;   // CreateProcess's lookup order is Windows-only
        var dir = Path.Combine(Path.GetTempPath(), "thide-exe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var name = "thide-planted-" + Guid.NewGuid().ToString("N")[..8];
            File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), Path.Combine(dir, name + ".exe"));

            Assert.Throws<FileNotFoundException>(() => ProcessRunner.StartInfo(new ProcessSpec(name, [], WorkingDirectory: dir)));
            var outcome = await ProcessRunner.RunAsync(new ProcessSpec(name, [], WorkingDirectory: dir));
            Assert.NotNull(outcome.StartError);

            var psi = ProcessRunner.StartInfo(new ProcessSpec("where", ["cmd"], WorkingDirectory: dir));
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "where.exe"), psi.FileName, ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
