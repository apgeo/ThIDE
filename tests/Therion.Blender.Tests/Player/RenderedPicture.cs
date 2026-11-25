// A rendered frame, read back for its pixel statistics: how many pixels are mostly of a colour,
// where they lie, and how much of a colour there is. For tests that render invented scenes in
// flat, strong colours and ask where and when each of them shows.

using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

internal sealed record RenderedPicture(int Width, int Height, byte[] Rgba)
{
    public static RenderedPicture Read(string path)
    {
        var (width, height, rgba) = Png.Decode(File.ReadAllBytes(path));
        return new RenderedPicture(width, height, rgba);
    }

    /// <summary>A part of the picture, by shares of its width from the left and of its
    /// height from the top.</summary>
    public RenderedPicture Part(double left, double right, double top, double bottom)
    {
        int x0 = (int)(left * Width), x1 = (int)(right * Width), y0 = (int)(top * Height), y1 = (int)(bottom * Height);
        var part = new byte[(x1 - x0) * (y1 - y0) * 4];
        for (int y = y0; y < y1; y++)
            Array.Copy(Rgba, (y * Width + x0) * 4, part, (y - y0) * (x1 - x0) * 4, (x1 - x0) * 4);
        return new RenderedPicture(x1 - x0, y1 - y0, part);
    }

    /// <summary>The pixels that are mostly of this colour: every channel the colour has is
    /// strong, and stronger by far than every channel it has not. With where their middle
    /// lies, as shares of the width and the height.</summary>
    public (int Count, double X, double Y) Of(ColorRgb colour)
    {
        int count = 0;
        double sumX = 0, sumY = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (!Is(x, y, colour)) continue;
                count++;
                sumX += x;
                sumY += y;
            }
        return count == 0 ? (0, 0, 0) : (count, sumX / count / Width, sumY / count / Height);
    }

    private bool Is(int x, int y, ColorRgb colour)
    {
        int at = (y * Width + x) * 4;
        if (Rgba[at + 3] < 128) return false;
        int weakest = 255, strongestOther = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            bool has = (channel == 0 ? colour.R : channel == 1 ? colour.G : colour.B) > 0.5;
            if (has) weakest = Math.Min(weakest, Rgba[at + channel]);
            else strongestOther = Math.Max(strongestOther, Rgba[at + channel]);
        }
        return weakest > 70 && weakest > 1.7 * strongestOther;
    }

    /// <summary>The columns the pixels of this colour lie between.</summary>
    public (int Left, int Right) BoxOf(ColorRgb colour)
    {
        var columns = Enumerable.Range(0, Width).Where(x => Enumerable.Range(0, Height).Any(y => Is(x, y, colour))).ToList();
        return columns.Count == 0 ? (0, 0) : (columns[0], columns[^1]);
    }

    /// <summary>The highest row that has a pixel of this colour between two columns.</summary>
    public int TopOf(ColorRgb colour, int fromX, int toX)
        => Enumerable.Range(0, Height).FirstOrDefault(y => Enumerable.Range(fromX, Math.Max(1, toX - fromX)).Any(x => Is(x, y, colour)), -1);

    /// <summary>How much of this colour there is: over every pixel, how far its weakest
    /// channel of the colour stands above its strongest other one, by how solid the pixel is.</summary>
    public double AmountOf(ColorRgb colour)
    {
        double sum = 0;
        for (int at = 0; at < Rgba.Length; at += 4)
        {
            int weakest = 255, strongestOther = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                bool has = (channel == 0 ? colour.R : channel == 1 ? colour.G : colour.B) > 0.5;
                if (has) weakest = Math.Min(weakest, Rgba[at + channel]);
                else strongestOther = Math.Max(strongestOther, Rgba[at + channel]);
            }
            sum += Math.Max(0, weakest - strongestOther) * (Rgba[at + 3] / 255.0);
        }
        return sum;
    }
}
