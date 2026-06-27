// EDIT-03 — code snippets / templates. Tab-expandable (and completion-listed) templates for the
// common Therion blocks. Built-in defaults can be extended/overridden by a user-editable file at
// %AppData%/TherionProc/snippets.json (XDG fallback on POSIX): an array of {trigger, description,
// template}. Templates use \n for line breaks and $0 to mark the final caret position.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace TherionProc.Editor;

internal static class TherionSnippets
{
    public sealed record Snippet(string Trigger, string Description, string Template);

    // Lazy so Load() runs on first access (after the type initializer has populated Defaults) rather
    // than during static-field init, where Defaults (declared lower in the file) would still be null.
    private static IReadOnlyList<Snippet>? _all;
    public static IReadOnlyList<Snippet> All => _all ??= Load();

    public static Snippet? Find(string trigger) =>
        All.FirstOrDefault(s => string.Equals(s.Trigger, trigger, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Replaces <c>[start, start+length)</c> with the rendered snippet (continuation lines indented
    /// to the trigger's column) and positions the caret at the <c>$0</c> marker.
    /// </summary>
    public static void Insert(TextArea area, int start, int length, Snippet snippet)
    {
        var doc = area.Document;
        var line = doc.GetLineByOffset(start);
        var indent = LeadingWhitespace(doc.GetText(line.Offset, start - line.Offset));
        var nl = doc.GetLineByNumber(1).DelimiterLength == 2 ? "\r\n" : "\n";
        var (body, caret) = Render(snippet.Template, indent, nl);
        doc.Replace(start, length, body);
        area.Caret.Offset = Math.Min(start + caret, doc.TextLength);
    }

    private static (string Body, int Caret) Render(string template, string indent, string nl)
    {
        var lines = template.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append(nl).Append(indent);
            sb.Append(lines[i]);
        }
        var rendered = sb.ToString();
        int caret = rendered.IndexOf("$0", StringComparison.Ordinal);
        if (caret >= 0) rendered = rendered.Remove(caret, 2);
        else caret = rendered.Length;
        return (rendered, caret);
    }

    private static string LeadingWhitespace(string text)
    {
        int i = 0;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text.Substring(0, i);
    }

    // ---- loading (built-in defaults + optional user overrides) ----

    private sealed class SnippetDto
    {
        public string? Trigger { get; set; }
        public string? Description { get; set; }
        public string? Template { get; set; }
    }

    private static IReadOnlyList<Snippet> Load()
    {
        var map = new Dictionary<string, Snippet>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Defaults) map[s.Trigger] = s;
        try
        {
            var path = UserSnippetPath();
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<List<SnippetDto>>(File.ReadAllText(path)) is { } dtos)
            {
                foreach (var d in dtos)
                    if (!string.IsNullOrWhiteSpace(d.Trigger) && d.Template is not null)
                        map[d.Trigger!] = new Snippet(d.Trigger!, d.Description ?? d.Trigger!, d.Template);
            }
        }
        catch { /* ignore a malformed user file — keep the built-in defaults */ }
        return map.Values.OrderBy(s => s.Trigger, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string UserSnippetPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "TherionProc", "snippets.json");
    }

    private static readonly Snippet[] Defaults =
    {
        new("survb", "survey … endsurvey block",
            "survey $0 -title \"\"\n  centreline\n    date \n    team \"\"\n    data normal from to length compass clino\n    \n  endcentreline\nendsurvey"),
        new("clt", "centreline + data normal",
            "centreline\n  date $0\n  data normal from to length compass clino\n  \nendcentreline"),
        new("datan", "data normal reading order",
            "data normal from to length compass clino$0"),
        new("scrapb", "scrap … endscrap block",
            "scrap $0 -projection plan -scale [0 0 1 0 0 0 1 0 m]\n  \nendscrap"),
        new("mapb", "map … endmap block",
            "map $0\n  \nendmap"),
        new("thconf", "thconfig skeleton",
            "encoding  utf-8\nsource $0\n\nlayout layout1\nendlayout\n\nselect 1\nexport map -layout layout1 -output map.pdf"),
    };
}
