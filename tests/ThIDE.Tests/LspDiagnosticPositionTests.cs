using System.Text;
using System.Text.Json;
using Therion.Lsp;

namespace ThIDE.Tests;

// Where the LSP server puts its squiggles. A client reads Range.Character as a UTF-16 code-unit index
// into the line, so a tab (one unit), a combining mark (one unit) and an astral character (two units)
// before the token must move it by exactly that much. Diagnostics are deterministic, so the cases are
// a fixed table of prefixes crossed with the line endings and the file kinds.
public class LspDiagnosticPositionTests
{
    // Text placed before the flagged token on its line: indentation (a tab is one UTF-16 unit, like a
    // space), and names that carry accents as a base letter plus combining marks, characters outside
    // the BMP (a musical symbol, an emoji, a flag of two regional indicators, a ZWJ family),
    // right-to-left and CJK text.
    public static readonly string[] Indents = ["", "\t", "\t\t", "  \t "];

    public static readonly string[] Names =
    [
        "a", "e\u0301", "e\u0301\u0302\u0303x", "\U0001D11E", "\U0001F600", "\U0001F1F7\U0001F1F4",
        "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\u05E9\u05DC\u05D5\u05DD", "\u65E5\u672C\u8A9E", "\U0001D11Ea\u0301",
    ];

    public static readonly string[] LineEndings = ["\n", "\r\n", "\r"];

    public static TheoryData<string, string, string> IndentNameEnding()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var i in Indents)
            foreach (var n in Names)
                foreach (var e in LineEndings)
                    data.Add(i, n, e);
        return data;
    }

    // The UTF-16 index of every blank-separated word of the line, and the word's length in code units.
    private static List<(string Word, int Start)> Words(string line)
    {
        var words = new List<(string, int)>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && line[i] is ' ' or '\t') i++;
            int start = i;
            while (i < line.Length && line[i] is not (' ' or '\t')) i++;
            if (i > start) words.Add((line[start..i], start));
        }
        return words;
    }

    private static string[] LinesOf(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    [Theory]
    [MemberData(nameof(IndentNameEnding))]
    public void An_unresolved_equate_reference_starts_at_its_utf16_index(string indent, string name, string eol)
    {
        // Three references, none of which exists, each flagged on its own; each one follows the others.
        var line = indent + "equate " + name + " zzz " + name + "b";
        var text = "survey s" + eol + line + eol + "endsurvey" + eol;

        var diags = DiagnosticProvider.Compute("cave.th", text).Where(d => d.Code == "TH_SEM_001").ToList();

        var expected = Words(line).Skip(1).ToList();
        Assert.Equal(3, expected.Count);
        Assert.Equal(expected.Count, diags.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            var d = diags[i];
            Assert.Equal(1, d.Range.Start.Line);
            Assert.Equal(1, d.Range.End.Line);
            Assert.Equal(expected[i].Start, d.Range.Start.Character);
            Assert.Equal(expected[i].Start + expected[i].Word.Length, d.Range.End.Character);
            Assert.Equal(expected[i].Word, LinesOf(text)[1][d.Range.Start.Character..d.Range.End.Character]);
        }
    }

    [Theory]
    [MemberData(nameof(IndentNameEnding))]
    public void A_bad_number_in_a_shot_row_starts_at_its_utf16_index(string indent, string name, string eol)
    {
        // Station names are free text; the bad length comes after two of them.
        var row = indent + name + " " + name + "2 qq 0 0";
        var text = string.Join(eol, "survey s", "  centreline", "    data normal from to length compass clino", row, "  endcentreline", "endsurvey") + eol;

        var d = Assert.Single(DiagnosticProvider.Compute("cave.th", text), x => x.Code == "TH_SEM_006");

        var line = LinesOf(text)[3];
        int at = line.IndexOf(" qq ", StringComparison.Ordinal) + 1;
        Assert.Equal(3, d.Range.Start.Line);
        Assert.Equal(at, d.Range.Start.Character);
        Assert.Equal(at + 2, d.Range.End.Character);
        Assert.Equal("qq", line[d.Range.Start.Character..d.Range.End.Character]);
    }

    [Theory]
    [MemberData(nameof(IndentNameEnding))]
    public void An_unknown_command_starts_at_its_utf16_index(string indent, string name, string eol)
    {
        // Inside a survey the command word itself is the token; the indentation comes first.
        var word = name + "foo\U0001D11Ebar";
        var text = "survey s" + eol + indent + word + " x" + eol + "endsurvey" + eol;

        var d = Assert.Single(DiagnosticProvider.Compute("cave.th", text), x => x.Code == "TH0010");

        Assert.Equal(1, d.Range.Start.Line);
        Assert.Equal(indent.Length, d.Range.Start.Character);
        Assert.Equal(indent.Length + word.Length, d.Range.End.Character);
    }

    [Theory]
    [MemberData(nameof(IndentNameEnding))]
    public void An_unknown_thconfig_command_starts_at_its_utf16_index(string indent, string name, string eol)
    {
        var word = name + "foo\U0001F600";
        var text = "source a.th" + eol + indent + word + " bar" + eol;

        var d = Assert.Single(DiagnosticProvider.Compute("cave.thconfig", text), x => x.Code == "TH0010");

        Assert.Equal(1, d.Range.Start.Line);
        Assert.Equal(indent.Length, d.Range.Start.Character);
        Assert.Equal(indent.Length + word.Length, d.Range.End.Character);
    }

    [Theory]
    [InlineData("cave.th", "survey s\n\U0001F600 x\nendsurvey\n")]
    [InlineData("cave.th", "survey \U0001F600s\nendsurvey\n")]
    [InlineData("cave.th", "survey s\n  map \U0001D11E\n  endmap\nendsurvey\n")]
    [InlineData("cave.th2", "scrap \U0001F600x -projection plan\nendscrap\n")]
    [InlineData("cave.th2", "\tscrap \U0001D11Ex\n")]
    [InlineData("cave.thconfig", "source \U0001F600.th\n\U0001D11E\n")]
    public void Every_diagnostic_is_well_formed_text_that_the_json_rpc_loop_can_send(string path, string text)
    {
        // Program.cs serializes these with System.Text.Json, which throws on a lone UTF-16 surrogate:
        // a message that quotes half of an astral character would end the server.
        var diags = DiagnosticProvider.Compute(path, text);
        Assert.NotEmpty(diags);
        foreach (var d in diags)
        {
            var ex = Record.Exception(() => JsonSerializer.SerializeToUtf8Bytes(new { message = d.Message, code = d.Code }));
            Assert.True(ex is null, $"{d.Code}: '{d.Message}' cannot be serialized: {ex?.GetType().Name} {ex?.Message}");
            Assert.True(IsWellFormed(d.Message), $"{d.Code}: '{d.Message}' has a lone surrogate");
        }
    }

    [Fact]
    public void Positions_are_zero_based_and_the_range_is_never_empty_or_backwards()
    {
        // A diagnostic on a lone-CR file, on a file whose last line has no ending, and on an empty file.
        foreach (var text in new[] { "survey s\rendsurvey\rfoo\r", "survey s\nfoo", "", "\n\n\n", "\U0001F600" })
            foreach (var path in new[] { "a.th", "a.th2", "a.thconfig" })
                foreach (var d in DiagnosticProvider.Compute(path, text))
                {
                    Assert.True(d.Range.Start.Line >= 0 && d.Range.Start.Character >= 0, $"{path} {d.Code}");
                    Assert.True(d.Range.End.Line > d.Range.Start.Line
                                || (d.Range.End.Line == d.Range.Start.Line && d.Range.End.Character > d.Range.Start.Character),
                        $"{path} {d.Code}: {d.Range}");
                    var lines = LinesOf(text);
                    Assert.True(d.Range.Start.Line < lines.Length, $"{path} {d.Code}: line {d.Range.Start.Line} of {lines.Length}");
                    Assert.True(d.Range.Start.Character <= lines[d.Range.Start.Line].Length, $"{path} {d.Code}: character past the line end");
                }
    }

    private static bool IsWellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
            if (char.IsSurrogate(s[i])) return false;
        }
        return true;
    }
}
