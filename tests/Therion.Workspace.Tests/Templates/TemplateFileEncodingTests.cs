using System.Text;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Templates;

/// <summary>
/// A template file declares its own encoding, like any Therion source file. A project created from
/// it must get the same bytes, or the generated file's <c>encoding</c> line lies about its contents.
/// </summary>
public class TemplateFileEncodingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-tplenc-").FullName;
    private static readonly DateTime Now = new(2026, 10, 28);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Latin2_template_file_is_written_byte_for_byte()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var folder = Path.Combine(_root, "club");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "template.json"), """{ "id": "club", "openAfterCreate": "cave.thconfig" }""");
        File.WriteAllText(Path.Combine(folder, "cave.thconfig"), "source cave.th\n");
        var before = Encoding.GetEncoding("iso-8859-2").GetBytes("encoding iso8859-2\nsurvey s -title \"Občina\"\nendsurvey\n");
        File.WriteAllBytes(Path.Combine(folder, "cave.th"), before);

        var (template, error) = ProjectTemplateEngine.LoadFolder(folder);
        Assert.Null(error);
        var (plan, errors) = ProjectTemplateEngine.Plan(template!, new Dictionary<string, string>(), Now);
        Assert.Empty(errors);
        var target = Path.Combine(_root, "out");
        Assert.Null(ProjectTemplateEngine.Write(plan!, folder, target));

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(target, "cave.th")));
    }
}
