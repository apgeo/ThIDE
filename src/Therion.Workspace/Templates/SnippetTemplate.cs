// The template language shared by snippets, file templates and project templates: a subset of the
// VS Code snippet syntax, so snippet files written for other editors read the same way.
//
//   $0                 final caret            $1 $2 …          tab stops (an index used twice mirrors)
//   ${1:text}          stop with a default    ${1|a,b,c|}      choice (the first is the default)
//   $NAME ${NAME}      variable               ${NAME:default}  variable with a fallback
//   \$ \} \\           literal $, }, \
//
// A '$' not followed by a digit, an upper-case name or '{' is literal text, so Therion source pasted
// into a template needs no escaping.

using System.Text;

namespace Therion.Workspace.Templates;

public abstract record TemplateSegment;
public sealed record TextSegment(string Text) : TemplateSegment;
/// <summary>A tab stop; index 0 is the final caret.</summary>
public sealed record TabStopSegment(int Index, string? Default, IReadOnlyList<string>? Choices) : TemplateSegment;
public sealed record VariableSegment(string Name, string? Default) : TemplateSegment;

/// <summary>A template that could not be read: where and why.</summary>
public sealed record TemplateError(int Position, string Message, string? Source = null)
{
    public override string ToString() => Source is null ? $"{Message} (at {Position})" : $"{Source}: {Message} (at {Position})";
}

/// <param name="IsMirror">A later use of an index: it repeats what is typed into the first.</param>
public sealed record RenderedStop(int Index, int Start, int Length, IReadOnlyList<string>? Choices, bool IsMirror);

/// <param name="Stops">Tab stops in the rendered text (not including the final caret), in document order.</param>
/// <param name="FinalCaret">Where the caret ends: <c>$0</c>, or the end of the text.</param>
public sealed record RenderedSnippet(string Text, IReadOnlyList<RenderedStop> Stops, int FinalCaret);

public sealed class SnippetTemplate
{
    private SnippetTemplate(IReadOnlyList<TemplateSegment> segments) => Segments = segments;

    public IReadOnlyList<TemplateSegment> Segments { get; }

    /// <summary>Parses a template. Never throws: a malformed template is an error value.</summary>
    public static (SnippetTemplate? Template, TemplateError? Error) Parse(string source)
    {
        var segments = new List<TemplateSegment>();
        var text = new StringBuilder();
        int i = 0;

        void FlushText()
        {
            if (text.Length > 0) { segments.Add(new TextSegment(text.ToString())); text.Clear(); }
        }

        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\\' && i + 1 < source.Length && source[i + 1] is '$' or '}' or '\\')
            {
                text.Append(source[i + 1]);
                i += 2;
                continue;
            }
            if (c != '$' || i + 1 >= source.Length)
            {
                text.Append(c);
                i++;
                continue;
            }

            char next = source[i + 1];
            if (char.IsAsciiDigit(next))
            {
                int start = i + 1, end = start;
                while (end < source.Length && char.IsAsciiDigit(source[end])) end++;
                if (!int.TryParse(source.AsSpan(start, end - start), out var number))
                    return (null, new TemplateError(start, "tab stop number is too large"));
                FlushText();
                segments.Add(new TabStopSegment(number, null, null));
                i = end;
                continue;
            }
            if (IsNameStart(next))
            {
                int start = i + 1, end = start;
                while (end < source.Length && IsNamePart(source[end])) end++;
                FlushText();
                segments.Add(new VariableSegment(source[start..end], null));
                i = end;
                continue;
            }
            if (next == '{')
            {
                var (segment, after, error) = ParseBraced(source, i);
                if (error is not null) return (null, error);
                FlushText();
                segments.Add(segment!);
                i = after;
                continue;
            }
            text.Append(c);
            i++;
        }
        FlushText();
        return (new SnippetTemplate(segments), null);
    }

    // ${…} starting at the '$'.
    private static (TemplateSegment? Segment, int After, TemplateError? Error) ParseBraced(string s, int dollar)
    {
        int i = dollar + 2;
        if (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            int start = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (!int.TryParse(s.AsSpan(start, i - start), out var index))
                return (null, 0, new TemplateError(start, "tab stop number is too large"));
            if (i < s.Length && s[i] == '}') return (new TabStopSegment(index, null, null), i + 1, null);
            if (i < s.Length && s[i] == ':')
            {
                var (body, after) = ReadUntilClose(s, i + 1);
                return after < 0 ? (null, 0, Unterminated(dollar)) : (new TabStopSegment(index, body, null), after, null);
            }
            if (i < s.Length && s[i] == '|')
            {
                int close = s.IndexOf("|}", i + 1, StringComparison.Ordinal);
                if (close < 0) return (null, 0, Unterminated(dollar));
                var choices = SplitChoices(s[(i + 1)..close]);
                return (new TabStopSegment(index, choices[0], choices), close + 2, null);
            }
            return (null, 0, new TemplateError(i, "expected '}', ':' or '|' after the tab stop number"));
        }
        if (i < s.Length && IsNameStart(s[i]))
        {
            int start = i;
            while (i < s.Length && IsNamePart(s[i])) i++;
            var name = s[start..i];
            if (i < s.Length && s[i] == '}') return (new VariableSegment(name, null), i + 1, null);
            if (i < s.Length && s[i] == ':')
            {
                var (body, after) = ReadUntilClose(s, i + 1);
                return after < 0 ? (null, 0, Unterminated(dollar)) : (new VariableSegment(name, body), after, null);
            }
            return (null, 0, new TemplateError(i, "expected '}' or ':' after the variable name"));
        }
        return (null, 0, new TemplateError(dollar, "'${' must be followed by a tab stop number or an upper-case variable name"));
    }

    // Default text up to the matching '}', honouring escapes and nested braces.
    private static (string Body, int After) ReadUntilClose(string s, int from)
    {
        var sb = new StringBuilder();
        int depth = 0;
        for (int i = from; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && s[i + 1] is '$' or '}' or '\\') { sb.Append(s[++i]); continue; }
            if (c == '{') depth++;
            if (c == '}')
            {
                if (depth == 0) return (sb.ToString(), i + 1);
                depth--;
            }
            sb.Append(c);
        }
        return ("", -1);
    }

    // Options are separated by unescaped commas; "\," and "\|" are literal.
    private static List<string> SplitChoices(string body)
    {
        var choices = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] is ',' or '|' or '\\') { sb.Append(body[++i]); continue; }
            if (body[i] == ',') { choices.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(body[i]);
        }
        choices.Add(sb.ToString());
        return choices;
    }

    private static TemplateError Unterminated(int at) => new(at, "'${' is not closed with '}'");

    private static bool IsNameStart(char c) => c is >= 'A' and <= 'Z' or '_';
    private static bool IsNamePart(char c) => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    /// <summary>Names of the variables the template uses, in order of first use.</summary>
    public IReadOnlyList<string> VariableNames =>
        Segments.OfType<VariableSegment>().Select(v => v.Name).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Renders the template. Line breaks become <paramref name="newline"/> followed by
    /// <paramref name="indent"/> (continuation lines line up under the first), in variable values too.
    /// A variable <paramref name="variables"/> cannot resolve and that has no default becomes a tab
    /// stop holding its name, so the user fills it in.
    /// </summary>
    public RenderedSnippet Render(Func<string, string?> variables, string indent = "", string newline = "\n")
    {
        var sb = new StringBuilder();
        var stops = new List<RenderedStop>();
        var seen = new HashSet<int>();
        int finalCaret = -1;
        int nextFree = Segments.OfType<TabStopSegment>().Select(t => t.Index).DefaultIfEmpty(0).Max() + 1;
        var unknownStops = new Dictionary<string, int>(StringComparer.Ordinal);
        // A bare $1 repeats whatever ${1:…} said, wherever the default was written.
        var defaults = new Dictionary<int, (string? Text, IReadOnlyList<string>? Choices)>();
        foreach (var t in Segments.OfType<TabStopSegment>())
            if (t.Default is not null && !defaults.ContainsKey(t.Index)) defaults[t.Index] = (t.Default, t.Choices);

        void Append(string value)
        {
            var lines = value.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append(newline).Append(indent);
                sb.Append(lines[i]);
            }
        }

        void Stop(int index, string text, IReadOnlyList<string>? choices)
        {
            int start = sb.Length;
            Append(text);
            stops.Add(new RenderedStop(index, start, sb.Length - start, choices, IsMirror: !seen.Add(index)));
        }

        foreach (var segment in Segments)
        {
            switch (segment)
            {
                case TextSegment t:
                    Append(t.Text);
                    break;
                case TabStopSegment { Index: 0 }:
                    if (finalCaret < 0) finalCaret = sb.Length;
                    break;
                case TabStopSegment s:
                    var fallback = defaults.GetValueOrDefault(s.Index);
                    Stop(s.Index, s.Default ?? fallback.Text ?? "", s.Choices ?? fallback.Choices);
                    break;
                case VariableSegment v:
                    var value = variables(v.Name) ?? v.Default;
                    if (value is not null) { Append(value); break; }
                    if (!unknownStops.TryGetValue(v.Name, out var idx)) unknownStops[v.Name] = idx = nextFree++;
                    Stop(idx, v.Name, null);
                    break;
            }
        }

        // Tab order: by index, then position (VS Code order); the final caret is not a stop.
        var ordered = stops.OrderBy(s => s.Index).ThenBy(s => s.Start).ToList();
        return new RenderedSnippet(sb.ToString(), ordered, finalCaret < 0 ? sb.Length : finalCaret);
    }
}
