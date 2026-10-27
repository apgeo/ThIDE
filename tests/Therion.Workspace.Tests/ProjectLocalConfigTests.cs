namespace Therion.Workspace.Tests;

public class ProjectLocalConfigTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-plc-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed record Sample(string Name, int Count);

    [Fact]
    public void Project_folder_location_is_the_dot_thide_directory()
    {
        Assert.Equal(Path.Combine(_root, ".thide"), ProjectLocalConfig.DirectoryFor(_root, ProjectConfigLocation.ProjectFolder));
        Assert.Equal(Path.Combine(_root, ".thide", "hooks.json"),
            ProjectLocalConfig.PathFor(_root, ProjectConfigLocation.ProjectFolder, "hooks.json"));
    }

    [Fact]
    public void App_data_location_is_keyed_by_the_project_root()
    {
        var appData = Path.Combine(_root, "appdata");

        var dir = ProjectLocalConfig.DirectoryFor(_root, ProjectConfigLocation.AppData, appData);

        Assert.Equal(Path.Combine(appData, ProjectSidecar.KeyFor(_root)!, "config"), dir);
    }

    [Fact]
    public void No_root_no_directory()
    {
        Assert.Null(ProjectLocalConfig.DirectoryFor(null, ProjectConfigLocation.ProjectFolder));
    }

    [Fact]
    public void Missing_file_is_neither_value_nor_error()
    {
        var read = ProjectLocalConfig.TryReadJson<Sample>(Path.Combine(_root, "none.json"));

        Assert.Null(read.Value);
        Assert.Null(read.Error);
    }

    [Fact]
    public void Hand_edited_json_with_comments_and_trailing_commas_reads()
    {
        var path = Path.Combine(_root, "s.json");
        File.WriteAllText(path, "{\n  // a comment\n  \"name\": \"x\",\n  \"COUNT\": 2,\n}\n");

        var read = ProjectLocalConfig.TryReadJson<Sample>(path);

        Assert.Equal(new Sample("x", 2), read.Value);
    }

    [Fact]
    public void Broken_json_is_an_error_value_naming_the_file_and_line()
    {
        var path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, "{\n  \"name\": \"x\"\n  \"count\": 2\n}\n");

        var read = ProjectLocalConfig.TryReadJson<Sample>(path);

        Assert.Null(read.Value);
        Assert.StartsWith("bad.json (line 3)", read.Error);
    }

    [Fact]
    public void Empty_file_is_an_error_value()
    {
        var path = Path.Combine(_root, "empty.json");
        File.WriteAllText(path, "");

        Assert.Equal("empty.json: the file is empty.", ProjectLocalConfig.TryReadJson<Sample>(path).Error);
    }
}
