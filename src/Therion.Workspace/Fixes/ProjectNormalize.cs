// Tools ▸ Normalise project: Format all files plus the house-keeping a shared repository wants —
// one line-ending style, UTF-8 instead of a legacy declared encoding, and runs of `input` lines in
// natural order. Every step is text in, text out, so it previews and undoes like any batch edit.

using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Workspace.Editing;
using Therion.Workspace.Formatting;

namespace Therion.Workspace.Fixes;

/// <param name="IndentUnit">Re-indent with this unit; null = leave indentation alone.</param>
/// <param name="ToUtf8">Rewrite an <c>encoding</c> line that names anything but UTF-8 to <c>encoding utf-8</c>
/// (the file is then saved as UTF-8).</param>
/// <param name="SortInputs">Sort each run of consecutive <c>input</c> lines naturally (<c>trip2</c> before <c>trip10</c>).</param>
public sealed record NormalizeOptions(string? IndentUnit, LineEndings LineEndings, bool ToUtf8, bool SortInputs)
{
    public bool IsEmpty => IndentUnit is null && LineEndings == LineEndings.Keep && !ToUtf8 && !SortInputs;
}

public static partial class ProjectNormalize
{
    /// <summary>The text after the chosen steps (line endings excepted: <see cref="FormatAll.Plan"/> applies those).</summary>
    public static string Apply(string text, NormalizeOptions options)
    {
        if (options.IndentUnit is { } unit) text = TherionReindenter.Format(text, unit);
        if (options.ToUtf8) text = EncodingToUtf8(text);
        if (options.SortInputs) text = SortInputRuns(text);
        return text;
    }

    /// <summary>
    /// One whole-file edit per file the steps change; files that do not parse cleanly are skipped and
    /// listed (see <see cref="FormatAll.Plan"/>).
    /// </summary>
    public static (EditBatch Batch, IReadOnlyList<EditPreviewRow> Rows, IReadOnlyList<string> Skipped) Plan(
        IEnumerable<string> files, Func<string, string?> readText, NormalizeOptions options, Func<int, string> describe)
    {
        var plan = FormatAll.Plan(files, readText, options.LineEndings, t => Apply(t, options), describe);
        // Sorting inputs changes what the project includes in which order; the model must be rebuilt.
        return options.SortInputs || options.ToUtf8 ? (plan.Batch with { RequiresWorkspaceReload = true }, plan.Rows, plan.Skipped) : plan;
    }

    /// <summary>
    /// <c>encoding utf-8</c> in place of an <c>encoding</c> line naming another encoding. Only the first
    /// directive counts (it is the one the reader honours), and only near the top of the file.
    /// </summary>
    public static string EncodingToUtf8(string text)
    {
        var m = EncodingLine().Match(text);
        if (!m.Success || m.Index > 4096) return text;
        var name = m.Groups["name"].Value;
        if (name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase)) return text;
        return text[..m.Groups["name"].Index] + "utf-8" + text[(m.Groups["name"].Index + name.Length)..];
    }

    /// <summary>
    /// Each run of consecutive <c>input</c> lines (same indentation, nothing between them) sorted by
    /// path in natural order. A run with a comment or a blank line in it is two runs.
    /// </summary>
    public static string SortInputRuns(string text)
    {
        var lines = Regex.Split(text, @"(?<=\n)");
        var result = new List<string>(lines.Length);
        int i = 0;
        while (i < lines.Length)
        {
            if (InputPath(lines[i]) is null) { result.Add(lines[i++]); continue; }
            int j = i;
            var indent = Indent(lines[i]);
            while (j < lines.Length && InputPath(lines[j]) is not null && Indent(lines[j]) == indent) j++;
            var run = lines[i..j];
            // The last line of the file may have no terminator; keep the terminators where they were.
            var endings = run.Select(Ending).ToList();
            var sorted = run.Select(l => l[..^Ending(l).Length]).OrderBy(l => InputPath(l), NaturalStringComparer.Instance).ToList();
            for (int k = 0; k < sorted.Count; k++) result.Add(sorted[k] + endings[k]);
            i = j;
        }
        return string.Concat(result);
    }

    private static string? InputPath(string line)
    {
        var m = InputLine().Match(line);
        return m.Success ? m.Groups["path"].Value.Trim('"') : null;
    }

    private static string Indent(string line) => line[..(line.Length - line.TrimStart(' ', '\t').Length)];

    private static string Ending(string line) => line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith('\n') ? "\n" : "";

    [GeneratedRegex(@"^[ \t]*encoding[ \t]+(?<name>[A-Za-z0-9_-]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodingLine();

    // A plain `input <path>` line: no trailing comment, so moving it moves nothing else.
    [GeneratedRegex(@"^[ \t]*input[ \t]+(?<path>""[^""]+""|\S+)[ \t]*\r?\n?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InputLine();
}
