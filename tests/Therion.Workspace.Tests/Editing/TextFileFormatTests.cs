using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public sealed class TextFileFormatTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-format-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ALatin1File_ReadsAndSavesByteForByte()
    {
        var bytes = Encoding.Latin1.GetBytes("encoding iso-8859-1\nsurvey Bédeilhac\nendsurvey\n");
        var file = SourceFileIo.Read(Write("a.th", bytes));

        Assert.Contains("Bédeilhac", file.Text);
        var (saved, _) = SourceFileIo.EncodeForSave(file.Path, file.Text, SourceFileIo.FormatOf(file));
        Assert.Equal(bytes, saved);
    }

    [Fact]
    public void TheTherionSpellingOfTheEncodingName_IsUnderstood()
    {
        var bytes = Encoding.Latin1.GetBytes("encoding iso8859-1\nsurvey Bédeilhac\nendsurvey\n");
        Assert.Contains("Bédeilhac", SourceFileIo.Read(Write("b.th", bytes)).Text);
    }

    [Fact]
    public void AUtf8BomIsKept()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("survey ș\nendsurvey\n")).ToArray();
        var file = SourceFileIo.Read(Write("c.th", bytes));
        var (saved, _) = SourceFileIo.EncodeForSave(file.Path, file.Text, SourceFileIo.FormatOf(file));
        Assert.Equal(bytes, saved);
    }

    [Fact]
    public void ChangingTheEncodingLine_ChangesHowTheFileIsSaved()
    {
        var (saved, format) = SourceFileIo.EncodeForSave("d.th", "encoding iso-8859-1\nsurvey é\n", TextFileFormat.Default);
        Assert.Equal(Encoding.Latin1.CodePage, format.Encoding.CodePage);
        Assert.Equal(Encoding.Latin1.GetBytes("encoding iso-8859-1\nsurvey é\n"), saved);
    }

    [Fact]
    public void ACharacterTheEncodingCannotHold_StopsTheSave()
    {
        var latin1 = new TextFileFormat(Encoding.Latin1, []);
        Assert.Throws<UnrepresentableCharacterException>(() =>
            SourceFileIo.EncodeForSave("e.th", "encoding iso-8859-1\nsurvey Peștera\n", latin1));
    }

    [Fact]
    public void DeletingTheEncodingLine_SavesAsUtf8_AsItWillBeReadBack()
    {
        var path = Write("f.th", Encoding.Latin1.GetBytes("encoding iso-8859-1\nsurvey Bédeilhac\n"));
        var latin1 = SourceFileIo.FormatOf(SourceFileIo.Read(path));

        var format = SourceFileIo.SaveText(path, "survey Bédeilhac\n", latin1);

        Assert.Equal(Encoding.UTF8.CodePage, format.Encoding.CodePage);
        Assert.Equal("survey Bédeilhac\n", SourceFileIo.Read(path).Text);
    }

    [Fact]
    public void AUtf16File_KeepsItsFormat_EvenWithAnEncodingLine()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("encoding iso-8859-1\nsurvey ș\n")).ToArray();
        var file = SourceFileIo.Read(Write("g.th", bytes));

        var (saved, format) = SourceFileIo.EncodeForSave(file.Path, file.Text, SourceFileIo.FormatOf(file));

        Assert.Equal(Encoding.Unicode.CodePage, format.Encoding.CodePage);
        Assert.Equal(bytes, saved);
    }

    [Fact]
    public void AnEncodingLineTheScanCouldNotFindAgain_IsIgnored()
    {
        var (_, format) = SourceFileIo.EncodeForSave("h.th", "encoding utf-16\nsurvey a\n", TextFileFormat.Default);
        Assert.Equal(Encoding.UTF8.CodePage, format.Encoding.CodePage);
    }

    [Fact]
    public void WithoutAReadFormat_TheFormatOnDiskIsKept()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("survey ș\n")).ToArray();
        var path = Write("i.th", bytes);

        var (saved, _) = SourceFileIo.EncodeForSave(path, "survey ș\n", readAs: null);

        Assert.Equal(bytes, saved);
        Assert.Equal(TextFileFormat.Default, SourceFileIo.FormatOnDisk(Path.Combine(_dir, "missing.th")));
    }
}
