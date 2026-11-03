using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// The library write primitives on their own, without any caller compensating: lossy reads that
/// replace with '?', writes through links and over protected files, and new files whose content
/// declares a legacy encoding.
/// </summary>
public class WritePrimitiveGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-write-primitive-").FullName;

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Ascii_declared_file_with_an_8bit_byte_refuses_a_write_that_would_lose_it()
    {
        var path = Path.Combine(_root, "notes.th");
        byte[] original = [.. "encoding ascii\n# v"u8, 0xE9, .. "r\n"u8];
        File.WriteAllBytes(path, original);

        var file = SourceFileIo.Read(path);
        Assert.True(file.Lossy, "the strict ASCII decode should have noticed the 0xE9 byte");

        Exception? refused = null;
        try { SourceFileIo.Write(file, file.Text + "# edit\n"); }
        catch (UndecodableBytesException ex) { refused = ex; }

        var after = File.ReadAllBytes(path);
        Assert.True(refused is not null,
            $"a lossy ASCII read was written back; byte 0xE9 became 0x{after[original.Length - 3]:X2} and is gone for good");
    }

    [Fact]
    public void Write_through_a_symlink_keeps_the_link_and_updates_its_target()
    {
        if (OperatingSystem.IsWindows()) return;
        var target = Path.Combine(_root, "target.th");
        var link = Path.Combine(_root, "link.th");
        File.WriteAllText(target, "survey a\nendsurvey\n");
        File.CreateSymbolicLink(link, target);

        SourceFileIo.Write(SourceFileIo.Read(link), "survey b\nendsurvey\n");

        Assert.True(new FileInfo(link).LinkTarget is not null, "the write replaced the symbolic link with a regular file");
        Assert.Equal("survey b\nendsurvey\n", File.ReadAllText(target));
    }

    [Fact]
    public void Write_keeps_the_file_s_unix_mode()
    {
        if (OperatingSystem.IsWindows()) return;
        var path = Path.Combine(_root, "shared.th");
        File.WriteAllText(path, "survey a\nendsurvey\n");
        // 0640: not what any common umask gives a fresh file, so a replaced file shows.
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(path, mode);

        SourceFileIo.Write(SourceFileIo.Read(path), "survey b\nendsurvey\n");

        var after = File.GetUnixFileMode(path);
        Assert.True(after == mode, $"mode was {Convert.ToString((int)mode, 8)} before the write and {Convert.ToString((int)after, 8)} after it");
    }

    [Fact]
    public void Write_refuses_a_read_only_file()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = Path.Combine(_root, "locked.th");
        File.WriteAllText(path, "survey a\nendsurvey\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        Exception? refused = null;
        try { SourceFileIo.Write(SourceFileIo.Read(path), "survey b\nendsurvey\n"); }
        catch (UnauthorizedAccessException ex) { refused = ex; }

        Assert.True(refused is not null,
            $"a read-only file was replaced; it now reads '{File.ReadAllText(path).TrimEnd()}'");
    }

    [Fact]
    public void Create_writes_the_bytes_the_content_s_encoding_line_declares()
    {
        var path = Path.Combine(_root, "trip.th");
        var content = "encoding iso8859-2\nsurvey a -title \"š\"\nendsurvey\n";

        SourceFileIo.Create(path, content);

        var read = SourceFileIo.Read(path);
        Assert.True(read.Text == content,
            $"a created file declaring iso8859-2 reads back as '{read.Text.Split('\n')[1]}' instead of the title it was given");
    }

    [Fact]
    public void Create_refuses_content_its_encoding_cannot_hold_and_makes_nothing()
    {
        var path = Path.Combine(_root, "new", "trip.th");
        var content = "encoding iso8859-1\nsurvey a -title \"Peștera\"\nendsurvey\n";

        var refused = Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.Create(path, content));

        Assert.Equal('ș', refused.Character);
        Assert.False(Directory.Exists(Path.Combine(_root, "new")));
    }
}
