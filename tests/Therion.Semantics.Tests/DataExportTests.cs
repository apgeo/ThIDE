// CSV / Markdown table export.
using System.Collections.Generic;
using Therion.Semantics;
using Xunit;

namespace Therion.Semantics.Tests;

public class DataExportTests
{
    private static readonly string[] Headers = { "Name", "Length" };
    private static IReadOnlyList<IReadOnlyList<string>> Rows =>
        new IReadOnlyList<string>[]
        {
            new[] { "cave", "1.2 km" },
            new[] { "with,comma", "3 m" },
            new[] { "with \"quote\"", "4 m" },
        };

    [Fact]
    public void Csv_quotes_fields_with_commas_and_quotes()
    {
        var csv = DataExport.ToCsv(Headers, Rows);
        Assert.Contains("Name,Length", csv);
        Assert.Contains("\"with,comma\",3 m", csv);
        Assert.Contains("\"with \"\"quote\"\"\",4 m", csv);
    }

    [Fact]
    public void Markdown_renders_header_separator_and_escapes_pipes()
    {
        var md = DataExport.ToMarkdown(new[] { "A", "B" },
            new IReadOnlyList<string>[] { new[] { "x|y", "z" } });
        Assert.Contains("| A | B |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("x\\|y", md);
    }


    [Fact]
    public void Html_escapes_cells_and_renders_table()
    {
        var html = DataExport.ToHtml(new[] { "A" },
            new IReadOnlyList<string>[] { new[] { "<x> & \"y\"" } });
        Assert.Contains("<table>", html);
        Assert.Contains("<th>A</th>", html);
        Assert.Contains("&lt;x&gt; &amp; &quot;y&quot;", html);
    }

    [Fact]
    public void Latex_escapes_specials_and_renders_tabular()
    {
        var tex = DataExport.ToLatex(new[] { "A", "B" },
            new IReadOnlyList<string>[] { new[] { "a_b", "100%" } });
        Assert.Contains("\\begin{tabular}{ll}", tex);
        Assert.Contains("A & B \\\\", tex);
        Assert.Contains("a\\_b", tex);
        Assert.Contains("100\\%", tex);
        Assert.Contains("\\end{tabular}", tex);
    }
}
