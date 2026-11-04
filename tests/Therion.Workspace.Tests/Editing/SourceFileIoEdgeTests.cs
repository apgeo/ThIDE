using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// The save path's edges: a folder that is not there yet, when a byte-order mark is kept, a lossy read
/// behind a BOM, the format on disk against the format an editor read, the temporary file after a
/// failed write, and what the two write refusals tell the user.
/// </summary>
public sealed class SourceFileIoEdgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-sourcefile-edge-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Create_makes_the_missing_folders()
    {
        var path = Path.Combine(_dir, "new", "deeper", "a.th");

        SourceFileIo.Create(path, "survey a\nendsurvey\n");

        Assert.True(File.Exists(path));
        Assert.Equal("survey a\nendsurvey\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_bom_is_kept_when_the_declared_encoding_matches_it()
    {
        // The editor read the file as Latin-1 (a declaration) with a UTF-8 BOM in front. Removing the
        // declaration makes the BOM's UTF-8 the encoding again: the BOM matches and stays.
        var readAs = new TextFileFormat(Encoding.Latin1, Utf8Bom);

        var format = SourceFileIo.FormatForSave("survey ș\n", readAs);

        Assert.Equal(Encoding.UTF8.CodePage, format.Encoding.CodePage);
        Assert.Equal(Utf8Bom, format.Bom);
    }

    [Fact]
    public void A_bom_is_dropped_when_the_declared_encoding_differs()
    {
        // A UTF-8 file with a BOM that now declares Latin-1: a UTF-8 BOM in front of Latin-1 bytes is wrong.
        var readAs = new TextFileFormat(Encoding.UTF8, Utf8Bom);

        var format = SourceFileIo.FormatForSave("encoding iso-8859-1\nsurvey é\n", readAs);

        Assert.Equal(Encoding.Latin1.CodePage, format.Encoding.CodePage);
        Assert.Empty(format.Bom);
    }

    [Fact]
    public void A_changed_format_keeps_the_lossy_flag_and_an_unchanged_one_is_the_same_object()
    {
        var readAs = new TextFileFormat(Encoding.UTF8, []) { Lossy = true };

        Assert.Same(readAs, SourceFileIo.FormatForSave("survey a\n", readAs));
        Assert.True(SourceFileIo.FormatForSave("encoding iso-8859-1\nsurvey a\n", readAs).Lossy);
        Assert.Equal(TextFileFormat.Default, SourceFileIo.FormatForSave("survey a\n", null));
    }

    [Fact]
    public void A_lossy_read_behind_a_bom_decodes_after_the_bom()
    {
        // UTF-8 BOM, "a", an invalid byte, "b": the BOM must not reach the text and the decode must
        // cover exactly the bytes after it.
        var path = Write("lossy.th", [.. Utf8Bom, (byte)'a', 0xFF, (byte)'b']);

        var file = SourceFileIo.Read(path);

        Assert.True(file.Lossy);
        Assert.Equal("a�b", file.Text);
        Assert.Equal(Utf8Bom, file.Bom);
        Assert.True(SourceFileIo.FormatOnDisk(path).Lossy);
    }

    [Fact]
    public void A_lossy_read_marks_the_text_and_the_saved_format_is_not_lossy()
    {
        var path = Write("lossy2.th", [(byte)'a', 0xC3, (byte)'b']);
        var file = SourceFileIo.Read(path);
        Assert.True(file.Lossy);

        var (bytes, format) = SourceFileIo.EncodeForSave(path, "ab", SourceFileIo.FormatOf(file));

        Assert.False(format.Lossy);
        Assert.Equal("ab"u8.ToArray(), bytes);
    }

    [Fact]
    public void A_read_format_wins_over_the_format_on_disk()
    {
        // On disk: UTF-8 with a BOM. The editor's format: UTF-8 without one. The editor's wins.
        var path = Write("disk.th", [.. Utf8Bom, .. "survey a\n"u8.ToArray()]);

        var (bytes, format) = SourceFileIo.EncodeForSave(path, "survey a\n", TextFileFormat.Default);

        Assert.Empty(format.Bom);
        Assert.Equal("survey a\n"u8.ToArray(), bytes);
        // Without one, the disk's format is used.
        Assert.Equal([.. Utf8Bom, .. "survey a\n"u8.ToArray()], SourceFileIo.EncodeForSave(path, "survey a\n", null).Bytes);
    }

    [Fact]
    public void A_failed_write_leaves_no_temporary_file()
    {
        // The target is a folder: the temporary file is written, the move over the folder fails.
        var target = Path.Combine(_dir, "folder.th");
        Directory.CreateDirectory(target);

        Assert.ThrowsAny<Exception>(() => SourceFileIo.WriteOver(target, "survey a\n"u8.ToArray()));

        Assert.Empty(Directory.GetFiles(_dir, "*.therion-write-*.tmp"));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void A_successful_write_leaves_only_the_file()
    {
        var path = Write("ok.th", "survey a\n"u8.ToArray());

        SourceFileIo.SaveText(path, "survey b\n");
        SourceFileIo.WriteOver(Path.Combine(_dir, "fresh.th"), "x"u8.ToArray());

        Assert.Equal(["fresh.th", "ok.th"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("survey b\n", File.ReadAllText(path));
    }

    [Fact]
    public void The_refusals_name_the_character_the_encoding_and_the_file()
    {
        var path = Write("latin.th", Encoding.Latin1.GetBytes("encoding iso-8859-1\nsurvey a\n"));
        var file = SourceFileIo.Read(path);

        var ex = Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.Write(file, file.Text + "# ș\n"));
        Assert.Equal('ș', ex.Character);
        Assert.Equal(file.Encoding.WebName, ex.EncodingName);
        Assert.Contains("'ș' (U+0219)", ex.Message);
        Assert.Contains(path, ex.Message);
        Assert.Contains(file.Encoding.WebName, ex.Message);

        var lossyPath = Write("lossy3.th", [(byte)'a', 0xFF]);
        var lossy = SourceFileIo.Read(lossyPath);
        var undecodable = Assert.Throws<UndecodableBytesException>(() => SourceFileIo.Write(lossy, lossy.Text));
        Assert.Equal('�', undecodable.Character);
        Assert.Contains(lossyPath, undecodable.Message);
        Assert.Contains("encoding", undecodable.Message);
    }
}
