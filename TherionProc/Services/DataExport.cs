// DATA-09 — export any tabular data view to CSV or a Markdown table (string builders only;
// clipboard/file writing is the caller's concern). Pure + unit-testable.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Input.Platform;

namespace TherionProc.Services;

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

    private static string CsvField(string? value)
    {
        var v = value ?? string.Empty;
        if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static string MdCell(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\").Replace("|", "\\|")
            .Replace("\r", string.Empty).Replace("\n", "<br>");
}

/// <summary>Sets text on the desktop clipboard via the main window (no-op in headless tests).</summary>
public static class ClipboardHelper
{
    public static void SetText(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            _ = life.MainWindow?.Clipboard?.SetTextAsync(text);
    }
}
