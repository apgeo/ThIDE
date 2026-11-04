// The editor takes a new text from its view-model (a save's cleanup, a reload, a rename) as one replace
// of the part that differs, so the caret, the undo history and the folds outside it survive. Kept apart
// from the control so the diff can be checked without one.

using System;

namespace ThIDE.Editor;

/// <summary>One replace: <see cref="Length"/> characters at <see cref="Offset"/> become <see cref="Text"/>.</summary>
internal readonly record struct BufferReplacement(int Offset, int Length, string Text);

internal static class BufferSync
{
    /// <summary>Turns <paramref name="current"/> into <paramref name="text"/> with one replace of the span
    /// between their common prefix and suffix.</summary>
    public static BufferReplacement ChangedPart(string current, string text)
    {
        int max = Math.Min(current.Length, text.Length);
        int prefix = 0;
        while (prefix < max && current[prefix] == text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && current[current.Length - 1 - suffix] == text[text.Length - 1 - suffix]) suffix++;
        return new BufferReplacement(prefix, current.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
    }
}
