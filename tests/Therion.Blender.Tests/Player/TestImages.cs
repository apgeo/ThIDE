// Invented images for the tests of photos: a flag whose parts tell left from right and top from
// bottom, so a picture that is mirrored or upside down is seen at once.

using Therion.Blender.Tests.SceneProbe;

namespace Therion.Blender.Tests.Player;

internal static class TestImages
{
    /// <summary>A PNG of the flag: green across its top quarter, and below that red on the left
    /// and blue on the right.</summary>
    public static byte[] FlagPng(int width = 64, int height = 32)
    {
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int channel = y < height / 4 ? 1 : x < width / 2 ? 0 : 2;
                rgb[(y * width + x) * 3 + channel] = 255;
            }
        return Png.EncodeRgb(width, height, rgb);
    }

    /// <summary>A PNG of one colour.</summary>
    public static byte[] PlainPng(int width, int height, byte r, byte g, byte b)
    {
        var rgb = new byte[width * height * 3];
        for (int p = 0; p < width * height; p++)
        {
            rgb[p * 3] = r;
            rgb[p * 3 + 1] = g;
            rgb[p * 3 + 2] = b;
        }
        return Png.EncodeRgb(width, height, rgb);
    }

    /// <summary>The same flag as a JPEG, 32 by 16 pixels (made once with an image library and
    /// kept here as its bytes: .NET has no JPEG writer of its own).</summary>
    public static byte[] FlagJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/" +
        "2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAQACADAREAAhEBAxEB/8QA" +
        "FgABAQEAAAAAAAAAAAAAAAAAAAYH/8QAGhAAAgIDAAAAAAAAAAAAAAAAABhl42akpf/EABcBAQEBAQAAAAAAAAAAAAAAAAAICQX/xAAhEQABAQcFAAAAAAAA" +
        "AAAAAAAAGBYXY2RlpORmoqPh4v/aAAwDAQACEQMRAD8Al2GgN2slZmo23s7aK9Q2uQGGgN2sM1G29hFeobXIIB28M6tJcSQK9bZBHLsZzj9h28M6tISBXrbI" +
        "DsZzj9k2Smb/AAAMCNxjHQAH/9k=");

    /// <summary>Writes an image into a folder and returns its path.</summary>
    public static string Write(string folder, string name, byte[] image)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, image);
        return path;
    }
}
