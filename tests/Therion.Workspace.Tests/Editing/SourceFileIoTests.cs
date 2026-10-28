using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public class SourceFileIoTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-sourcefile-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Latin1_file_is_written_back_in_latin1()
    {
        var path = Path.Combine(_root, "grotte.th");
        File.WriteAllText(path, "encoding iso-8859-1\nsurvey Bédeilhac\nendsurvey\n", Encoding.Latin1);

        var file = SourceFileIo.Read(path);
        SourceFileIo.Write(file, file.Text.Replace("endsurvey", "  # é\nendsurvey"));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("encoding iso-8859-1\nsurvey Bédeilhac\n  # é\nendsurvey\n", Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain((byte)0xC3, bytes); // no UTF-8 lead byte crept in
    }

    [Fact]
    public void Utf8_bom_survives_a_rewrite()
    {
        var path = Path.Combine(_root, "a.th");
        File.WriteAllText(path, "survey a\nendsurvey\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var file = SourceFileIo.Read(path);
        SourceFileIo.Write(file, "survey a\n  input b.th\nendsurvey\n");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("survey a\n  input b.th\nendsurvey\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void Character_the_encoding_cannot_hold_fails_the_write()
    {
        var path = Path.Combine(_root, "grotte.th");
        File.WriteAllText(path, "encoding iso-8859-1\nsurvey a\nendsurvey\n", Encoding.Latin1);
        var before = File.ReadAllBytes(path);

        var file = SourceFileIo.Read(path);

        Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.Write(file, file.Text + "# Peștera\n"));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_root)); // the temporary was cleaned up
    }

    [Fact]
    public void Create_refuses_an_existing_file()
    {
        var path = Path.Combine(_root, "new.th");
        SourceFileIo.Create(path, "survey new\nendsurvey\n");

        Assert.Throws<IOException>(() => SourceFileIo.Create(path, "x"));
        Assert.Equal("survey new\nendsurvey\n", File.ReadAllText(path));
    }
}
