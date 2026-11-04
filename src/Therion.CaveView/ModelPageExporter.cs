// Writes a stand-alone web page for a cave model: index.html, the page's script and stylesheet,
// the CaveView.js runtime under a version-named directory, and the model. The result is a plain
// folder any static web server can serve - no server code, no build step, nothing fetched from
// elsewhere.
//
//   index.html                      the page; its settings are a JSON block inside it
//   assets/                         model-page.js, model-page.css, station-mark.js
//   caveview/<version>/             the viewer runtime (its home directory)
//   model/<file>                    the model - or model/model.js when it is embedded
//   README.txt                      how to publish and link to the page

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Therion.CaveView;

/// <summary>One file of a model page: its path in the page's folder and how to read it.</summary>
public sealed record ModelPageFile(string Path, Func<Stream> Open);

public static class ModelPageExporter
{
    /// <summary>The model formats CaveView reads.</summary>
    public static IReadOnlyList<string> ModelExtensions { get; } = new[] { ".lox", ".3d", ".plt" };

    /// <summary>The runtime's directory in the page: named by version, so a page updated on a
    /// server is never run with a cached older viewer.</summary>
    public static string RuntimeDirectory => $"caveview/{CaveViewRuntime.Version}/";

    /// <summary>
    /// The files of the page, without writing anything. Throws <see cref="ArgumentException"/> for
    /// a model CaveView cannot read, an empty title or a language the runtime has no catalogue for.
    /// </summary>
    public static IReadOnlyList<ModelPageFile> Plan(ModelPageModel model, ModelPageOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        Validate(model, options);

        var modelName = SafeFileName(model.FileName);
        var files = new List<ModelPageFile>();

        var html = RenderIndex(options, modelName);
        files.Add(Text("index.html", html));
        files.Add(Text("README.txt", RenderReadme(options, modelName)));

        foreach (var page in CaveViewRuntime.PageFiles)
        {
            if (page == "index.html") continue;
            var p = page;
            files.Add(new ModelPageFile(p, () => CaveViewRuntime.OpenPageFile(p)!));
        }
        foreach (var rt in CaveViewRuntime.Files)
        {
            var r = rt;
            files.Add(new ModelPageFile(RuntimeDirectory + r, () => CaveViewRuntime.Open(r)!));
        }

        if (options.EmbedModel)
            files.Add(new ModelPageFile("model/model.js", () => EmbeddedModelScript(model, modelName)));
        else
            files.Add(new ModelPageFile("model/" + modelName, model.Open));

        return files;
    }

    /// <summary>Writes the page into <paramref name="directory"/> (created if missing; files of the
    /// same names are replaced, anything else there is left alone). Returns the path of index.html.</summary>
    public static string WriteToDirectory(ModelPageModel model, ModelPageOptions options, string directory)
    {
        var files = Plan(model, options);
        var root = Path.GetFullPath(directory);
        foreach (var f in files)
        {
            var dest = Path.Combine(root, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var src = f.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
        return Path.Combine(root, "index.html");
    }

    /// <summary>Writes the page as a zip archive of the same folder into <paramref name="output"/>.</summary>
    public static void WriteZip(ModelPageModel model, ModelPageOptions options, Stream output)
    {
        var files = Plan(model, options);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var f in files)
        {
            var entry = zip.CreateEntry(f.Path, CompressionLevel.Optimal);
            using var src = f.Open();
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
    }

    // ---- rendering (internal for tests) ---------------------------------------------------

    internal static void Validate(ModelPageModel model, ModelPageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Title))
            throw new ArgumentException("A model page needs a title.", nameof(options));
        var ext = Path.GetExtension(model.FileName).ToLowerInvariant();
        if (!ModelExtensions.Contains(ext))
            throw new ArgumentException($"CaveView cannot read '{model.FileName}': a model is a .lox, .3d or .plt file.", nameof(model));
        if (!CaveViewRuntime.Languages.Contains(options.Language))
            throw new ArgumentException($"No viewer catalogue for language '{options.Language}'.", nameof(options));
        if (!(options.CloseUpSpan > 0 && double.IsFinite(options.CloseUpSpan)))
            throw new ArgumentException("The close-up span must be a number of metres above 0.", nameof(options));
    }

    internal static string RenderIndex(ModelPageOptions options, string modelName)
    {
        var template = CaveViewRuntime.ReadPageText("index.html")
            ?? throw new InvalidOperationException("The model page template is missing from Therion.CaveView.");

        var description = string.IsNullOrWhiteSpace(options.Description)
            ? string.Empty
            : "<p>" + WebUtility.HtmlEncode(options.Description.Trim()) + "</p>";

        var footer = new StringBuilder();
        footer.Append(WebUtility.HtmlEncode(modelName));
        if (!string.IsNullOrWhiteSpace(options.Generator))
            footer.Append(" · ").Append(WebUtility.HtmlEncode(options.Generator));
        footer.Append(" · 3D: <a href=\"https://github.com/apgeo/CaveView.js\">CaveView.js</a> (MIT)");

        var modelScript = options.EmbedModel ? "<script src=\"model/model.js\"></script>\n" : string.Empty;

        return template
            .Replace("{{LANG}}", options.Language, StringComparison.Ordinal)
            .Replace("{{GENERATOR}}", WebUtility.HtmlEncode(options.Generator ?? "Therion.CaveView"), StringComparison.Ordinal)
            .Replace("{{TITLE}}", WebUtility.HtmlEncode(options.Title.Trim()), StringComparison.Ordinal)
            .Replace("{{HOME}}", RuntimeDirectory, StringComparison.Ordinal)
            .Replace("{{DESCRIPTION}}", description, StringComparison.Ordinal)
            .Replace("{{FOOTER}}", footer.ToString(), StringComparison.Ordinal)
            .Replace("{{MODEL_SCRIPT}}", modelScript, StringComparison.Ordinal)
            .Replace("{{CONFIG}}", RenderConfig(options, modelName), StringComparison.Ordinal);
    }

    // The JSON block model-page.js reads. System.Text.Json's default encoder writes '<', '>' and
    // '&' as \u escapes, so no text of the reader's can close the <script> element it sits in.
    internal static string RenderConfig(ModelPageOptions options, string modelName)
    {
        var v = options.View;
        var config = new
        {
            language = options.Language,
            home = RuntimeDirectory,
            surveyDirectory = "model/",
            model = modelName,
            toolbar = options.Toolbar,
            closeUp = options.CloseUp,
            closeUpSpan = options.CloseUpSpan,
            view = new
            {
                shading = v.Shading,
                walls = v.Walls,
                splays = v.Splays,
                stations = v.Stations,
                stationLabels = v.StationLabels,
                entrances = v.Entrances,
                box = v.BoundingBox,
                orientation = v.Orientation,
                orthographic = v.Orthographic,
                autoRotate = v.AutoRotate,
            },
            places = options.Places
                .Where(p => !string.IsNullOrWhiteSpace(p.Station))
                .Select(p => new
                {
                    label = string.IsNullOrWhiteSpace(p.Label) ? p.Station : p.Label,
                    station = p.Station,
                    // an array reference is the one CaveView matches name by name
                    @ref = p.StationParts is { Count: > 0 } parts && parts.Any(n => n.Contains('.')) ? parts : null,
                    note = p.Note,
                }),
        };
        return JsonSerializer.Serialize(config);
    }

    private static string RenderReadme(ModelPageOptions options, string modelName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(options.Title.Trim());
        sb.AppendLine(new string('=', Math.Min(72, Math.Max(3, options.Title.Trim().Length))));
        sb.AppendLine();
        sb.AppendLine("A web page showing the 3D model " + modelName + ", drawn by CaveView.js.");
        sb.AppendLine();
        sb.AppendLine("Publishing: copy this whole folder to any web server (or a static host such as");
        sb.AppendLine("GitHub Pages) and open index.html through it. Nothing else is needed.");
        sb.AppendLine();
        if (options.EmbedModel)
        {
            sb.AppendLine("The model is carried in model/model.js, so index.html also opens straight from");
            sb.AppendLine("a disk. (Viewer controls in a language other than English still need a server.)");
        }
        else
        {
            sb.AppendLine("Opened straight from a disk, a browser refuses to read the model file next to the");
            sb.AppendLine("page. To look at it locally, serve the folder, e.g.:  python3 -m http.server");
            sb.AppendLine("and open http://localhost:8000/ .");
        }
        sb.AppendLine();
        sb.AppendLine("Linking to a place: index.html#station=<full station name> opens the page flown");
        sb.AppendLine("to that station and marked. Other links of a page holding the model can do the");
        sb.AppendLine("same with <a href=\"#\" data-station=\"<full station name>\">...</a>.");
        sb.AppendLine();
        sb.AppendLine("The viewer is CaveView.js " + CaveViewRuntime.Version + " (MIT licence, " + RuntimeDirectory + "LICENSE),");
        sb.AppendLine("from https://github.com/apgeo/CaveView.js, a fork of https://github.com/aardgoose/CaveView.js.");
        return sb.ToString();
    }

    private static Stream EmbeddedModelScript(ModelPageModel model, string modelName)
    {
        using var src = model.Open();
        using var buffer = new MemoryStream();
        src.CopyTo(buffer);
        var script = "window.CAVE_PAGE_MODEL = { name: " + JsonSerializer.Serialize(modelName)
            + ", base64: \"" + Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length) + "\" };\n";
        return new MemoryStream(Encoding.UTF8.GetBytes(script));
    }

    /// <summary>A model file name safe in a URL and on any file system; the extension is kept (lower-cased).</summary>
    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        name = Regex.Replace(name.Normalize(NormalizationForm.FormD), @"\p{Mn}", string.Empty);
        name = Regex.Replace(name, @"[^A-Za-z0-9._-]+", "-").Trim('-', '.');
        if (name.Length == 0) name = "model";
        return name + ext;
    }

    private static ModelPageFile Text(string path, string content) =>
        new(path, () => new MemoryStream(new UTF8Encoding(false).GetBytes(content)));
}
