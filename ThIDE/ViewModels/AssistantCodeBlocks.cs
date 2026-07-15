// Splits an assistant answer into prose and fenced-code segments (CAP-03), so the pane can render a
// ```therion … ``` block as a code card with Copy / Insert / Replace. A plain fence scanner — no
// markdown library — matching the pane's no-dependency stance. Runs when an answer finalizes, never
// per streaming delta (while streaming the raw text shows as-is).

using System;
using System.Collections.Generic;

namespace ThIDE.ViewModels;

/// <param name="IsCode">True for a fenced code block; false for a run of prose.</param>
/// <param name="Text">The segment's text: for code, the block's contents without the fences.</param>
/// <param name="Language">The info string after the opening fence (e.g. "therion"), or "" — code only.</param>
public sealed record ChatSegment(bool IsCode, string Text, string Language);

public static class AssistantCodeBlocks
{
    /// <summary>
    /// Parses <paramref name="text"/> into ordered prose/code segments. Text with no fence comes back as
    /// a single prose segment (the whole thing), so the common case renders exactly as today. Blank prose
    /// between blocks is dropped; an unterminated opening fence takes the rest of the text as code (a
    /// truncated answer's snippet still gets a card).
    /// </summary>
    public static IReadOnlyList<ChatSegment> Parse(string? text)
    {
        var segments = new List<ChatSegment>();
        if (string.IsNullOrEmpty(text)) return segments;

        // Normalize CRLF/CR so line handling is uniform; the editor re-applies its own line endings on insert.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var buffer = new List<string>();
        bool inCode = false;
        string language = "";

        void FlushProse()
        {
            var prose = string.Join("\n", buffer).Trim('\n');
            if (prose.Trim().Length > 0) segments.Add(new ChatSegment(false, prose, ""));
            buffer.Clear();
        }

        void FlushCode()
        {
            segments.Add(new ChatSegment(true, string.Join("\n", buffer).Trim('\n'), language));
            buffer.Clear();
            language = "";
        }

        foreach (var line in lines)
        {
            if (IsFence(line, out var info))
            {
                if (inCode) { FlushCode(); inCode = false; }
                else { FlushProse(); inCode = true; language = info; }
                continue;
            }
            buffer.Add(line);
        }

        // Trailing buffer: a closed prose run, or an unterminated code block taken as code.
        if (inCode) FlushCode();
        else FlushProse();

        return segments;
    }

    /// <summary>A fence line: optional leading whitespace, then ≥3 backticks, then an info string.</summary>
    private static bool IsFence(string line, out string info)
    {
        info = "";
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return false;
        info = trimmed.TrimStart('`').Trim();
        return true;
    }
}
