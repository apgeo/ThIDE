using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class NewProjectViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-newproj-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private NewProjectViewModel Make() =>
        new(Path.Combine(_root, "library"), Path.Combine(_root, "cave"), () => new DateTime(2026, 10, 28));

    [Fact]
    public void Offers_the_built_ins_with_their_fields()
    {
        var vm = Make();

        Assert.Equal(["single-cave", "karst-area", "expedition"], vm.Templates.Select(t => t.Template.Id));
        Assert.Equal(["project", "title", "cs"], vm.Fields.Select(f => f.Variable.Id));
        Assert.True(vm.CanCreate);
    }

    [Fact]
    public void A_bad_value_blocks_create_with_a_message()
    {
        var vm = Make();

        vm.Fields.Single(f => f.Variable.Id == "project").Value = "two words";

        Assert.False(vm.CanCreate);
        Assert.NotEmpty(vm.Message);
    }

    [Fact]
    public void Create_writes_the_project_and_reports_its_thconfig()
    {
        var vm = Make();
        vm.Fields.Single(f => f.Variable.Id == "project").Value = "pit";

        Assert.Null(vm.Create());

        Assert.Equal(Path.Combine(_root, "cave", "pit.thconfig"), vm.CreatedThconfig);
        Assert.True(File.Exists(Path.Combine(_root, "cave", "pit.th")));
        Assert.True(Directory.Exists(Path.Combine(_root, "cave", "sketches")));
    }

    [Fact]
    public void Imported_template_appears_and_can_be_removed()
    {
        var src = Path.Combine(_root, "club");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "template.json"), """{ "id": "club", "title": "Club layout" }""");
        File.WriteAllText(Path.Combine(src, "main.th"), "survey main\nendsurvey\n");
        var vm = Make();

        Assert.Null(vm.Import(src));
        Assert.Equal("club", vm.SelectedTemplate!.Template.Id);

        vm.RemoveSelected();
        Assert.DoesNotContain(vm.Templates, t => t.Template.Id == "club");
    }
}
