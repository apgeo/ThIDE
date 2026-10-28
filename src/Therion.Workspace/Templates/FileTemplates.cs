// File templates: what File ▸ New File… offers. A template is a body in the snippet template language
// (variables filled when the file is created, tab stops rendered with their defaults, $0 for where
// the caret starts) plus the file's extension. Layers like snippets: built-ins, then the user's
// `templates/files/*.json` in app data, then the project's in its config folder.

using System.Text;
using System.Text.Json;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Templates;

/// <param name="Id">Stable id; a later layer with the same id replaces the template.</param>
/// <param name="DefaultName">File name stem offered first (a number is added when it is taken).</param>
/// <param name="TitleKey">For built-ins: resource keys the app translates.</param>
/// <param name="ActivateThconfig">Make the new file the active thconfig once created.</param>
public sealed record FileTemplate(
    string Id, string Title, string Description, string Extension, string DefaultName, string Body,
    bool ActivateThconfig = false, string? TitleKey = null, string? DescriptionKey = null)
{
    /// <summary>Survey files are the ones another file inputs.</summary>
    public bool IsSurveyFile => Extension is ".th" or ".th2";
}

/// <summary>The new file and where its caret starts.</summary>
public sealed record NewFilePlan(EditBatch Batch, string FilePath, string Content, int Caret, string? InputLine, bool InputExists);

public static class FileTemplateCatalog
{
    private sealed class Dto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Extension { get; set; }
        public string? DefaultName { get; set; }
        public string? Body { get; set; }
        public bool ActivateThconfig { get; set; }
    }

    /// <summary>Built-ins overlaid by template files (one JSON object per file); broken ones are reported.</summary>
    public static (IReadOnlyList<FileTemplate> Templates, IReadOnlyList<TemplateError> Errors) Load(
        IEnumerable<FileTemplate> builtIns, IEnumerable<SnippetLayer> files)
    {
        var map = new Dictionary<string, FileTemplate>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var errors = new List<TemplateError>();
        foreach (var t in builtIns) { map[t.Id] = t; order.Add(t.Id); }

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Json)) continue;
            Dto? d;
            try { d = JsonSerializer.Deserialize<Dto>(file.Json, ProjectLocalConfig.JsonOptions); }
            catch (JsonException ex) { errors.Add(new TemplateError((int)(ex.LineNumber ?? 0) + 1, ex.Message, file.Name)); continue; }
            if (d is null || string.IsNullOrWhiteSpace(d.Id) || d.Body is null)
            {
                errors.Add(new TemplateError(0, "a template needs an id and a body", file.Name));
                continue;
            }
            if (SnippetTemplate.Parse(d.Body).Error is { } error) { errors.Add(error with { Source = file.Name }); continue; }
            var ext = string.IsNullOrWhiteSpace(d.Extension) ? ".th" : d.Extension.StartsWith('.') ? d.Extension : "." + d.Extension;
            if (!map.ContainsKey(d.Id)) order.Add(d.Id);
            map[d.Id] = new FileTemplate(d.Id, d.Title ?? d.Id, d.Description ?? "", ext, d.DefaultName ?? d.Id, d.Body, d.ActivateThconfig);
        }
        return (order.Select(id => map[id]).ToList(), errors);
    }

    public static IReadOnlyList<FileTemplate> BuiltIns { get; } =
    [
        new("th-trip", "Survey trip", "A survey with one centreline: date, team, units and data line from the last trip in the open file.",
            ".th", "trip",
            "encoding utf-8\nsurvey ${SURVEY_NAME} -title \"${SURVEY_NAME}\"\n  centreline\n    date $DATE\n    ${TEAM_RECENT:team \"\"}\n" +
            "    ${LAST_UNITS:units length meters}\n    ${LAST_DATA:data normal from to length compass clino}\n    $0\n  endcentreline\nendsurvey\n",
            TitleKey: "FileTpl_Trip", DescriptionKey: "FileTpl_TripDesc"),
        new("th-connection", "Connection survey", "A wrapper survey that inputs other surveys and equates them.",
            ".th", "connection",
            "encoding utf-8\nsurvey ${SURVEY_NAME}\n  input ${1:part1.th}\n  input ${2:part2.th}\n\n  equate ${3:1@part1} ${4:1@part2}$0\nendsurvey\n",
            TitleKey: "FileTpl_Connection", DescriptionKey: "FileTpl_ConnectionDesc"),
        new("thconfig-basic", "Thconfig", "Compiles one survey file to a PDF map and a 3D model.",
            ".thconfig", "thconfig",
            "encoding utf-8\nsource ${1:main.th}\n\nlayout ${2:layout1}\n  scale 1 500\nendlayout\n\nselect ${3:cave}\nexport map -layout $2 -output ${4:map.pdf}\nexport model -output ${5:model.lox}\n$0",
            ActivateThconfig: true, TitleKey: "FileTpl_Thconfig", DescriptionKey: "FileTpl_ThconfigDesc"),
        new("th2-scrap", "Sketch (th2)", "An empty plan scrap ready for drawing.",
            ".th2", "sketch",
            "encoding utf-8\n##XTHERION## xth_me_area_adjust 0 0 1000 1000\n\nscrap s_${SURVEY_NAME} -projection plan -scale [0 0 1 0 0 0 1 0 m]\n$0\nendscrap\n",
            TitleKey: "FileTpl_Scrap", DescriptionKey: "FileTpl_ScrapDesc"),
        new("th-layout-library", "Layout library", "Shared layouts a thconfig can copy from.",
            ".th", "layouts",
            "encoding utf-8\n# Layouts shared by this project's thconfigs (use: layout name -> copy ${1:base})\nlayout ${1:base}\n  scale 1 500\n  $0\nendlayout\n",
            TitleKey: "FileTpl_Layouts", DescriptionKey: "FileTpl_LayoutsDesc"),
        new("blank", "Blank file", "An empty survey file.", ".th", "new", "encoding utf-8\n$0",
            TitleKey: "FileTpl_Blank", DescriptionKey: "FileTpl_BlankDesc"),
    ];
}

public static class NewFilePlanner
{
    /// <summary>A file name stem made into a valid Therion identifier (survey and scrap names).</summary>
    public static string IdentifierFrom(string stem)
    {
        var sb = new StringBuilder(stem.Length);
        foreach (var c in stem.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length == 0 ? "survey" : sb.ToString();
    }

    /// <summary>The first free name: <c>trip.th</c>, then <c>trip-2.th</c>, <c>trip-3.th</c>…</summary>
    public static string FreeName(string folder, string stem, string extension, Func<string, bool> exists)
    {
        var name = stem + extension;
        for (int n = 2; exists(Path.Combine(folder, name)); n++) name = $"{stem}-{n}{extension}";
        return name;
    }

    /// <summary>
    /// Renders the template for <paramref name="filePath"/> and, with <paramref name="inputInto"/>, the
    /// <c>input</c> line that brings the new file into that survey file (after its last <c>input</c>,
    /// else inside its first survey block, else at the top) unless it is already there.
    /// </summary>
    /// <param name="variables">Resolver for the template's variables (the standard ones, usually from
    /// the file open in the editor); SURVEY_NAME defaults to the file name.</param>
    public static NewFilePlan Plan(FileTemplate template, string filePath, Func<string, string?> variables,
        string? inputInto = null, Func<string, string?>? readText = null)
    {
        var name = IdentifierFrom(Path.GetFileNameWithoutExtension(filePath));
        string? Vars(string v) => v switch
        {
            "SURVEY_NAME" => variables(v) ?? name,
            "FILE_NAME" => Path.GetFileName(filePath),
            "FILE_BASENAME" => Path.GetFileNameWithoutExtension(filePath),
            "FILE_DIR" => Path.GetDirectoryName(filePath),
            _ => variables(v),
        };

        var (parsed, _) = SnippetTemplate.Parse(template.Body);
        var rendered = parsed is null
            ? new RenderedSnippet(template.Body, [], template.Body.Length)
            : parsed.Render(Vars);

        var edits = new List<FileTextEdits>();
        string? inputLine = null;
        bool exists = false;
        if (inputInto is not null && readText?.Invoke(inputInto) is { } text)
        {
            var rel = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(inputInto))!, Path.GetFullPath(filePath)).Replace('\\', '/');
            inputLine = Import.Th2Scaffold.InputLine(rel);
            var ast = new ThParser().Parse(inputInto, text).Value;
            exists = AllInputs(ast).Any(i => FilePaths.Same(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputInto))!, i.Path.Trim('"')), filePath));
            if (!exists) edits.Add(new FileTextEdits(inputInto, [InsertInput(ast, text, inputLine)]));
        }

        var batch = new EditBatch(edits, [new FileCreate(filePath, rendered.Text)], [], RequiresWorkspaceReload: edits.Count > 0);
        return new NewFilePlan(batch, filePath, rendered.Text, rendered.FinalCaret, inputLine, exists);
    }

    private static IEnumerable<InputCommand> AllInputs(TherionFile? ast)
    {
        IEnumerable<InputCommand> Walk(IEnumerable<TherionNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (n is InputCommand i) yield return i;
                else if (n is BlockCommand b) foreach (var inner in Walk(b.Children)) yield return inner;
            }
        }
        return Walk(ast?.Children ?? []);
    }

    // After the last input (same indentation); else inside the first survey, one level in; else at
    // the top, after an encoding line.
    private static TextEdit InsertInput(TherionFile? ast, string text, string inputLine)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        int LineEnd(int offset)
        {
            int i = text.IndexOf('\n', Math.Min(offset, text.Length));
            return i < 0 ? text.Length : i + 1;
        }
        string IndentAt(int offset)
        {
            int start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1, i = start;
            while (i < text.Length && text[i] is ' ' or '\t') i++;
            return text[start..i];
        }
        string Line(int at, string indent) => (at == text.Length && text.Length > 0 && !text.EndsWith('\n') ? nl : "") + indent + inputLine + nl;

        if (AllInputs(ast).LastOrDefault() is { } last)
        {
            int at = LineEnd(last.Span.StartOffset);
            return TextEdit.Insert(at, Line(at, IndentAt(last.Span.StartOffset)));
        }
        if (ast?.Children.OfType<SurveyCommand>().FirstOrDefault() is { } survey)
        {
            int at = LineEnd(survey.Span.StartOffset);
            return TextEdit.Insert(at, Line(at, IndentAt(survey.Span.StartOffset) + "  "));
        }
        var encoding = ast?.Children.OfType<TherionCommand>().FirstOrDefault(c => c.Keyword == "encoding");
        int top = encoding is null ? 0 : LineEnd(encoding.Span.StartOffset);
        return TextEdit.Insert(top, Line(top, ""));
    }
}
