namespace Therion.Mcp.Tests;

/// <summary>
/// The jail is the one place a prompt-injected .th file could reach the rest of the disk, so the
/// escapes matter more than the happy path.
/// </summary>
public class WorkspacePathsTests
{
    [Theory]
    [InlineData("caves/upper.th")]
    [InlineData("./caves/upper.th")]
    [InlineData("caves/../caves/upper.th")]
    public void Accepts_relative_paths_inside_the_root(string relative)
    {
        using var fixture = FixtureWorkspace.Create();

        Assert.True(WorkspacePaths.TryResolve(fixture.Root, relative, out var full, out var error));
        Assert.Null(error);
        Assert.Equal(fixture.PathTo("caves", "upper.th"), full, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Accepts_an_absolute_path_that_stays_inside()
    {
        using var fixture = FixtureWorkspace.Create();
        var inside = fixture.PathTo("caves", "upper.th");

        Assert.True(WorkspacePaths.TryResolve(fixture.Root, inside, out _, out _));
    }

    [Theory]
    [InlineData("../outside.th")]
    [InlineData("caves/../../outside.th")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_escapes_and_empties(string path)
    {
        using var fixture = FixtureWorkspace.Create();

        Assert.False(WorkspacePaths.TryResolve(fixture.Root, path, out var full, out var error));
        Assert.Null(full);
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_an_absolute_path_outside_the_root()
    {
        using var fixture = FixtureWorkspace.Create();
        var outside = Path.Combine(Path.GetTempPath(), "thmcp_elsewhere.th");

        Assert.False(WorkspacePaths.TryResolve(fixture.Root, outside, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_a_sibling_root_sharing_a_name_prefix()
    {
        using var fixture = FixtureWorkspace.Create();
        var sibling = fixture.Root + "-evil";

        Assert.False(WorkspacePaths.TryResolve(fixture.Root, Path.Combine(sibling, "x.th"), out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejects_a_symlinked_file_pointing_outside()
    {
        using var fixture = FixtureWorkspace.Create();
        var secret = Path.Combine(Path.GetTempPath(), "thmcp_secret_" + Guid.NewGuid().ToString("N") + ".th");
        File.WriteAllText(secret, "survey secret\nendsurvey\n");

        var link = fixture.PathTo("leak.th");
        if (!TryCreateSymlink(link, secret, directory: false)) return; // unprivileged Windows: nothing to prove

        try
        {
            Assert.False(WorkspacePaths.TryResolve(fixture.Root, "leak.th", out _, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            File.Delete(secret);
        }
    }

    /// <summary>The escape a leaf-only symlink check misses: the link is a directory mid-path.</summary>
    [Fact]
    public void Rejects_a_file_reached_through_a_symlinked_directory()
    {
        using var fixture = FixtureWorkspace.Create();
        var secretDir = Path.Combine(Path.GetTempPath(), "thmcp_secretdir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secretDir);
        File.WriteAllText(Path.Combine(secretDir, "secret.th"), "survey secret\nendsurvey\n");

        var link = fixture.PathTo("escape");
        if (!TryCreateSymlink(link, secretDir, directory: true)) return;

        try
        {
            Assert.False(WorkspacePaths.TryResolve(fixture.Root, "escape/secret.th", out _, out var error));
            Assert.NotNull(error);
        }
        finally
        {
            Directory.Delete(secretDir, recursive: true);
        }
    }

    [Fact]
    public void Resolves_a_path_whose_file_does_not_exist_yet()
    {
        using var fixture = FixtureWorkspace.Create();

        Assert.True(WorkspacePaths.TryResolve(fixture.Root, "caves/new-survey.th", out var full, out _));
        Assert.False(File.Exists(full));
    }

    [Fact]
    public void ToRelative_uses_forward_slashes()
    {
        using var fixture = FixtureWorkspace.Create();

        Assert.Equal("caves/upper.th", WorkspacePaths.ToRelative(fixture.Root, fixture.PathTo("caves", "upper.th")));
    }

    /// <summary>Creating a symlink needs Developer Mode or admin on Windows; skip rather than fail there.</summary>
    private static bool TryCreateSymlink(string link, string target, bool directory)
    {
        try
        {
            if (directory) Directory.CreateSymbolicLink(link, target);
            else File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
