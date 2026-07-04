// TODO / FIXME / QM aggregator. Scans Therion source comments (everything after a `#`)
// for tag words and returns a navigable list. Purely syntactic (no model), so it runs over any
// text and is unit-testable; the app aggregates across all project files into a "TODOs" panel.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Therion.Core;

namespace Therion.Semantics;

/// <summary>One tagged comment: its tag, the text following the tag, and where it is.</summary>
public sealed record TodoItem(string Tag, string Text, SourceSpan Span);

public static class TodoScanner
{
    // The recognised tags (the roadmap's # TODO / # FIXME / # CHECK / # QM plus common siblings).
    private static readonly Regex TagRegex = new(
        @"\b(TODO|FIXME|CHECK|QM|BUG|HACK|XXX)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ImmutableArray<TodoItem> Scan(string path, string text)
    {
        if (string.IsNullOrEmpty(text)) return ImmutableArray<TodoItem>.Empty;

        var items = ImmutableArray.CreateBuilder<TodoItem>();
        int offset = 0, line = 1;
        foreach (var rawLine in SplitKeepingOffsets(text))
        {
            var content = rawLine.TrimEnd('\r');
            int hash = content.IndexOf('#');
            if (hash >= 0)
            {
                var comment = content[hash..];
                var match = TagRegex.Match(comment);
                if (match.Success)
                {
                    int col = hash + match.Index + 1;             // 1-based column of the tag
                    int startOffset = offset + hash + match.Index;
                    var span = new SourceSpan(path,
                        new SourceLocation(line, col),
                        new SourceLocation(line, col + match.Length),
                        startOffset, match.Length);
                    // Text is only what follows the tag word (drops the leading `#`, the tag and any
                    // `:`/`-` separator), so "# TODO: fix this" surfaces as "fix this".
                    var body = comment[(match.Index + match.Length)..].TrimStart(' ', '\t', ':', '-').Trim();
                    items.Add(new TodoItem(match.Value.ToUpperInvariant(), body, span));
                }
            }
            offset += rawLine.Length + 1;   // + the '\n' consumed by the split
            line++;
        }
        return items.ToImmutable();
    }

    // Splits on '\n' but yields each line's text so the caller can track byte offsets.
    private static IEnumerable<string> SplitKeepingOffsets(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                yield return text[start..i];
                start = i + 1;
            }
        if (start <= text.Length) yield return text[start..];
    }
}
