// Small rules that the checks of specs, plans, movies and storyboards share, and that the bundle
// writer names its folder by: a colour channel, and a file name that every file system takes.

namespace Therion.Blender;

internal static class PlanRules
{
    /// <summary>The names Windows keeps for its devices, with or without an extension (COM0 and
    /// LPT0, and the superscript digits, as Windows 11 keeps them too).</summary>
    private static readonly HashSet<string> ReservedOnWindows = new(
        ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
         .. "0123456789¹²³".SelectMany(d => new[] { $"COM{d}", $"LPT{d}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>A character that some file system refuses in a name: a control character, or one
    /// of those Windows refuses, which include every one Linux and macOS refuse. The same set on
    /// every platform, so that what is checked on one is good on the others.</summary>
    public static bool IsRefusedInAName(char c) => c < ' ' || "<>:\"/\\|?*".Contains(c);

    /// <summary>Whether Windows keeps <paramref name="name"/> for a device: <c>CON</c>,
    /// <c>nul.mp4</c>, <c>COM1 .txt</c>.</summary>
    public static bool IsReservedOnWindows(string name) => ReservedOnWindows.Contains(name.Split('.')[0].TrimEnd(' '));

    /// <summary>A base name that every file system takes with an extension after it: no
    /// separator, no character any of them refuses, and no name Windows keeps for a device.</summary>
    public static bool IsPlainBaseName(string name) => !name.Any(IsRefusedInAName) && !IsReservedOnWindows(name);

    /// <summary>The message of a base name that is not plain.</summary>
    public const string BaseNameMessage =
        "The base name must be a plain file name (no separators, no characters Windows refuses such as ':' or '?', "
        + "and not a device name such as 'CON').";

    /// <summary>Every channel within 0–1 (and not NaN).</summary>
    public static bool IsUnitColor(ColorRgb c) => c.R is >= 0 and <= 1 && c.G is >= 0 and <= 1 && c.B is >= 0 and <= 1;
}
