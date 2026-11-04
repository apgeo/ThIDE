// Gesture strings as the user reads them. The keyboard map stores Avalonia's own spelling
// ("Ctrl+OemCloseBrackets"), which KeyGesture.Parse reads back; Settings ▸ Keyboard shows the key
// the user presses ("Ctrl+]") and, on macOS, the platform's modifier symbols ("⌘⇧F").

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Input;

namespace ThIDE.Services;

public static class KeyGestureText
{
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.OemCloseBrackets] = "]", [Key.OemOpenBrackets] = "[", [Key.OemQuestion] = "/",
        [Key.OemPeriod] = ".", [Key.OemComma] = ",", [Key.OemPlus] = "=", [Key.OemMinus] = "-",
        [Key.OemSemicolon] = ";", [Key.OemQuotes] = "'", [Key.OemPipe] = "\\", [Key.OemBackslash] = "\\",
        [Key.OemTilde] = "`",
        [Key.Add] = "Num +", [Key.Subtract] = "Num -", [Key.Multiply] = "Num *", [Key.Divide] = "Num /",
        [Key.Decimal] = "Num .",
        [Key.Return] = "Enter", [Key.Escape] = "Esc", [Key.Back] = "Backspace", [Key.Delete] = "Delete",
        [Key.PageUp] = "PageUp", [Key.PageDown] = "PageDown", [Key.Insert] = "Insert",
    };

    /// <summary>
    /// <paramref name="gesture"/> (the stored spelling) as the user reads it on this platform; the
    /// text itself when it does not parse, and "" for "".
    /// </summary>
    public static string Format(string? gesture) => Format(gesture, OperatingSystem.IsMacOS());

    /// <summary><see cref="Format(string?)"/> with the platform given (tests).</summary>
    public static string Format(string? gesture, bool macOS)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;
        KeyGesture parsed;
        try { parsed = KeyGesture.Parse(gesture); }
        catch (Exception) { return gesture; }

        var key = KeyName(parsed.Key);
        var m = parsed.KeyModifiers;
        if (macOS)
        {
            // Apple's order: Control, Option, Shift, Command, then the key with no separator.
            var sb = new StringBuilder();
            if (m.HasFlag(KeyModifiers.Control)) sb.Append('⌃');
            if (m.HasFlag(KeyModifiers.Alt)) sb.Append('⌥');
            if (m.HasFlag(KeyModifiers.Shift)) sb.Append('⇧');
            if (m.HasFlag(KeyModifiers.Meta)) sb.Append('⌘');
            return sb.Append(key).ToString();
        }
        var parts = new List<string>(5);
        if (m.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(KeyModifiers.Meta)) parts.Add(OperatingSystem.IsWindows() ? "Win" : "Super");
        parts.Add(key);
        return string.Join("+", parts);
    }

    /// <summary>True when the two stored gestures are the same key chord (spelling and case aside).</summary>
    public static bool Same(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return KeyGesture.Parse(a).Equals(KeyGesture.Parse(b)); }
        catch (Exception) { return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase); }
    }

    private static string KeyName(Key key) =>
        Names.TryGetValue(key, out var name) ? name
        : key is >= Key.D0 and <= Key.D9 ? ((char)('0' + (key - Key.D0))).ToString()
        : key is >= Key.NumPad0 and <= Key.NumPad9 ? "Num " + (char)('0' + (key - Key.NumPad0))
        : key.ToString();
}
