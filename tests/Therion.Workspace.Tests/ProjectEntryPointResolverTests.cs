namespace Therion.Workspace.Tests;

/// <summary>
/// Folder resolution is what the CLI and the MCP server use when given a project directory. An
/// ordinary project — one thconfig beside survey files that start with `encoding utf-8`, the way
/// xtherion and TopoDroid write them — must resolve to that thconfig.
/// </summary>
public class ProjectEntryPointResolverTests
{
    [Fact]
    public async Task Folder_with_thconfig_and_encoded_survey_files_selects_the_thconfig()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "cave.thconfig");
        File.WriteAllText(cfg, "source cave.th\nexport map -o cave.pdf\n");
        File.WriteAllText(Path.Combine(dir.Path, "cave.th"),
            "encoding utf-8\nsurvey cave\n  centreline\n  endcentreline\nendsurvey\n");
        File.WriteAllText(Path.Combine(dir.Path, "cave.th2"),
            "encoding utf-8\nscrap s1\nendscrap\n");

        var resolver = new ProjectEntryPointResolver(new ThconfigSniffer());
        var resolution = await resolver.ResolveAsync(dir.Path);

        Assert.True(resolution.Selected is not null,
            $"expected the thconfig to be selected, but resolution has {resolution.Candidates.Length} candidates: "
            + string.Join(", ", resolution.Candidates.Select(Path.GetFileName)));
        Assert.Equal(Path.GetFullPath(cfg), resolution.Selected);
    }

    [Fact]
    public async Task Loader_opens_a_folder_holding_one_thconfig_and_encoded_survey_files()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "cave.thconfig");
        File.WriteAllText(cfg, "source cave.th\n");
        File.WriteAllText(Path.Combine(dir.Path, "cave.th"), "encoding utf-8\nsurvey cave\nendsurvey\n");

        var entry = await WorkspaceLoader.ResolveEntryPointAsync(dir.Path);

        Assert.Equal(Path.GetFullPath(cfg), entry);
    }

    [Fact]
    public async Task A_named_thconfig_wins_over_extensionless_files()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "cave.thc");
        File.WriteAllText(cfg, "source cave.th\n");
        File.WriteAllText(Path.Combine(dir.Path, "README"), "source cave.th\nexport map\n");
        File.WriteAllText(Path.Combine(dir.Path, "LICENSE"), "Permission is granted.\n");

        var resolution = await new ProjectEntryPointResolver(new ThconfigSniffer()).ResolveAsync(dir.Path);

        Assert.Equal(Path.GetFullPath(cfg), resolution.Selected);
    }

    [Fact]
    public async Task Without_a_named_thconfig_an_extensionless_config_is_found_by_its_content()
    {
        using var dir = new TempDir();
        var cfg = Path.Combine(dir.Path, "build");
        File.WriteAllText(cfg, "source cave.th\nexport map -o cave.pdf\n");
        File.WriteAllText(Path.Combine(dir.Path, "README"), "Import the source files.\n");
        File.WriteAllText(Path.Combine(dir.Path, "cave.th"), "encoding utf-8\ninput a.th\nsurvey cave\nendsurvey\n");

        var resolution = await new ProjectEntryPointResolver(new ThconfigSniffer()).ResolveAsync(dir.Path);

        Assert.Equal(Path.GetFullPath(cfg), resolution.Selected);
    }
}
