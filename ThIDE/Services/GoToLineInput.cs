// Reads the line number typed into Go To Line against the file's line count. A number outside
// the file, or text that is no number, comes back with the sentence to show instead of being
// dropped silently.

using System.Globalization;
using ThIDE.Resources;

namespace ThIDE.Services;

public static class GoToLineInput
{
    /// <summary>True with the 1-based <paramref name="line"/> when <paramref name="text"/> names a line of the
    /// file. Otherwise false; <paramref name="problem"/> is the localized reason, or null when nothing was entered.</summary>
    public static bool TryResolve(string? text, int lineCount, out int line, out string? problem)
    {
        line = 0;
        problem = null;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            problem = string.Format(Tr.Get("Notif_GoToLineNotNumber"), text, lineCount);
            return false;
        }
        if (n < 1 || n > lineCount)
        {
            problem = string.Format(Tr.Get("Notif_GoToLineOutOfRange"), n, lineCount);
            return false;
        }
        line = n;
        return true;
    }
}
