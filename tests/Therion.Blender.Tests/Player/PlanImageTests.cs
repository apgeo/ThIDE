// The check an image passes before it travels with a plan as a photo: a PNG or a JPEG by what it
// holds, of a size that can be read and that is not too large. Only invented images.

using System.Buffers.Binary;
using Therion.Blender.Plan;

namespace Therion.Blender.Tests.Player;

public class PlanImageTests
{
    private static PlanImageInfo Check(byte[] bytes) => PlanImage.Check(new MemoryStream(bytes));

    private static string Refused(byte[] bytes) => Assert.Throws<PlanImageException>(() => Check(bytes)).Message;

    /// <summary>The head of a PNG that says it is this large; nothing follows it.</summary>
    private static byte[] PngHead(uint width, uint height)
    {
        var png = new byte[24];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        return png;
    }

    /// <summary>A JPEG's head with a segment of <paramref name="padding"/> bytes (as a camera's
    /// notes are) before the one that has the size.</summary>
    private static byte[] JpegHead(int width, int height, int padding = 0, byte frame = 0xC0)
    {
        var jpeg = new List<byte> { 0xFF, 0xD8 };
        if (padding > 0)
        {
            jpeg.AddRange([0xFF, 0xE1, (byte)((padding + 2) >> 8), (byte)(padding + 2)]);
            jpeg.AddRange(new byte[padding]);
        }
        jpeg.AddRange([0xFF, 0xFF, frame, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0]);
        return [.. jpeg];
    }

    [Fact]
    public void APng_IsKnownByItsHead_WithItsSize()
    {
        byte[] png = TestImages.FlagPng(64, 32);

        var info = Check(png);

        Assert.Equal((PlanImageFormat.Png, 64, 32, (long)png.Length), (info.Format, info.Width, info.Height, info.Bytes));
    }

    [Fact]
    public void AJpeg_IsKnownByItsHead_WithItsSize_WhereverTheSizeStands()
    {
        byte[] jpeg = TestImages.FlagJpeg();
        var info = Check(jpeg);
        Assert.Equal((PlanImageFormat.Jpeg, 32, 16, (long)jpeg.Length), (info.Format, info.Width, info.Height, info.Bytes));

        // After sixty thousand bytes of notes, and in a progressive picture.
        Assert.Equal((4000, 3000), (Check(JpegHead(4000, 3000, padding: 60_000)).Width, Check(JpegHead(4000, 3000, padding: 60_000)).Height));
        Assert.Equal(PlanImageFormat.Jpeg, Check(JpegHead(640, 480, frame: 0xC2)).Format);
    }

    [Fact]
    public void AFileFromDisk_IsCheckedTheSameWay()
    {
        string path = Path.Combine(Path.GetTempPath(), $"thide-plan-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, TestImages.FlagPng(8, 4));
        try
        {
            Assert.Equal((8, 4), (PlanImage.Check(path).Width, PlanImage.Check(path).Height));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WhatIsNoPngAndNoJpeg_IsRefused_WhateverItIsCalled()
    {
        Assert.Equal("the file is neither a PNG nor a JPEG image", Refused("GIF89a\u0001\u0000\u0001\u0000"u8.ToArray()));
        Assert.Equal("the file is neither a PNG nor a JPEG image", Refused([]));
        Assert.Equal("the file is neither a PNG nor a JPEG image", Refused("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray()));
        Assert.Equal("the file is neither a PNG nor a JPEG image", Refused([0x89, (byte)'P', (byte)'N', (byte)'G']));
    }

    [Fact]
    public void AnImageThatIsCutShort_OrDamaged_IsRefused()
    {
        Assert.Equal("the PNG file has no header", Refused(PngHead(8, 8)[..20]));
        byte[] wrongChunk = PngHead(8, 8);
        wrongChunk[12] = (byte)'t';
        Assert.Equal("the PNG file has no header", Refused(wrongChunk));
        Assert.Equal("the image has no size", Refused(PngHead(0, 8)));

        Assert.Equal("the JPEG file has no picture size", Refused([0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3]));
        Assert.Equal("the JPEG file has no picture size", Refused([0xFF, 0xD8, 0xFF, 0xD9]));
        Assert.Equal("the JPEG file has no picture size", Refused(JpegHead(640, 480)[..8]));
        Assert.Equal("the JPEG file is damaged before its picture", Refused([0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 1, 2, 7, 7, 7, 7]));
        Assert.Equal("the image has no size", Refused(JpegHead(0, 480)));
    }

    [Fact]
    public void AnImageThatIsTooLarge_IsRefused_ByItsSidesItsPixelsAndItsBytes()
    {
        Assert.Equal(
            $"the image is {PlanImage.MaxSide + 1} x 10 pixels; a side is at most {PlanImage.MaxSide}",
            Refused(PngHead(PlanImage.MaxSide + 1, 10)));
        Assert.Equal("the PNG file's size cannot be read", Refused(PngHead(uint.MaxValue, 10)));
        Assert.Equal("the image is 9000 x 9000 pixels; an image has at most 64 million", Refused(PngHead(9000, 9000)));
        Assert.Equal((8000, 8000), (Check(PngHead(8000, 8000)).Width, Check(PngHead(8000, 8000)).Height));

        // A stream that says how long it is, is not read to its end to be found too long.
        var huge = new SparseStream(PngHead(100, 100), PlanImage.MaxBytes + 1);
        Assert.Equal("the image file is larger than 32 MiB", Assert.Throws<PlanImageException>(() => PlanImage.Check(huge)).Message);
        Assert.True(huge.BytesRead < 1000, $"{huge.BytesRead} bytes were read");
        // One that cannot say is read no further than the largest image may be.
        var endless = new SparseStream(PngHead(100, 100), long.MaxValue, seekable: false);
        Assert.Equal("the image file is larger than 32 MiB", Assert.Throws<PlanImageException>(() => PlanImage.Check(endless)).Message);
        Assert.InRange(endless.BytesRead, PlanImage.MaxBytes, PlanImage.MaxBytes + 200_000);
    }

    /// <summary>A stream of a given length that holds <paramref name="head"/> and then zeros.</summary>
    private sealed class SparseStream(byte[] head, long length, bool seekable = true) : Stream
    {
        private long _position;

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = (int)Math.Min(count, length - _position);
            if (read <= 0) return 0;
            Array.Clear(buffer, offset, read);
            for (int i = 0; i < read && _position + i < head.Length; i++) buffer[offset + i] = head[_position + i];
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
