// EDIT-14 — trailing-whitespace trim + final-newline normalisation applied on save.
// Text-level (idempotent) cleanup used as the save funnel's fallback; the editor also applies
// the same rules in place (caret-preserving) when a view is loaded.

using System;
using System.Text.RegularExpressions;

namespace TherionProc.Services;

public static class EditorTextCleanup
{
    /// <summary>
    /// Applies the on-save cleanup when EDIT-14 is enabled (both the compile-time flag and the
    /// user setting); otherwise returns <paramref name="text"/> unchanged.
    /// </summary>
    public static string ApplyOnSave(string text, AppSettings? settings) =>
        EditorFeatureFlags.IsEnabled(EditorFeature.TrimTrailingOnSave, settings)
            ? Clean(text, ensureFinalNewline: true)
            : text;

    /// <summary>
    /// Removes trailing spaces/tabs from every line (preserving the file's newline style) and,
    /// when <paramref name="ensureFinalNewline"/> is set, guarantees the file ends with a newline.
    /// Idempotent.
    /// </summary>
    public static string Clean(string text, bool ensureFinalNewline)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        // Strip whitespace that sits immediately before a line break…
        var cleaned = Regex.Replace(text, @"[ \t]+(?=\r?\n)", "");
        // …and any trailing whitespace on the final (newline-less) line.
        cleaned = Regex.Replace(cleaned, @"[ \t]+\z", "");
        if (ensureFinalNewline && cleaned.Length > 0 && !cleaned.EndsWith("\n", StringComparison.Ordinal))
            cleaned += nl;
        return cleaned;
    }
}
