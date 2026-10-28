// Snippets in the editor: the catalog (built-ins, the user's snippets.json in app data, the
// project's snippets.json in its config folder) and the insertion, which renders the template with
// the standard variables and hands it to AvaloniaEdit's snippet session — Tab and Shift+Tab move
// between the fields, a field used twice is mirrored, Enter or Esc ends the session.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;
using Therion.Workspace;
using Therion.Workspace.Templates;
using ThIDE.Resources;

namespace ThIDE.Editor;

internal static class TherionSnippets
{
    private static IReadOnlyList<SnippetDefinition>? _all;
    private static string? _projectRoot;
    private static ProjectConfigLocation _location;
    private static readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>Raised once per broken snippet file per session, with what was wrong.</summary>
    public static event EventHandler<IReadOnlyList<TemplateError>>? LoadErrors;

    public static IReadOnlyList<SnippetDefinition> All => _all ??= Load();

    /// <summary>Switches the project layer (called when the workspace root changes).</summary>
    public static void SetProject(string? root, ProjectConfigLocation location)
    {
        _projectRoot = root;
        _location = location;
        _all = null;
    }

    /// <summary>Forgets the loaded catalog so the next use re-reads the snippet files.</summary>
    public static void Reload() => _all = null;

    public static SnippetDefinition? Find(string trigger, string? filePath) =>
        All.FirstOrDefault(s => string.Equals(s.Trigger, trigger, StringComparison.OrdinalIgnoreCase) && s.AppliesTo(filePath));

    public static IEnumerable<SnippetDefinition> For(string? filePath) => All.Where(s => s.AppliesTo(filePath));

    public static string DescriptionOf(SnippetDefinition s) => s.DescriptionKey is { } key ? Tr.Get(key) : s.Description;

    /// <summary>
    /// Replaces <c>[start, start+length)</c> with the snippet: variables filled in from the file and
    /// the caret, continuation lines indented to the trigger's column, and a field session started
    /// for its tab stops.
    /// </summary>
    public static void Insert(TextArea area, int start, int length, SnippetDefinition snippet, string? filePath)
    {
        var (template, error) = SnippetTemplate.Parse(snippet.Template);
        if (template is null)
        {
            LoadErrors?.Invoke(null, [error! with { Source = snippet.Trigger }]);
            return;
        }

        var doc = area.Document;
        var vars = SnippetVariables.Standard(new SnippetContext(filePath, doc.Text, start, DateTime.Now));
        // AvaloniaEdit indents inserted line breaks to the caret line itself, so render flat.
        var rendered = template.Render(vars, indent: "", newline: "\n");

        // One undo step: the trigger and the expansion go together.
        using (doc.RunUpdate())
        {
            doc.Remove(start, length);
            area.Caret.Offset = start;
            ToAvaloniaSnippet(rendered).Insert(area);
        }
    }

    /// <summary>Text runs, fields (first use of an index) and mirrors (later uses), plus the final caret.</summary>
    private static Snippet ToAvaloniaSnippet(RenderedSnippet r)
    {
        var snippet = new Snippet();
        var fields = new Dictionary<int, SnippetReplaceableTextElement>();
        int pos = 0;
        bool caretPlaced = false;

        void Text(int upTo)
        {
            if (!caretPlaced && r.FinalCaret >= pos && r.FinalCaret <= upTo && r.FinalCaret < r.Text.Length)
            {
                if (r.FinalCaret > pos) snippet.Elements.Add(new SnippetTextElement { Text = r.Text[pos..r.FinalCaret] });
                snippet.Elements.Add(new SnippetCaretElement());
                caretPlaced = true;
                pos = r.FinalCaret;
            }
            if (upTo > pos) snippet.Elements.Add(new SnippetTextElement { Text = r.Text[pos..upTo] });
            pos = Math.Max(pos, upTo);
        }

        foreach (var stop in r.Stops.OrderBy(s => s.Start))
        {
            Text(stop.Start);
            var text = r.Text.Substring(stop.Start, stop.Length);
            if (!stop.IsMirror && !fields.ContainsKey(stop.Index))
            {
                var field = new SnippetReplaceableTextElement { Text = text };
                fields[stop.Index] = field;
                snippet.Elements.Add(field);
            }
            else if (fields.TryGetValue(stop.Index, out var target))
                snippet.Elements.Add(new SnippetBoundElement { TargetElement = target });
            else
                snippet.Elements.Add(new SnippetTextElement { Text = text });
            pos = stop.Start + stop.Length;
        }
        Text(r.Text.Length);
        if (!caretPlaced) snippet.Elements.Add(new SnippetCaretElement());
        return snippet;
    }

    private static IReadOnlyList<SnippetDefinition> Load()
    {
        var layers = new List<SnippetLayer> { Layer("snippets.json (user)", UserSnippetPath()) };
        if (ProjectLocalConfig.PathFor(_projectRoot, _location, "snippets.json") is { } projectFile)
            layers.Add(Layer("snippets.json (project)", projectFile));

        var (snippets, errors) = SnippetCatalog.Load(SnippetCatalog.BuiltIns, layers.ToArray());
        var fresh = errors.Where(e => _reported.Add(e.ToString())).ToList();
        if (fresh.Count > 0) LoadErrors?.Invoke(null, fresh);
        return snippets;
    }

    private static SnippetLayer Layer(string name, string path)
    {
        try { return new SnippetLayer(name, File.Exists(path) ? File.ReadAllText(path) : null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new SnippetLayer(name, null); }
    }

    public static string UserSnippetPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", "snippets.json");
    }
}
