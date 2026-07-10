// Render a header + rows of strings as CSV, Markdown, HTML or LaTeX. String builders only;
// clipboard and file writing are the caller's concern. Pure + unit-testable.
//
// Lives beside SurveyTables, which projects a WorkspaceSemanticModel into headers+rows and whose own
// header comment used to say "the formatting lives in the app's DataExport". It doesn't any more:
// the MCP export tools need the same formatters with no UI loaded.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Therion.Semantics;

/// <summary>Renders a header + rows of strings as CSV or a GitHub-flavoured Markdown table.</summary>
public static class DataExport
{
    /// <summary>RFC-4180-ish CSV: quote fields containing a comma, quote, or newline.</summary>
    public static string ToCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvField)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(CsvField)));
        return sb.ToString();
    }

    /// <summary>A Markdown table (header, separator, rows). Pipes/newlines in cells are escaped.</summary>
    public static string ToMarkdown(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers.Select(MdCell))).AppendLine(" |");
        sb.Append('|').Append(string.Join("|", headers.Select(_ => " --- "))).AppendLine("|");
        foreach (var row in rows)
            sb.Append("| ").Append(string.Join(" | ", row.Select(MdCell))).AppendLine(" |");
        return sb.ToString();
    }

    /// <summary>An HTML <c>&lt;table&gt;</c> fragment (cells HTML-escaped). .</summary>
    public static string ToHtml(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<table>");
        sb.Append("  <thead><tr>");
        foreach (var h in headers) sb.Append("<th>").Append(HtmlCell(h)).Append("</th>");
        sb.AppendLine("</tr></thead>");
        sb.AppendLine("  <tbody>");
        foreach (var row in rows)
        {
            sb.Append("    <tr>");
            foreach (var c in row) sb.Append("<td>").Append(HtmlCell(c)).Append("</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("  </tbody>");
        sb.AppendLine("</table>");
        return sb.ToString();
    }

    /// <summary>A LaTeX <c>tabular</c> environment (cells escaped for LaTeX). .</summary>
    public static string ToLatex(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.Append("\\begin{tabular}{").Append(string.Concat(headers.Select(_ => "l"))).AppendLine("}");
        sb.AppendLine("\\hline");
        sb.Append(string.Join(" & ", headers.Select(LatexCell))).AppendLine(" \\\\");
        sb.AppendLine("\\hline");
        foreach (var row in rows)
            sb.Append(string.Join(" & ", row.Select(LatexCell))).AppendLine(" \\\\");
        sb.AppendLine("\\hline");
        sb.AppendLine("\\end{tabular}");
        return sb.ToString();
    }

    private static string CsvField(string? value)
    {
        var v = value ?? string.Empty;
        if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static string HtmlCell(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string LatexCell(string? value)
    {
        var v = value ?? string.Empty;
        var sb = new StringBuilder(v.Length);
        foreach (var c in v)
            sb.Append(c switch
            {
                '\\' => "\\textbackslash{}",
                '&' or '%' or '$' or '#' or '_' or '{' or '}' => "\\" + c,
                '~' => "\\textasciitilde{}",
                '^' => "\\textasciicircum{}",
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    private static string MdCell(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|")
            .Replace("\r", string.Empty).Replace("\n", "<br>");
}
