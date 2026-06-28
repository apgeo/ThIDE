using System.IO;
using System.Linq;
using TherionProc.Services;

namespace TherionProc.Tests;

// MEDIA-03 — orphan (present-but-unreferenced) media detection + image-header dimensions.
public class MediaHealthTests
{
    [Fact]
    public void Unreferenced_media_on_disk_is_reported_as_orphan()
    {
        using var dir = new TempDir();
        dir.Write("scan1.xvi", "XVI 1\n");
        dir.Write("notes.txt", "not media\n");
        File.WriteAllBytes(Path.Combine(dir.Path, "photo.png"), MinimalPng(64, 32));

        var orphans = MediaScanner.ScanOrphans(workspace: null, dir.Path);

        Assert.Contains(orphans, m => m.FileName == "scan1.xvi" && m.Status == MediaStatus.Orphan);
        Assert.Contains(orphans, m => m.FileName == "photo.png" && m.Status == MediaStatus.Orphan);
        Assert.DoesNotContain(orphans, m => m.FileName == "notes.txt");   // not a media type
    }

    [Fact]
    public void Png_dimensions_are_read_from_the_header()
    {
        using var dir = new TempDir();
        var p = Path.Combine(dir.Path, "img.png");
        File.WriteAllBytes(p, MinimalPng(123, 45));
        var dim = ImageDimensions.TryGet(p);
        Assert.NotNull(dim);
        Assert.Equal((123, 45), dim!.Value);
    }

    [Fact]
    public void Null_root_yields_no_orphans()
        => Assert.Empty(MediaScanner.ScanOrphans(null, null));

    // A minimal valid-enough PNG header (signature + IHDR with width/height) for dimension reading.
    private static byte[] MinimalPng(int w, int h)
    {
        var b = new byte[33];
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        sig.CopyTo(b, 0);
        // IHDR chunk length (13) + "IHDR" at offset 8..15, width@16, height@20 (big-endian).
        b[8] = 0; b[9] = 0; b[10] = 0; b[11] = 13;
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }
}
