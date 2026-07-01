using System.IO;
using System.Text;
using TherionProc.Services;

namespace TherionProc.Tests;

// binary/huge-file open guard.
public class FileGuardTests
{
    [Fact]
    public void Plain_text_is_allowed()
    {
        using var dir = new TempDir();
        var p = dir.Write("a.th", "survey x\n  centreline\n  endcentreline\nendsurvey\n");
        Assert.Null(FileGuard.ShouldBlockTextOpen(p));
    }

    [Fact]
    public void Nul_bytes_flag_a_binary_file()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "blob.bin");
        File.WriteAllBytes(p, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x61, 0x62 });
        Assert.NotNull(FileGuard.ShouldBlockTextOpen(p));
        Assert.Contains("binary", FileGuard.ShouldBlockTextOpen(p)!);
    }

    [Fact]
    public void Utf16_bom_text_is_allowed_despite_nul_bytes()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "u16.th");
        File.WriteAllText(p, "survey x\nendsurvey\n", new UnicodeEncoding(false, true));   // UTF-16 LE + BOM
        Assert.Null(FileGuard.ShouldBlockTextOpen(p));
    }
}
