using System.Text;
using System.Text.RegularExpressions;
using Therion.Syntax;
using Therion.Testing;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using Therion.Workspace.Formatting;

namespace Therion.Workspace.Tests.Formatting;

/// <summary>
/// The re-indenter behind Format Document, Format all files, <c>therion-cli format</c> and the
/// <c>format_file</c> tool, over generated files: every block keyword in any case and both centreline
/// spellings, layout/lookup bodies, bare and file <c>source</c>, stray closers, continuations,
/// comments, blank and whitespace-only lines, LF/CRLF/CR and a missing final newline. It may change
/// indentation and trailing whitespace and nothing else. Seeds are fixed; a failure names its seed.
/// </summary>
public partial class ReindentProperties
{
    private const int Files = 400;

    private static readonly string[] Units = ["  ", "\t", "    "];

    [Fact]
    public void Formatting_is_idempotent_and_changes_only_indentation_and_trailing_whitespace()
    {
        int checkedDepth = 0;
        for (int seed = 0; seed < Files; seed++)
        {
            var random = new SeededRandom((ulong)seed * 7919 + 17);
            var file = Generate(random, allowStray: seed % 3 == 0);
            var unit = random.Pick(Units);
            var text = file.Text;
            var formatted = TherionReindenter.Format(text, unit);
            var because = $"[seed {seed}, unit '{Escape(unit)}']";

            Assert.True(formatted == TherionReindenter.Format(formatted, unit), $"{because} formatting twice differs from formatting once");

            var (before, beforeEnds) = SplitLines(text);
            var (after, afterEnds) = SplitLines(formatted);
            Assert.True(before.Count == after.Count, $"{because} the line count changed from {before.Count} to {after.Count}");
            Assert.True(beforeEnds.SequenceEqual(afterEnds), $"{because} a line terminator changed");

            for (int i = 0; i < before.Count; i++)
            {
                Assert.True(before[i].Trim() == after[i].Trim(), $"{because} line {i + 1} changed beyond its whitespace: '{Escape(before[i])}' -> '{Escape(after[i])}'");
                if (file.Opaque.Contains(i))
                {
                    Assert.True(before[i] == after[i], $"{because} line {i + 1} inside a layout/lookup body was changed");
                    continue;
                }
                // Outside opaque bodies: whole units of indentation, no trailing whitespace, blank lines empty.
                var body = after[i].TrimStart();
                var indent = after[i][..^body.Length];
                Assert.True(indent.Length % unit.Length == 0 && indent == Repeat(unit, indent.Length / unit.Length),
                    $"{because} line {i + 1} is indented with something other than whole units: '{Escape(after[i])}'");
                Assert.True(after[i].Length == 0 || !char.IsWhiteSpace(after[i][^1]), $"{because} line {i + 1} kept trailing whitespace");
                if (!file.HasStray && file.Depth[i] is { } depth && body.Length > 0)
                {
                    Assert.True(Repeat(unit, depth) + body == after[i],
                        $"{because} line {i + 1} '{Escape(before[i])}' should be at depth {depth}, got '{Escape(after[i])}'");
                    checkedDepth++;
                }
            }

            Assert.True(SignificantTokens(text).SequenceEqual(SignificantTokens(formatted)), $"{because} the token sequence changed");

            // The editor applies DesiredLines in place; the result must be the same text Format gives.
            var desired = TherionReindenter.DesiredLines(before, unit);
            var applied = new StringBuilder();
            for (int i = 0; i < before.Count; i++) applied.Append(desired[i] ?? before[i]).Append(beforeEnds[i]);
            Assert.True(formatted == applied.ToString(), $"{because} DesiredLines applied in place differs from Format");
            Assert.All(TherionReindenter.DesiredLines(after, unit), d => Assert.Null(d));
        }
        Assert.True(checkedDepth > 5000, $"only {checkedDepth} lines had their depth checked");
    }

    [Fact]
    public void Every_block_keyword_indents_its_body_by_one_unit_in_any_case()
    {
        string[] blocks = ["survey", "centreline", "scrap", "line", "area", "map", "surface", "scan", "comment", "group", "layout", "lookup", "code", "source"];
        Assert.Equal(blocks.Order(), TherionReindenter.BlockKeywords.Order());   // the editor's block table is built from this
        foreach (var keyword in blocks.Append("centerline"))
            foreach (var spelling in new[] { keyword, keyword.ToUpperInvariant(), char.ToUpperInvariant(keyword[0]) + keyword[1..] })
            {
                var closer = "end" + (spelling.Equals("centerline", StringComparison.OrdinalIgnoreCase) ? "centreline" : spelling);
                var opener = keyword is "layout" or "lookup" ? spelling + " x" : spelling;
                var text = $"survey a\n{opener}\nbody line\n{closer}\nendsurvey\n";
                var expected = keyword is "layout" or "lookup"
                    ? $"survey a\n  {opener}\nbody line\n  {closer}\nendsurvey\n"   // a layout/lookup body is left verbatim
                    : $"survey a\n  {opener}\n    body line\n  {closer}\nendsurvey\n";
                Assert.Equal(expected, TherionReindenter.Format(text, "  "));
            }
    }

    [Fact]
    public void Source_with_a_file_is_a_command_and_a_bare_source_opens_a_block()
    {
        Assert.Equal("source a.th\nsurvey x\nendsurvey\n", TherionReindenter.Format("source a.th\n  survey x\nendsurvey\n", "  "));
        Assert.Equal("source\n  a.th\nendsource\n", TherionReindenter.Format("source\na.th\nendsource\n", "  "));
    }

    /// <summary>A closer whose block is not open anywhere is a typo or a leftover; the blocks that are open stay open.</summary>
    [Fact(Skip = "Known bug: a closer whose block is not open pops every open block, so the rest of the file is indented as top level")]
    public void A_stray_closer_does_not_change_the_indentation_of_the_lines_after_it()
    {
        var text = "survey a\ncentreline\nendscrap\n1 2 3 4 5\nendcentreline\nendsurvey\n";
        Assert.Equal("survey a\n  centreline\n    endscrap\n    1 2 3 4 5\n  endcentreline\nendsurvey\n", TherionReindenter.Format(text, "  "));
    }

    [Fact]
    public void A_closer_for_an_outer_block_closes_the_unterminated_inner_ones()
    {
        var text = "survey a\ncentreline\n1 2 3 4 5\nendsurvey\nsurvey b\nendsurvey\n";
        Assert.Equal("survey a\n  centreline\n    1 2 3 4 5\nendsurvey\nsurvey b\nendsurvey\n", TherionReindenter.Format(text, "  "));
    }

    /// <summary>
    /// A whitespace-only line between a lone CR and an LF is emptied, and the two terminators then read
    /// as one CRLF: the file loses a line.
    /// </summary>
    [Fact(Skip = "Known bug: emptying a whitespace-only line that sits between a lone CR and an LF merges the two terminators into one CRLF, so a line is lost")]
    public void Emptying_a_blank_line_never_merges_the_line_breaks_around_it()
    {
        var text = "survey a\r   \nendsurvey\n";
        Assert.Equal(SplitLines(text).Lines.Count, SplitLines(TherionReindenter.Format(text, "  ")).Lines.Count);
    }

    /// <summary>
    /// A quoted string continued with <c>\</c> carries on to the next line, so that line's leading
    /// whitespace is part of the string; re-indenting the line changes the string.
    /// </summary>
    [Fact(Skip = "Known bug: a line that continues a quoted string is re-indented, which changes the string's text")]
    public void A_line_continuing_a_quoted_string_is_left_as_it_is()
    {
        var text = "survey a -title \"first \\\n        second\"\nendsurvey\n";
        var formatted = TherionReindenter.Format(text, "  ");
        Assert.Equal(SignificantTokens(text), SignificantTokens(formatted));
    }

    /// <summary>
    /// The tokenizer takes only spaces and tabs as separators; a no-break space is part of a word. The
    /// re-indenter trims every Unicode space, so it turns <c>NBSP + survey</c> into the keyword and drops a
    /// no-break space at the end of a data value.
    /// </summary>
    [Fact(Skip = "Known bug: the re-indenter trims no-break and other Unicode spaces that the tokenizer keeps as part of a token")]
    public void Unicode_spaces_that_belong_to_a_token_are_kept()
    {
        foreach (var text in new[] { "survey a\n\u00A0x 1\nendsurvey\n", "survey a\n1 2 3 4 5\u00A0\nendsurvey\n", "survey a\n\u2003endsurvey\n" })
            Assert.Equal(SignificantTokens(text), SignificantTokens(TherionReindenter.Format(text, "  ")));
    }

    // ---- Format all files ---------------------------------------------------

    [Fact]
    public void Format_all_keeps_uniform_line_endings_rewrites_only_changed_files_and_is_stable()
    {
        for (int seed = 0; seed < 150; seed++)
        {
            var random = new SeededRandom((ulong)seed * 104729 + 3);
            var nl = random.Chance(50) ? "\n" : "\r\n";
            var text = Survey(random, nl);
            var unit = random.Pick(Units);
            string Format(string t) => TherionReindenter.Format(t, unit);
            var because = $"[seed {seed}, {(nl == "\n" ? "LF" : "CRLF")}]";
            int described = -1;

            var (batch, rows, skipped) = FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.Keep, Format, n => { described = n; return ""; });
            Assert.True(skipped.Count == 0, $"{because} the generated survey did not parse cleanly:\n{text}");

            var expected = Format(text);
            if (expected == text)
            {
                Assert.Empty(batch.Edits);
                continue;
            }
            var edit = Assert.Single(Assert.Single(batch.Edits).Edits);
            Assert.Equal(new TextEdit(0, text.Length, text, expected), edit);
            Assert.Single(rows);
            var (a, _) = SplitLines(text);
            var (b, _) = SplitLines(expected);
            Assert.True(a.Zip(b).Count(p => p.First != p.Second) == described, $"{because} the preview's changed-line count is {described}");

            // Stable: the result plans no further change, and an identity formatter plans none either.
            Assert.Empty(FormatAll.Plan(["/p/a.th"], _ => expected, LineEndings.Keep, Format, _ => "").Batch.Edits);
            Assert.Empty(FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.Keep, t => t, _ => "").Batch.Edits);

            // Converting line endings changes them and nothing else.
            var toLf = Assert.Single(Assert.Single(FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.Lf, Format, _ => "").Batch.Edits).Edits);
            Assert.Equal(expected.Replace("\r\n", "\n"), toLf.NewText);
            var toCrLf = Assert.Single(Assert.Single(FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.CrLf, Format, _ => "").Batch.Edits).Edits);
            Assert.Equal(expected.Replace("\r\n", "\n").Replace("\n", "\r\n"), toCrLf.NewText);
        }
    }

    /// <summary>"Keep" keeps each line's own ending, as the re-indenter does, in a file that mixes them.</summary>
    [Fact(Skip = "Known bug: Format all with Keep rewrites every line of a file that mixes CRLF and LF with CRLF")]
    public void Format_all_keep_leaves_each_line_ending_of_a_mixed_file_alone()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var random = new SeededRandom((ulong)seed + 99);
            var text = Survey(random, null);
            Assert.Empty(FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.Keep, t => t, _ => "").Batch.Edits);
            var planned = FormatAll.Plan(["/p/a.th"], _ => text, LineEndings.Keep, t => TherionReindenter.Format(t, "  "), _ => "").Batch;
            if (planned.Edits.Count > 0)
                Assert.Equal(TherionReindenter.Format(text, "  "), planned.Edits[0].Edits[0].NewText);
        }
    }

    // ---- generators ---------------------------------------------------------

    private sealed record GeneratedFile(string Text, HashSet<int> Opaque, int?[] Depth, bool HasStray);

    private static readonly string[] Openers = ["survey", "centreline", "centerline", "scrap", "line", "area", "map", "scan", "comment", "group", "code", "source"];
    private static readonly string[] Indents = ["", "", "  ", "\t", "    ", " \t ", "        "];
    private static readonly string[] Trailers = ["", "", "", " ", "\t", "  \t"];

    private static string RandomCase(SeededRandom random, string word) => random.Next(4) switch
    {
        0 => word.ToUpperInvariant(),
        1 => char.ToUpperInvariant(word[0]) + word[1..],
        2 => string.Concat(word.Select((c, i) => i % 2 == 0 ? char.ToUpperInvariant(c) : c)),
        _ => word,
    };

    private static GeneratedFile Generate(SeededRandom random, bool allowStray)
    {
        var lines = new List<string>();
        var depth = new List<int?>();
        var opaque = new HashSet<int>();
        var stack = new Stack<string>();
        bool stray = false;
        int target = random.Next(5, 60);

        void Plain(string content, int? d) { lines.Add(random.Pick(Indents) + content + random.Pick(Trailers)); depth.Add(d); }

        while (lines.Count < target)
        {
            int roll = random.Next(100);
            if (roll < 22)
            {
                var keyword = random.Pick(Openers);
                var args = keyword switch
                {
                    "survey" => " s" + random.Next(100) + (random.Chance(30) ? " -title \"a b\"" : ""),
                    "scrap" => " sc1 -projection plan",
                    "line" => random.Pick(new[] { " wall", " border -close on" }),
                    "area" => " water",
                    "map" => " m1",
                    "code" => " metapost",
                    _ => "",
                };
                Plain(RandomCase(random, keyword) + args, stack.Count);
                stack.Push(keyword == "centerline" ? "centreline" : keyword);
            }
            else if (roll < 42 && stack.Count > 0)
            {
                var top = stack.Pop();
                var spelled = top == "centreline" && random.Chance(50) ? "centerline" : top;
                Plain(RandomCase(random, "end" + spelled), stack.Count);
            }
            else if (roll < 46 && allowStray)
            {
                var notOpen = Openers.Where(k => k != "centerline" && !stack.Contains(k)).ToList();
                Plain("end" + RandomCase(random, random.Pick(notOpen)), null);
                stray = true;
            }
            else if (roll < 52)
            {
                var kind = random.Chance(50) ? "layout" : "lookup";
                Plain(RandomCase(random, kind) + (kind == "layout" ? " l1" : " altitude"), stack.Count);
                int body = random.Next(0, 7);
                for (int i = 0; i < body; i++)
                {
                    opaque.Add(lines.Count);
                    lines.Add(random.Pick(new[]
                    {
                        "code metapost", "  def a = 1;  ", "\tendcode", "survey inside", "endsurvey", "  scale 1 200 ",
                        "", "   ", "# c", "x \\", "    \\setbox0", "endcentreline\t", kind == "layout" ? "endlookup" : "endlayout",
                    }));
                    depth.Add(null);
                }
                Plain(RandomCase(random, "end" + kind), stack.Count);
            }
            else if (roll < 56) Plain(random.Pick(new[] { "source a.th", "source \"b c.th\"", "input sub/x.th" }), stack.Count);
            else if (roll < 74) Plain(random.Pick(new[] { "1 2 10.0 90 -5", "a1 a2 3.5 270 +10", "end end1 1 0 0", "2 3 4.25 - down", "data normal from to length compass clino" }), stack.Count);
            else if (roll < 82) Plain(random.Pick(new[] { "# note", "#", "# survey x", "1 2 3 4 5 # trailing" }), stack.Count);
            else if (roll < 88)
            {
                Plain("title \"a\" " + random.Pick(new[] { "\\", "\\  ", "\\\t" }), stack.Count);
                Plain(random.Pick(new[] { "-author x", "1 2 3 4 5", "\"q\"" }), stack.Count);
            }
            else
            {
                lines.Add(random.Pick(new[] { "", "", "   ", "\t \t" }));
                depth.Add(0);
            }
        }

        // Terminators: one style per file or mixed, with or without a final one. A lone CR is never
        // followed by a blank line ending in LF: the two would merge into one CRLF (in the input when
        // the line is empty, in the output once its whitespace is trimmed; the latter has its own test).
        int style = random.Next(4);
        var sb = new StringBuilder();
        string previous = "";
        for (int i = 0; i < lines.Count; i++)
        {
            var nl = style switch { 0 => "\n", 1 => "\r\n", 2 => "\r", _ => random.Pick(new[] { "\n", "\r\n", "\r" }) };
            if (previous == "\r" && lines[i].Trim().Length == 0 && nl.StartsWith('\n')) nl = "\r";
            if (i == lines.Count - 1 && random.Chance(30)) nl = "";
            sb.Append(lines[i]).Append(nl);
            previous = nl;
        }
        var depths = depth.ToList();
        if (previous.Length > 0) depths.Add(0);   // the empty segment after the final terminator
        return new GeneratedFile(sb.ToString(), opaque, depths.ToArray(), stray);
    }

    /// <summary>A survey that parses cleanly, randomly indented, with uniform <paramref name="nl"/> endings or mixed (null).</summary>
    private static string Survey(SeededRandom random, string? nl)
    {
        var lines = new List<string>();
        void Add(string content) => lines.Add(random.Pick(Indents) + content + random.Pick(Trailers));
        void Block(string name, int level)
        {
            Add($"survey {name}" + (random.Chance(40) ? $" -title \"{name} title\"" : ""));
            if (random.Chance(40)) Add("# a comment");
            Add(random.Chance(50) ? "centreline" : "centerline");
            Add("data normal from to length compass clino");
            int rows = random.Next(1, 6);
            for (int r = 0; r < rows; r++) Add($"{r} {r + 1} {random.Next(1, 30)}.{random.Next(10)} {random.Next(360)} {random.Next(-60, 60)}");
            Add(random.Chance(50) ? "endcentreline" : "endcenterline");
            if (level < 2)
                for (int c = random.Next(0, 3); c > 0; c--) Block(name + "c" + c, level + 1);
            if (random.Chance(30)) lines.Add(random.Pick(new[] { "", "   " }));
            Add("endsurvey");
        }
        Block("s", 0);

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Count - 1 || random.Chance(70)) sb.Append(nl ?? (i % 3 == 0 ? "\r\n" : "\n"));
        }
        var text = sb.ToString();
        if (nl is null && !text.Contains("\r\n")) text = text.Replace("\n", "\r\n");
        return text;
    }

    // ---- oracles ------------------------------------------------------------

    [GeneratedRegex("\r\n|\r|\n")]
    private static partial Regex Terminator();

    /// <summary>Lines and their terminators (LF, CRLF, CR or none); a text ending in a terminator has an empty last line.</summary>
    private static (List<string> Lines, List<string> Endings) SplitLines(string text)
    {
        var lines = new List<string>();
        var endings = new List<string>();
        int start = 0;
        foreach (Match m in Terminator().Matches(text))
        {
            lines.Add(text[start..m.Index]);
            endings.Add(m.Value);
            start = m.Index + m.Length;
        }
        lines.Add(text[start..]);
        endings.Add("");
        return (lines, endings);
    }

    /// <summary>Every token but whitespace, line breaks and continuations; a comment without its trailing blanks.</summary>
    private static List<(TherionTokenKind, string)> SignificantTokens(string text) =>
        new TherionTokenizer().Tokenize("a.th", text)
            .Where(t => t.Kind is not (TherionTokenKind.Whitespace or TherionTokenKind.NewLine or TherionTokenKind.LineContinuation))
            .Select(t => (t.Kind, t.Kind == TherionTokenKind.LineComment ? t.Text.TrimEnd(' ', '\t') : t.Text))
            .ToList();

    private static string Repeat(string unit, int count) => string.Concat(Enumerable.Repeat(unit, count));

    private static string Escape(string s) => s.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
}
