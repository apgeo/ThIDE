using System.Text;
using Therion.Workspace.Editing;
using Therion.Workspace.Templates;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// A file read with bytes its encoding cannot decode is flagged, and a write that would put U+FFFD
/// over those bytes is refused; once the replaced characters are gone, it saves as usual.
/// </summary>
public class LossyReadGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-lossy-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Latin1Undeclared()
    {
        var path = Path.Combine(_root, "notes.th");
        File.WriteAllBytes(path, [0x23, 0x20, 0x76, 0xE9, 0x72, 0x0A]); // "# vér\n" as Latin-1, no encoding line
        return path;
    }

    [Fact]
    public void Invalid_utf8_is_read_as_lossy_and_valid_utf8_is_not()
    {
        var clean = Path.Combine(_root, "clean.th");
        File.WriteAllText(clean, "# vér\n", new UTF8Encoding(false));

        Assert.True(SourceFileIo.Read(Latin1Undeclared()).Lossy);
        Assert.True(SourceFileIo.FormatOnDisk(Latin1Undeclared()).Lossy);
        Assert.False(SourceFileIo.Read(clean).Lossy);
    }

    [Fact]
    public void Write_and_CanEncode_refuse_text_that_still_holds_the_replacement()
    {
        var path = Latin1Undeclared();
        var before = File.ReadAllBytes(path);
        var file = SourceFileIo.Read(path);

        Assert.False(SourceFileIo.CanEncode(file, file.Text + "x\n"));
        Assert.Throws<UndecodableBytesException>(() => SourceFileIo.Write(file, file.Text + "x\n"));
        Assert.Throws<UndecodableBytesException>(() => SourceFileIo.SaveText(path, file.Text, readAs: null));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Adding_an_encoding_line_does_not_bypass_the_guard()
    {
        var path = Latin1Undeclared();
        var file = SourceFileIo.Read(path);

        Assert.Throws<UndecodableBytesException>(() =>
            SourceFileIo.EncodeForSave(path, "encoding iso8859-1\n" + file.Text, SourceFileIo.FormatOf(file)));
    }

    [Fact]
    public void Text_with_the_replacement_retyped_saves_and_is_no_longer_lossy()
    {
        var path = Latin1Undeclared();
        var file = SourceFileIo.Read(path);

        var format = SourceFileIo.SaveText(path, file.Text.Replace('�', 'é'), SourceFileIo.FormatOf(file));

        Assert.False(format.Lossy);
        Assert.Equal("# vér\n", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void Template_value_the_declared_encoding_cannot_hold_is_refused_and_nothing_is_written()
    {
        var template = new ProjectTemplate("latin", "Latin", "",
            [new TemplateVariable("title", "Title", "")],
            [new TemplateFileEntry("cave.th", "encoding iso8859-1\nsurvey s -title \"${title}\"\nendsurvey\n")],
            [], "");
        var (plan, errors) = ProjectTemplateEngine.Plan(template, new Dictionary<string, string> { ["title"] = "Peștera" }, new DateTime(2026, 11, 3));
        Assert.Empty(errors);
        var target = Path.Combine(_root, "out");

        Assert.NotNull(ProjectTemplateEngine.Write(plan!, null, target));
        Assert.False(File.Exists(Path.Combine(target, "cave.th")));
    }
}
