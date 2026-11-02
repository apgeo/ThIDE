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
}
