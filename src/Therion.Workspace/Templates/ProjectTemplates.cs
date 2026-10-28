// Project templates: a folder (or zip) with a template.json and the files of a new project, where
// paths and contents use ${variable}. The engine turns a template plus the user's values into the
// same ScaffoldPlan the TopoDroid scaffold produces — directories, text files, byte-for-byte copies
// — after checking every value with ScaffoldValueRules, so a value can neither break a line in a
// generated thconfig nor place a file outside the new project.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Therion.Workspace.Import;

namespace Therion.Workspace.Templates;

public enum TemplateVariableType { Text, Name, Number, Bool, Choice, Cs, Date }

/// <param name="Id">Lower-case identifier used as <c>${id}</c>.</param>
public sealed record TemplateVariable(
    string Id, string Prompt, string Default, TemplateVariableType Type = TemplateVariableType.Text,
    IReadOnlyList<string>? Choices = null, bool Required = true);

/// <summary>One file of a template: text (substituted) or a binary file copied as is.</summary>
/// <param name="SourcePath">For a binary: where it is, relative to the template folder.</param>
public sealed record TemplateFileEntry(string RelativePath, string? Text, string? SourcePath = null);

/// <param name="Folder">Where the template lives on disk (null for a built-in).</param>
/// <param name="OpenAfterCreate">Project-relative path (with variables) of the thconfig to open.</param>
public sealed record ProjectTemplate(
    string Id, string Title, string Description,
    IReadOnlyList<TemplateVariable> Variables,
    IReadOnlyList<TemplateFileEntry> Files,
    IReadOnlyList<string> Directories,
    string OpenAfterCreate,
    string? Folder = null,
    string? TitleKey = null);

public static partial class ProjectTemplateEngine
{
    // ---- loading -----------------------------------------------------------------------------

    private sealed class Dto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<VarDto>? Variables { get; set; }
        public List<string>? Directories { get; set; }
        public string? OpenAfterCreate { get; set; }
    }

    private sealed class VarDto
    {
        public string? Id { get; set; }
        public string? Prompt { get; set; }
        public string? Default { get; set; }
        public string? Type { get; set; }
        public List<string>? Choices { get; set; }
        public bool? Required { get; set; }
    }

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".th", ".th2", ".thconfig", ".thc", ".txt", ".md", ".json", ".xvi", ".svx", ".csv", ".gitignore", "",
    };

    /// <summary>Reads a template folder: <c>template.json</c> plus every other file under it.</summary>
    public static (ProjectTemplate? Template, string? Error) LoadFolder(string folder)
    {
        var manifest = Path.Combine(folder, "template.json");
        var read = ProjectLocalConfig.TryReadJson<Dto>(manifest);
        if (read.Error is { } error) return (null, error);
        if (read.Value is not { Id: { Length: > 0 } id } dto) return (null, $"{folder}: no template.json with an id");

        var files = new List<TemplateFileEntry>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(folder, path).Replace('\\', '/');
            if (rel == "template.json") continue;
            files.Add(TextExtensions.Contains(Path.GetExtension(path))
                ? new TemplateFileEntry(rel, File.ReadAllText(path))
                : new TemplateFileEntry(rel, null, rel));
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in dto.Variables ?? [])
        {
            if (string.IsNullOrWhiteSpace(v.Id) || !VariableIdRx().IsMatch(v.Id))
                return (null, $"{manifest}: variable id '{v.Id}' must be letters, digits and _ (it is written as ${{id}})");
            if (!ids.Add(v.Id)) return (null, $"{manifest}: variable '{v.Id}' is listed twice");
        }
        var variables = (dto.Variables ?? []).Select(v => new TemplateVariable(
            v.Id!, v.Prompt ?? v.Id!, v.Default ?? "",
            Enum.TryParse<TemplateVariableType>(v.Type, ignoreCase: true, out var t) ? t : TemplateVariableType.Text,
            v.Choices, v.Required ?? true)).ToList();
        return (new ProjectTemplate(id, dto.Title ?? id, dto.Description ?? "", variables, files, dto.Directories ?? [],
            dto.OpenAfterCreate ?? "", folder), null);
    }

    /// <summary>
    /// Imports a template folder or <c>.zip</c> into <paramref name="libraryFolder"/> (one sub-folder
    /// per template id). The zip may hold the template at its root or in one top-level folder.
    /// </summary>
    public static (ProjectTemplate? Template, string? Error) Import(string source, string libraryFolder)
    {
        var staging = Path.Combine(libraryFolder, ".import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Directory.Exists(source)) CopyTree(source, staging);
            else if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(source, staging);
            else return (null, $"{source}: not a folder or a .zip");

            // The template at the root, or in the one top-level folder (ignoring the extra folders
            // archivers add, such as macOS's __MACOSX).
            var folders = Directory.GetDirectories(staging)
                .Where(d => Path.GetFileName(d) is var n && n != "__MACOSX" && !n.StartsWith('.')).ToArray();
            var root = File.Exists(Path.Combine(staging, "template.json")) ? staging
                     : folders is [var only] && File.Exists(Path.Combine(only, "template.json")) ? only
                     : null;
            if (root is null) return (null, $"{Path.GetFileName(source)}: no template.json");
            var (template, error) = LoadFolder(root);
            if (template is null) return (null, error);
            if (!IsSafeId(template.Id)) return (null, $"'{template.Id}' is not a usable template id");
            if (BuiltIns.Any(b => b.Id == template.Id)) return (null, $"'{template.Id}' is the id of a built-in template; give the template another id");

            // Replace an earlier import of the same id only once the new one is in place.
            var target = Path.Combine(libraryFolder, template.Id);
            var previous = Directory.Exists(target) ? target + ".old-" + Guid.NewGuid().ToString("N")[..8] : null;
            if (previous is not null) Directory.Move(target, previous);
            try { Directory.Move(root, target); }
            catch
            {
                if (previous is not null) Directory.Move(previous, target);
                throw;
            }
            if (previous is not null) Directory.Delete(previous, recursive: true);
            return LoadFolder(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (null, ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    private static bool IsSafeId(string id) => id.Length > 0 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static void CopyTree(string from, string to)
    {
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    // ---- planning ----------------------------------------------------------------------------

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex VariableRx();

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}$")]
    private static partial Regex DateRx();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex VariableIdRx();

    /// <summary>The templates imported into a library folder (one sub-folder each; hidden ones skipped).</summary>
    public static IReadOnlyList<ProjectTemplate> LoadLibrary(string library)
    {
        if (!Directory.Exists(library)) return [];
        return Directory.EnumerateDirectories(library)
            .Where(d => !Path.GetFileName(d).StartsWith('.') && !Path.GetFileName(d).Contains(".old-"))
            .Order(StringComparer.Ordinal)
            .Select(d => LoadFolder(d).Template)
            .OfType<ProjectTemplate>()
            .ToList();
    }

    /// <summary>The values to start the form with: each variable's default (dates default to today).</summary>
    public static Dictionary<string, string> Defaults(ProjectTemplate template, DateTime today) =>
        template.Variables.ToDictionary(v => v.Id,
            v => v.Type == TemplateVariableType.Date && v.Default.Length == 0
                ? today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)
                : v.Default.Replace("${YEAR}", today.Year.ToString(CultureInfo.InvariantCulture))
                           .Replace("${DATE}", today.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)),
            StringComparer.Ordinal);

    /// <summary>Why each value cannot be used (empty when all are fine).</summary>
    public static IReadOnlyList<string> Validate(ProjectTemplate template, IReadOnlyDictionary<string, string> values)
    {
        var errors = new List<string>();
        foreach (var v in template.Variables)
        {
            var value = values.GetValueOrDefault(v.Id, "").Trim();
            if (value.Length == 0)
            {
                if (v.Required) errors.Add($"{v.Prompt}: a value is needed");
                continue;
            }
            bool ok = ScaffoldValueRules.IsSingleLine(value) && !value.Contains('"') && v.Type switch
            {
                TemplateVariableType.Name => ScaffoldValueRules.IsSurveyName(value),
                TemplateVariableType.Number => ScaffoldValueRules.IsNumber(value),
                TemplateVariableType.Bool => value is "true" or "false",
                TemplateVariableType.Choice => v.Choices?.Contains(value) ?? true,
                TemplateVariableType.Cs => ScaffoldValueRules.IsCoordinateSystem(value),
                TemplateVariableType.Date => DateRx().IsMatch(value),
                _ => true,
            };
            if (!ok) errors.Add($"{v.Prompt}: '{value}' cannot be written into the project");
        }
        return errors;
    }

    /// <summary>
    /// The plan for a template with these values, or the reasons there is none. Paths are checked
    /// after substitution, so a value cannot lead outside the project.
    /// </summary>
    public static (ScaffoldPlan? Plan, IReadOnlyList<string> Errors) Plan(ProjectTemplate template, IReadOnlyDictionary<string, string> values, DateTime now)
    {
        var errors = Validate(template, values).ToList();
        if (errors.Count > 0) return (null, errors);

        string Sub(string text) => VariableRx().Replace(text, m => m.Groups[1].Value switch
        {
            var id when values.TryGetValue(id, out var v) => v.Trim(),
            "DATE" => now.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
            "YEAR" => now.Year.ToString(CultureInfo.InvariantCulture),
            _ => m.Value,
        });

        string? SafePath(string raw)
        {
            var path = Sub(raw).Replace('\\', '/');
            var dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            var name = Path.GetFileName(path);
            bool ok = name.Length > 0 && name is not "." and not ".." && name.IndexOfAny(['"', ':']) < 0
                      && (dir.Length == 0 || ScaffoldValueRules.IsProjectRelativeDir(dir, allowDot: false));
            if (!ok) errors.Add($"'{path}' is not a path inside the project");
            return ok ? path : null;
        }

        var files = new List<ScaffoldFile>();
        var copies = new List<ScaffoldSourceCopy>();
        var dirs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in template.Directories)
        {
            var dir = Sub(d).Replace('\\', '/').TrimEnd('/');
            if (ScaffoldValueRules.IsProjectRelativeDir(dir, allowDot: false)) dirs.Add(dir);
            else errors.Add($"'{dir}' is not a folder inside the project");
        }
        foreach (var f in template.Files)
        {
            if (SafePath(f.RelativePath) is not { } path) continue;
            if (Path.GetDirectoryName(path)?.Replace('\\', '/') is { Length: > 0 } parent) AddParents(dirs, parent);
            if (f.Text is not null) files.Add(new ScaffoldFile(path, Sub(f.Text)));
            else copies.Add(new ScaffoldSourceCopy(path, f.SourcePath!));
        }
        var open = template.OpenAfterCreate.Length > 0 ? SafePath(template.OpenAfterCreate) ?? "" : "";
        if (errors.Count > 0) return (null, errors);
        foreach (var d in dirs.ToList()) AddParents(dirs, d);

        return (new ScaffoldPlan(dirs.ToList(), files, "", open) { SourceCopies = copies }, []);
    }

    private static void AddParents(SortedSet<string> dirs, string dir)
    {
        for (var d = dir; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d)?.Replace('\\', '/'))
            dirs.Add(d);
    }

    /// <summary>
    /// Writes a plan under <paramref name="targetRoot"/>. Refuses, writing nothing, when any file it
    /// would write already exists. Binary files are copied from <paramref name="templateFolder"/>.
    /// </summary>
    public static string? Write(ScaffoldPlan plan, string? templateFolder, string targetRoot)
    {
        var targets = plan.Files.Select(f => f.RelativePath).Concat(plan.SourceCopies.Select(c => c.RelativePath));
        if (targets.FirstOrDefault(t => File.Exists(Path.Combine(targetRoot, t))) is { } taken)
            return $"{taken} already exists";
        if (plan.SourceCopies.Count > 0 && templateFolder is null) return "the template has no folder to copy files from";

        foreach (var dir in plan.Directories) Directory.CreateDirectory(Path.Combine(targetRoot, dir));
        foreach (var f in plan.Files)
        {
            var path = Path.Combine(targetRoot, f.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, f.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        foreach (var c in plan.SourceCopies)
            File.Copy(Path.Combine(templateFolder!, c.SourceFileName), Path.Combine(targetRoot, c.RelativePath));
        return null;
    }

    // ---- built-ins -----------------------------------------------------------------------------

    private static readonly TemplateVariable CsVar = new("cs", "Coordinate system", "UTM34N", TemplateVariableType.Cs);

    public static IReadOnlyList<ProjectTemplate> BuiltIns { get; } =
    [
        new("single-cave", "Single cave", "One cave: a survey file to input trips into, a thconfig with a plan map and a 3D model, folders for data, sketches and output.",
            [new("project", "Cave name (no spaces)", "cave", TemplateVariableType.Name), new("title", "Title", "My cave"), CsVar],
            [
                new("${project}.thconfig", "encoding utf-8\nsource ${project}.th\n\nlayout plan\n  scale 1 500\n  north grid\nendlayout\n\nselect ${project}\nexport map -projection plan -layout plan -output output/${project}-plan.pdf\nexport model -output output/${project}.lox\n"),
                new("${project}.th", "encoding utf-8\nsurvey ${project} -title \"${title}\"\n  # Trips: input data/<trip>.th (File > New File, or Tools > Link survey file)\n  # Georeference: centreline / cs ${cs} / fix <entrance> <x> <y> <z> / endcentreline\nendsurvey\n"),
                new("README.md", "# ${title}\n\nCreated ${DATE} from the ThIDE single-cave template.\n\n- `data/` survey trips, `sketches/` th2 drawings, `output/` what Therion writes.\n"),
            ],
            ["data", "sketches", "output"], "${project}.thconfig", TitleKey: "ProjTpl_SingleCave"),
        new("karst-area", "Karst area", "Several caves of one area: each cave with its own thconfig, plus a master thconfig that compiles them together.",
            [new("area", "Area name (no spaces)", "area", TemplateVariableType.Name), new("cave", "First cave (no spaces)", "cave1", TemplateVariableType.Name), CsVar],
            [
                new("${area}.thconfig", "encoding utf-8\nsource ${area}.th\n\nselect ${area}\nexport map -projection plan -output output/${area}-plan.pdf\nexport model -output output/${area}.lox\n"),
                new("${area}.th", "encoding utf-8\nsurvey ${area}\n  input caves/${cave}/${cave}.th\n  # input caves/<next cave>/<next cave>.th\nendsurvey\n"),
                new("caves/${cave}/${cave}.th", "encoding utf-8\nsurvey ${cave}\n  # Trips of this cave: input <trip>.th\nendsurvey\n"),
                new("caves/${cave}/${cave}.thconfig", "encoding utf-8\nsource ${cave}.th\n\nselect ${cave}\nexport map -projection plan -output ../../output/${cave}-plan.pdf\n"),
            ],
            ["output"], "${area}.thconfig", TitleKey: "ProjTpl_KarstArea"),
        new("expedition", "Expedition", "A yearly expedition: one folder per year for its trips and sketches, and a thconfig per year.",
            [new("expedition", "Expedition name (no spaces)", "expedition", TemplateVariableType.Name), new("year", "Year", "${YEAR}", TemplateVariableType.Number), CsVar],
            [
                new("${expedition}-${year}.thconfig", "encoding utf-8\nsource ${expedition}.th\n\nselect ${expedition}\nexport map -projection plan -output output/${expedition}-${year}-plan.pdf\nexport model -output output/${expedition}-${year}.lox\n"),
                new("${expedition}.th", "encoding utf-8\nsurvey ${expedition}\n  input ${year}/trips.th\nendsurvey\n"),
                new("${year}/trips.th", "encoding utf-8\n# Trips of ${year}: input one file per trip (File > New File > Survey trip).\n"),
            ],
            ["${year}/sketches", "output"], "${expedition}-${year}.thconfig", TitleKey: "ProjTpl_Expedition"),
    ];
}
