using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// Reading a file and saving its text back unchanged must leave the bytes as they were. Where a
/// byte cannot be decoded, the save must not quietly write something else over it.
/// </summary>
public class EightBitRoundTripTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-8bit-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Declared_cp1250_file_keeps_its_bytes_through_read_and_save()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_root, "jeskyne.th");
        var before = Encoding.GetEncoding(1250).GetBytes("encoding cp1250\nsurvey s -title \"ěčý\"\nendsurvey\n");
        File.WriteAllBytes(path, before);

        var file = SourceFileIo.Read(path);
        Assert.DoesNotContain('�', file.Text);

        SourceFileIo.SaveText(path, file.Text, SourceFileIo.FormatOf(file));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Undeclared_latin1_byte_is_not_replaced_on_save()
    {
        var path = Path.Combine(_root, "notes.th");
        byte[] before = [0x23, 0x20, 0x76, 0xE9, 0x72, 0x0A]; // "# vér\n" as Latin-1, no encoding line
        File.WriteAllBytes(path, before);

        var file = SourceFileIo.Read(path);
        try
        {
            SourceFileIo.SaveText(path, file.Text, SourceFileIo.FormatOf(file));
        }
        catch (UnrepresentableCharacterException)
        {
            // Refusing the save is acceptable: the file is untouched.
        }

        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
