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
}
