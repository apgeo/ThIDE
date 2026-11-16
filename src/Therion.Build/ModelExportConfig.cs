// A thconfig that exports only a project's 3-D model, for a run outside the project folder: the
// project's own config with its paths made absolute, its exports and shell commands removed, and
// one `export model -fmt loch` added. Therion resolves `source` and `input` against the config that
// names them and writes outputs next to it, so the result can live in a temporary folder and leave
// the project untouched.

using System.Text;

namespace Therion.Build;

/// <summary>Builds a model-export thconfig from a project's thconfig.</summary>
public static class ModelExportConfig
{
    /// <summary>The model file the generated config writes, next to itself.</summary>
    public const string ModelFileName = "model.lox";

    private const int MaxInputDepth = 16;

    /// <summary>
    /// The text of a thconfig that, run from any folder, exports the model of the project whose
    /// config is <paramref name="configPath"/> to <paramref name="outputFileName"/> in that folder.
    /// <list type="bullet">
    /// <item><c>source</c>, <c>input</c> and <c>load</c> paths become absolute (relative to the file
    /// that names them, as Therion reads them).</item>
    /// <item>A config pulled in by a top-level <c>input</c> is inlined, rewritten the same way;
    /// inside an inline <c>source … endsource</c> block an <c>input</c> only gets an absolute path.</item>
    /// <item><c>export</c> and <c>system</c> commands and <c>layout</c>/<c>lookup</c> blocks are left
    /// out: the run makes no maps, and runs no shell command the project names.</item>
    /// <item>Everything else (<c>select</c>, <c>cs</c>, <c>encoding</c>, comments …) is kept.</item>
    /// </list>
    /// Texts are handled as Latin-1, so every byte of the original survives; a path the method
    /// adds is written in UTF-8 bytes, as file names are on Linux and macOS.
    /// </summary>
    /// <param name="readText">Reads a config file as Latin-1 text; null when it cannot be read
    /// (an unreadable <c>input</c> is then kept as an absolute <c>input</c>, for Therion to report).</param>
    public static string Create(string configPath, Func<string, string?> readText, string outputFileName = ModelFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(configPath);
        ArgumentNullException.ThrowIfNull(readText);
        ArgumentException.ThrowIfNullOrEmpty(outputFileName);

        string full = Path.GetFullPath(configPath);
        string text = readText(full) ?? throw new FileNotFoundException("The project's thconfig could not be read.", full);

        var output = new StringBuilder();
        var open = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase) { full };
        Rewrite(text, Path.GetDirectoryName(full)!, readText, output, open, depth: 0);
        if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
        output.Append("export model -fmt loch -o ").Append(Quote(outputFileName)).Append('\n');
        return output.ToString();
    }

    private static void Rewrite(
        string text, string directory, Func<string, string?> readText,
        StringBuilder output, HashSet<string> open, int depth)
    {
        string? skipUntil = null; // the end keyword of a block being left out
        bool inlineSource = false;

        foreach (var (raw, logical) in LogicalLines(text))
        {
            var (keyword, rest) = SplitKeyword(logical);

            if (skipUntil is not null)
            {
                if (keyword == skipUntil) skipUntil = null;
                continue;
            }

            if (inlineSource)
            {
                if (keyword == "endsource") inlineSource = false;
                if (keyword == "input" && Absolute(rest, directory) is { } inner)
                    output.Append("input ").Append(inner.Line).Append('\n');
                else
                    output.Append(raw);
                continue;
            }

            switch (keyword)
            {
                case "export":
                case "system":
                    continue;
                case "layout":
                    skipUntil = "endlayout";
                    continue;
                case "lookup":
                    skipUntil = "endlookup";
                    continue;
                case "source":
                    if (Absolute(rest, directory) is { } source)
                        output.Append("source ").Append(source.Line).Append('\n');
                    else
                    {
                        inlineSource = true;
                        output.Append(raw);
                    }
                    continue;
                case "load":
                    if (Absolute(rest, directory) is { } load)
                        output.Append("load ").Append(load.Line).Append('\n');
                    else
                        output.Append(raw);
                    continue;
                case "input":
                    if (Absolute(rest, directory) is not { } input)
                    {
                        output.Append(raw);
                        continue;
                    }
                    string? included = depth < MaxInputDepth && open.Add(input.FullPath) ? readText(input.FullPath) : null;
                    if (included is null)
                    {
                        output.Append("input ").Append(input.Line).Append('\n');
                        continue;
                    }
                    Rewrite(included, Path.GetDirectoryName(input.FullPath)!, readText, output, open, depth + 1);
                    if (output.Length > 0 && output[^1] != '\n') output.Append('\n');
                    open.Remove(input.FullPath);
                    continue;
                default:
                    output.Append(raw);
                    continue;
            }
        }
    }

    /// <summary>The command's first argument as an absolute path, with the rest of the line kept;
    /// null when there is no argument.</summary>
    private static (string FullPath, string Line)? Absolute(string rest, string directory)
    {
        if (ReadArgument(rest) is not var (argument, after)) return null;
        string relative = FromText(argument);
        string full = Path.GetFullPath(Path.Combine(directory, relative));
        return (full, Quote(ToText(full)) + after.TrimEnd('\r', '\n'));
    }

    /// <summary>One argument: a "quoted" string (a doubled quote stands for one) or a run of
    /// non-blank characters; a comment is not an argument.</summary>
    private static (string Argument, string After)? ReadArgument(string rest)
    {
        int i = 0;
        while (i < rest.Length && rest[i] is ' ' or '\t') i++;
        if (i >= rest.Length || rest[i] is '#' or '\r' or '\n') return null;
        if (rest[i] == '"')
        {
            var value = new StringBuilder();
            int j = i + 1;
            while (j < rest.Length)
            {
                if (rest[j] == '"')
                {
                    if (j + 1 < rest.Length && rest[j + 1] == '"') { value.Append('"'); j += 2; continue; }
                    return (value.ToString(), rest[(j + 1)..]);
                }
                value.Append(rest[j++]);
            }
            return (value.ToString(), "");
        }
        int end = i;
        while (end < rest.Length && rest[end] is not (' ' or '\t' or '\r' or '\n')) end++;
        return (rest[i..end], rest[end..]);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static (string Keyword, string Remainder) SplitKeyword(string logical)
    {
        int i = 0;
        while (i < logical.Length && logical[i] is ' ' or '\t') i++;
        int end = i;
        while (end < logical.Length && logical[end] is not (' ' or '\t' or '\r' or '\n')) end++;
        return (logical[i..end].ToLowerInvariant(), logical[end..]);
    }

    /// <summary>The text's lines, joining a line that ends in a backslash with the next: the
    /// physical lines as written (for keeping) and the joined command (for reading).</summary>
    private static IEnumerable<(string Raw, string Logical)> LogicalLines(string text)
    {
        var raw = new StringBuilder();
        var logical = new StringBuilder();
        int start = 0;
        while (start < text.Length)
        {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline + 1;
            string line = text[start..end];
            start = end;

            raw.Append(line);
            string content = line.TrimEnd('\r', '\n');
            if (content.EndsWith('\\'))
            {
                logical.Append(content, 0, content.Length - 1).Append(' ');
                continue;
            }
            logical.Append(content);
            if (!line.EndsWith('\n')) raw.Append('\n');
            yield return (raw.ToString(), logical.ToString());
            raw.Clear();
            logical.Clear();
        }
        if (raw.Length > 0)
            yield return (raw.Append('\n').ToString(), logical.ToString());
    }

    /// <summary>A path written in the config (Latin-1 text of UTF-8 bytes) as a file name.</summary>
    private static string FromText(string value) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));

    /// <summary>A file name as config text: its UTF-8 bytes, one Latin-1 character each.</summary>
    private static string ToText(string path) => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(path));
}
