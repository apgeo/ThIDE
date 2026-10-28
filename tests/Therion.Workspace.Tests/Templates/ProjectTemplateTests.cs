using System.IO.Compression;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

public class ProjectTemplateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-projtpl-").FullName;
    private static readonly DateTime Now = new(2026, 10, 28);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ProjectTemplate B(string id) => ProjectTemplateEngine.BuiltIns.Single(t => t.Id == id);

    [Fact]
    public void Single_cave_plans_files_folders_and_the_thconfig_to_open()
    {
        var values = ProjectTemplateEngine.Defaults(B("single-cave"), Now);
        values["project"] = "huda";
        values["title"] = "Huda lui Papară";

        var (plan, errors) = ProjectTemplateEngine.Plan(B("single-cave"), values, Now);

        Assert.Empty(errors);
        Assert.Equal("huda.thconfig", plan!.ThconfigRelativePath);
        Assert.Equal(["data", "output", "sketches"], plan.Directories);
        Assert.Contains("survey huda -title \"Huda lui Papară\"", plan.Files.Single(f => f.RelativePath == "huda.th").Content);
        Assert.Contains("Created 2026.10.28", plan.Files.Single(f => f.RelativePath == "README.md").Content);
    }

    [Fact]
    public void Expedition_defaults_to_this_year_and_creates_the_year_folders()
    {
        var (plan, errors) = ProjectTemplateEngine.Plan(B("expedition"), ProjectTemplateEngine.Defaults(B("expedition"), Now), Now);

        Assert.Empty(errors);
        Assert.Equal("expedition-2026.thconfig", plan!.ThconfigRelativePath);
        Assert.Contains("2026/sketches", plan.Directories);
        Assert.Contains("2026", plan.Directories);
        Assert.Contains(plan.Files, f => f.RelativePath == "2026/trips.th");
    }

    [Theory]
    [InlineData("project", "bad name")]
    [InlineData("project", "../escape")]
    [InlineData("title", "line\nbreak")]
    [InlineData("title", "a \"quote\"")]
    [InlineData("cs", "UTM 34")]
    [InlineData("project", "")]
    public void Values_that_would_break_a_file_or_leave_the_project_are_refused(string id, string value)
    {
        var values = ProjectTemplateEngine.Defaults(B("single-cave"), Now);
        values[id] = value;

        var (plan, errors) = ProjectTemplateEngine.Plan(B("single-cave"), values, Now);

        Assert.Null(plan);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_text_value_used_in_a_path_cannot_climb_out()
    {
        var template = new ProjectTemplate("t", "t", "", [new("dir", "dir", "x")], [new("${dir}/a.th", "x")], [], "");

        var (plan, errors) = ProjectTemplateEngine.Plan(template, new Dictionary<string, string> { ["dir"] = ".." }, Now);

        Assert.Null(plan);
        Assert.Contains(errors, e => e.Contains("not a path inside the project"));
    }

    [Fact]
    public void Folder_template_with_a_binary_is_imported_from_a_zip_planned_and_written()
    {
        var src = Path.Combine(_root, "src", "club");
        Directory.CreateDirectory(Path.Combine(src, "img"));
        File.WriteAllText(Path.Combine(src, "template.json"),
            """{ "id": "club", "title": "Club", "variables": [ { "id": "name", "prompt": "Name", "default": "c", "type": "name" } ], "openAfterCreate": "${name}.thconfig" }""");
        File.WriteAllText(Path.Combine(src, "${name}.thconfig"), "source ${name}.th\n");
        File.WriteAllBytes(Path.Combine(src, "img", "logo.png"), [0x89, 0x50, 0x4E, 0x47]);
        var zip = Path.Combine(_root, "club.zip");
        ZipFile.CreateFromDirectory(Path.Combine(_root, "src"), zip);

        var (template, error) = ProjectTemplateEngine.Import(zip, Path.Combine(_root, "library"));
        Assert.Null(error);

        var (plan, errors) = ProjectTemplateEngine.Plan(template!, new Dictionary<string, string> { ["name"] = "pit" }, Now);
        Assert.Empty(errors);
        var target = Path.Combine(_root, "out");
        Assert.Null(ProjectTemplateEngine.Write(plan!, template!.Folder, target));

        Assert.Equal("source pit.th\n", File.ReadAllText(Path.Combine(target, "pit.thconfig")));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(Path.Combine(target, "img", "logo.png")));
        Assert.Equal("pit.thconfig", plan!.ThconfigRelativePath);
    }

    [Fact]
    public void Write_refuses_to_overwrite_and_writes_nothing()
    {
        var (plan, _) = ProjectTemplateEngine.Plan(B("single-cave"), ProjectTemplateEngine.Defaults(B("single-cave"), Now), Now);
        File.WriteAllText(Path.Combine(_root, "cave.th"), "mine");

        Assert.NotNull(ProjectTemplateEngine.Write(plan!, null, _root));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_root, "cave.th")));
        Assert.False(File.Exists(Path.Combine(_root, "cave.thconfig")));
    }
}
