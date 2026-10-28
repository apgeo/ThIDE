// Completion for LSP clients: the same snippets the editor offers (built-ins plus the user's
// snippets.json), with ThIDE's variables already filled in for the position, and Therion's command
// keywords. Keyword completion stops at the first word of a line, as in the editor; the editor's
// richer completion (flag values, stations, data fields) is not offered here.

using System.Text;
using Therion.Syntax;
using Therion.Workspace.Templates;

namespace Therion.Lsp;

/// <param name="Kind">LSP CompletionItemKind: 14 keyword, 15 snippet.</param>
/// <param name="IsSnippet">insertTextFormat 2 (snippet) rather than 1 (plain text).</param>
public sealed record CompletionItem(string Label, int Kind, string Detail, string InsertText, bool IsSnippet);

public static class CompletionProvider
{
    public static IReadOnlyList<CompletionItem> Complete(
        string path, string text, int line, int character, IReadOnlyList<SnippetDefinition> snippets, DateTime now)
    {
        int offset = OffsetOf(text, line, character);
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        var before = text[lineStart..offset];
        // Only the first word of a line starts a command or a snippet.
        if (before.TrimStart().Contains(' ') || before.TrimStart().Contains('\t')) return [];

        var items = new List<CompletionItem>();
        var vars = SnippetVariables.Standard(new SnippetContext(path, text, offset, now));
        foreach (var s in snippets.Where(s => s.AppliesTo(path)))
        {
            var (template, error) = SnippetTemplate.Parse(s.Template);
            if (template is null || error is not null) continue;
            items.Add(new CompletionItem(s.Trigger, 15, s.Description, ToLspSnippet(template.Render(vars)), true));
        }
        foreach (var keyword in TokenClassifier.Keywords.Order(StringComparer.Ordinal))
            items.Add(new CompletionItem(keyword, 14, "Therion command", keyword, false));
        return items;
    }

    /// <summary>
    /// A rendered snippet in LSP snippet syntax: variables are already values, stops become
    /// <c>${n:text}</c>, the final caret <c>$0</c>, and literal '$', '}' and '\' are escaped.
    /// </summary>
    public static string ToLspSnippet(RenderedSnippet r)
    {
        var sb = new StringBuilder();
        int pos = 0;
        bool caretDone = false;

        void Text(int upTo)
        {
            if (!caretDone && r.FinalCaret >= pos && r.FinalCaret <= upTo)
            {
                Escape(sb, r.Text[pos..r.FinalCaret]);
                sb.Append("$0");
                caretDone = true;
                pos = r.FinalCaret;
            }
            if (upTo > pos) Escape(sb, r.Text[pos..upTo]);
            pos = Math.Max(pos, upTo);
        }

        foreach (var stop in r.Stops.OrderBy(s => s.Start))
        {
            Text(stop.Start);
            sb.Append("${").Append(stop.Index).Append(':');
            Escape(sb, r.Text.Substring(stop.Start, stop.Length));
            sb.Append('}');
            pos = stop.Start + stop.Length;
        }
        Text(r.Text.Length);
        if (!caretDone) sb.Append("$0");
        return sb.ToString();
    }

    private static void Escape(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            if (c is '$' or '}' or '\\') sb.Append('\\');
            sb.Append(c);
        }
    }

    // LSP positions count UTF-16 code units, like .NET strings.
    private static int OffsetOf(string text, int line, int character)
    {
        int offset = 0;
        for (int l = 0; l < line && offset < text.Length; l++)
        {
            int nl = text.IndexOf('\n', offset);
            if (nl < 0) return text.Length;
            offset = nl + 1;
        }
        return Math.Min(text.Length, offset + character);
    }
}
