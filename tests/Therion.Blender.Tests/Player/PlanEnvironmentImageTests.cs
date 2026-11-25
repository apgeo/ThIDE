// The check of a world's image (an HDRI) before it travels with a plan: a Radiance HDR or an
// OpenEXR picture by what the file holds, of a size that can be read and is not too large. The
// images are made here, byte by byte.

using System.Text;
using Therion.Blender.Plan;

namespace Therion.Blender.Tests.Player;

public class PlanEnvironmentImageTests
{
    private static PlanEnvironmentInfo Check(byte[] bytes) => PlanEnvironmentImage.Check(new MemoryStream(bytes));

    private static string Refused(byte[] bytes) => Assert.Throws<PlanImageException>(() => Check(bytes)).Message;

    private static byte[] Radiance(string head) => Encoding.ASCII.GetBytes(head);

    /// <summary>The head of an OpenEXR file whose picture is of this size, with nothing after it.</summary>
    private static byte[] ExrHead(int xMin, int yMin, int xMax, int yMax, int version = 2, bool window = true)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        void Name(string name)
        {
            writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write((byte)0);
        }
        writer.Write([(byte)0x76, (byte)0x2F, (byte)0x31, (byte)0x01]);
        writer.Write(version);
        Name("compression");
        Name("compression");
        writer.Write(1);
        writer.Write((byte)0);
        if (window)
        {
            Name("dataWindow");
            Name("box2i");
            writer.Write(16);
            foreach (int value in new[] { xMin, yMin, xMax, yMax }) writer.Write(value);
        }
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void ARadiancePicture_IsKnownByItsHead_WithItsSize()
    {
        byte[] hdr = TestImages.RadianceHdr(64, 32, TestImages.Compass);

        var info = Check(hdr);

        Assert.Equal((PlanEnvironmentFormat.Hdr, 64, 32, (long)hdr.Length), (info.Format, info.Width, info.Height, info.Bytes));
        // The lines a head may have beside the format, and the other name a file may start with.
        Assert.Equal((2048, 1024), Size(Check(Radiance("#?RGBE\n# made by a camera\nEXPOSURE=1.5\r\nFORMAT=32-bit_rle_rgbe\n\n-Y 1024 +X 2048\n"))));
        // A picture that is stored on its side says its width second and its rows first, or the other way round.
        Assert.Equal((2048, 1024), Size(Check(Radiance("#?RADIANCE\n\n+X 2048 -Y 1024\n"))));
        Assert.Equal((8, 4), Size(Check(Radiance("#?RADIANCE\nFORMAT=32-bit_rle_xyze\n\n+Y 4 +X 8\n"))));
    }

    private static (int, int) Size(PlanEnvironmentInfo info) => (info.Width, info.Height);

    [Fact]
    public void AnOpenExrPicture_IsKnownByItsHead_WithTheSizeOfItsDataWindow()
    {
        byte[] exr = TestImages.OpenExr(64, 32, TestImages.Compass);

        var info = Check(exr);

        Assert.Equal((PlanEnvironmentFormat.Exr, 64, 32, (long)exr.Length), (info.Format, info.Width, info.Height, info.Bytes));
        // A window need not start at zero; tiles and long names are a picture's too.
        Assert.Equal((4096, 2048), Size(Check(ExrHead(-10, 100, 4085, 2147))));
        Assert.Equal((16, 8), Size(Check(ExrHead(0, 0, 15, 7, version: 2 | 0x200 | 0x400))));
    }

    [Fact]
    public void AFileFromDisk_IsCheckedTheSameWay_AndItsNameSaysWhatItMayBe()
    {
        string path = Path.Combine(Path.GetTempPath(), $"thide-plan-sky-{Guid.NewGuid():N}.hdr");
        File.WriteAllBytes(path, TestImages.RadianceHdr(8, 4, TestImages.LitFromTheSouth));
        try
        {
            Assert.Equal((8, 4), Size(PlanEnvironmentImage.Check(path)));
        }
        finally
        {
            File.Delete(path);
        }
        Assert.True(PlanEnvironmentImage.HasExtension("hdri/sky.hdr"));
        Assert.True(PlanEnvironmentImage.HasExtension("SKY.EXR"));
        Assert.False(PlanEnvironmentImage.HasExtension("sky.png"));
        Assert.False(PlanEnvironmentImage.HasExtension("hdr"));
        Assert.False(PlanEnvironmentImage.HasExtension(null));
    }

    [Fact]
    public void WhatIsNeither_IsRefused_WhateverItIsCalled()
    {
        const string neither = "the file is neither a Radiance HDR nor an OpenEXR image";
        Assert.Equal(neither, Refused(TestImages.FlagPng()));
        Assert.Equal(neither, Refused(TestImages.FlagJpeg()));
        Assert.Equal(neither, Refused([]));
        Assert.Equal(neither, Refused("# a comment, not a picture\n"u8.ToArray()));
        Assert.Equal(neither, Refused([0x76, 0x2F, 0x31]));
    }

    [Fact]
    public void APictureThatIsCutShort_OrDamaged_IsRefused()
    {
        Assert.Equal("the Radiance file has no picture size", Refused(Radiance("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n")));
        Assert.Equal("the Radiance file has no picture size", Refused(Radiance("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n")));
        Assert.Equal("the Radiance file's picture size cannot be read", Refused(Radiance("#?RADIANCE\n\n-Y 32\n")));
        Assert.Equal("the Radiance file's picture size cannot be read", Refused(Radiance("#?RADIANCE\n\n-Y 32 +Y 64\n")));
        Assert.Equal("the Radiance file's picture size cannot be read", Refused(Radiance("#?RADIANCE\n\n-Y 32 +X -64\n")));
        Assert.Equal("the Radiance file's picture size cannot be read", Refused(Radiance("#?RADIANCE\n\n-Y 99999999999 +X 64\n")));
        Assert.Equal("the Radiance file is of a format that is not a picture's", Refused(Radiance("#?RADIANCE\nFORMAT=text\n\n-Y 32 +X 64\n")));

        Assert.Equal("the OpenEXR file has no head", Refused([0x76, 0x2F, 0x31, 0x01, 2]));
        Assert.Equal("the OpenEXR file is of a version that cannot be read", Refused(ExrHead(0, 0, 15, 7, version: 3)));
        Assert.Equal("the OpenEXR file holds deep data, not a picture", Refused(ExrHead(0, 0, 15, 7, version: 2 | 0x800)));
        Assert.Equal("the OpenEXR file has no picture size", Refused(ExrHead(0, 0, 15, 7, window: false)));
        Assert.Equal("the OpenEXR file's head is damaged", Refused(ExrHead(0, 0, 15, 7)[..30]));
        Assert.Equal("the OpenEXR file's picture size cannot be read", Refused(ExrHead(0, 0, -1, 7)));
        Assert.Equal("the Radiance file's picture size cannot be read", Refused(Radiance("#?RADIANCE\n\n-Y 0 +X 64\n")));
        Assert.Equal("the OpenEXR file's picture size cannot be read", Refused(ExrHead(int.MinValue, 0, int.MaxValue, 7)));
    }

    [Fact]
    public void APictureThatIsTooLarge_IsRefused_ByItsSidesItsPixelsAndItsBytes()
    {
        int side = PlanEnvironmentImage.MaxSide;
        Assert.Equal(
            $"the image is {side + 2} x 8 pixels; a side is at most {side}",
            Refused(Radiance($"#?RADIANCE\n\n-Y 8 +X {side + 2}\n")));
        Assert.Equal(
            $"the image is {side} x {side} pixels; a world's image has at most 134 million",
            Refused(ExrHead(0, 0, side - 1, side - 1)));
        // The largest that is taken: twice as wide as high, as such a picture is.
        Assert.Equal((side, side / 2), Size(Check(ExrHead(0, 0, side - 1, side / 2 - 1))));

        // A stream that says how long it is, is not read to its end to be found too long.
        byte[] head = Radiance("#?RADIANCE\n\n-Y 8 +X 16\n");
        var huge = new SparseStream(head, PlanEnvironmentImage.MaxBytes + 1);
        Assert.Equal("the image file is larger than 256 MiB", Assert.Throws<PlanImageException>(() => PlanEnvironmentImage.Check(huge)).Message);
        Assert.True(huge.BytesRead < 1000, $"{huge.BytesRead} bytes were read");
        // A head that never ends is given up on, not read for ever.
        var endless = new SparseStream(Radiance("#?RADIANCE\n"), long.MaxValue, seekable: false, fill: (byte)'x');
        Assert.Equal("the image file's head does not end", Assert.Throws<PlanImageException>(() => PlanEnvironmentImage.Check(endless)).Message);
        Assert.InRange(endless.BytesRead, 1, 2_000_000);
    }

    /// <summary>A stream of a given length that holds <paramref name="head"/> and then one byte
    /// over and over.</summary>
    private sealed class SparseStream(byte[] head, long length, bool seekable = true, byte fill = 0) : Stream
    {
        private long _position;

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - _position);
            for (int i = 0; i < read; i++)
                buffer[offset + i] = _position + i < head.Length ? head[_position + i] : fill;
            _position += read;
            BytesRead += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => seekable;
        public override bool CanWrite => false;
        public override long Length => seekable ? length : throw new NotSupportedException();
        public override long Position { get => _position; set => _position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
