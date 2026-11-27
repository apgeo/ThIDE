// Names that every file system takes the same way: a bundle made on one machine is unpacked on
// another, and a job id names a folder and files on whatever node takes the job. Windows gives some
// names to devices (CON, NUL, COM1…, whatever their extension), drops a trailing dot or space, and
// does not tell two names apart by their case; a name like that would open a device, or another
// file, where a Linux machine would open the file it names. Also: text that comes from elsewhere,
// shown on one line with its control characters made visible, so that a name cannot rewrite the
// terminal or the log it is printed to.

using System.Globalization;
using System.Text;

namespace Therion.Blender;

/// <summary>What a file name must be to mean the same file everywhere, and text made fit to print.</summary>
public static class PortableNames
{
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>The characters Windows refuses in a name, besides the separators.</summary>
    private const string Reserved = "<>:\"|?*";

    /// <summary>
    /// Whether <paramref name="name"/> (one part of a path, no separator) names the same file on
    /// Windows, macOS and Linux: not a device name of Windows with or without an extension, not
    /// ending in a dot or a space, without control characters or the characters Windows refuses.
    /// </summary>
    public static bool IsPortable(string? name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or "..") return false;
        if (name[^1] is '.' or ' ') return false;
        foreach (char c in name)
            if (char.IsControl(c) || Reserved.Contains(c) || c is '/' or '\\') return false;
        int dot = name.IndexOf('.');
        string stem = (dot < 0 ? name : name[..dot]).TrimEnd(' ');
        return !Devices.Contains(stem);
    }

    /// <summary>Whether every part of a relative path with forward slashes is
    /// <see cref="IsPortable"/>.</summary>
    public static bool IsPortablePath(string? path)
        => !string.IsNullOrEmpty(path) && path.Split('/').All(IsPortable);

    /// <summary>
    /// <paramref name="text"/> on one line and fit to print: control characters (escapes, line
    /// breaks, bells) and the marks that reorder text on screen are written as <c>\uXXXX</c>, and
    /// text longer than <paramref name="max"/> characters is cut there with an ellipsis.
    /// </summary>
    public static string Printable(string? text, int max = 500)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var shown = new StringBuilder(Math.Min(text.Length, max) + 8);
        for (int i = 0; i < text.Length; i++)
        {
            if (shown.Length >= max)
            {
                shown.Append('…');
                break;
            }
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                shown.Append(c).Append(text[++i]);
                continue;
            }
            if (char.IsControl(c) || IsReordering(c) || char.IsSurrogate(c))
                shown.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                shown.Append(c);
        }
        return shown.ToString();
    }

    /// <summary>The marks that change the order or the direction text is shown in.</summary>
    private static bool IsReordering(char c) => c is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
