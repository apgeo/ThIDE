// Generated checks for the two places where text crosses the process boundary: Therion's output lines
// have to come back as a file, a line and a span the editor can follow, and the arguments a process is
// started with have to reach it as written. Seeds are fixed (Therion.TestSupport.GeneratedRuns).

using System.Diagnostics;
using Therion.Build.Automation;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.TestSupport;

namespace Therion.Build.Tests;

public class BuildOutputAndArgumentPropertyTests : IDisposable
{
    private const int SeedBase = 26_000;
    private readonly HeuristicTherionOutputParser _parser = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thbuild_prop_" + Guid.NewGuid().ToString("N"));

    public BuildOutputAndArgumentPropertyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    // ---- generated paths and log lines ---------------------------------------------------------

    // Folder and file names with the characters that make a path hard to pick out of a line of text:
    // spaces, parentheses, brackets, quotes, commas, accents, non-Latin scripts, an astral character.
    private static readonly string[] Parts =
    [
        "cave", "My Cave", "a b  c", "Veneția-Superioară", "näive-ß", "日本語", "пещера", "שלום", "emoji \U0001F600",
        "plan (old)", "(x)", "survey [2]", "it's", "a,b", "a;b", "x=y", "50%", "a&b", "a+b", "#1", "~tmp", "@home",
        "v1.2.3", "dot.name", "-dash", "_under", "UPPER", "e\u0301\u0301",
    ];

    private static readonly string[] Extensions = ["th", "th2", "thconfig", "svx", "lox", "3d", "xvi", "mp"];

    private static string RandomPath(Random random)
    {
        var sep = random.Next(3) == 0 ? "\\" : "/";
        var root = random.Next(8) switch
        {
            0 => "C:" + sep,
            1 => "d:" + sep,
            2 => "\\\\server" + random.Next(3) + "\\share\\",
            3 => "/home/user/",
            4 => "/",
            _ => "",
        };
        if (root.StartsWith("\\\\")) sep = "\\";
        var parts = Enumerable.Range(0, random.Next(0, 4)).Select(_ => Parts[random.Next(Parts.Length)]).ToList();
        var file = Parts[random.Next(Parts.Length)] + "." + Extensions[random.Next(Extensions.Length)];
        // A leading dash would be an option on a command line, not part of any of these logs; a path
        // that starts with a blank or ends the previous word would not be a path token.
        var path = root + string.Concat(parts.Select(p => p + sep)) + file;
        return path.TrimStart();
    }

    private static int RandomLine(Random random) => random.Next(5) switch
    {
        0 => 1,
        1 => random.Next(1, 100),
        2 => random.Next(100, 100_000),
        3 => int.MaxValue,
        _ => random.Next(1, 1_000_000),
    };

    private static void AssertNavigable(SourceSpan? span, string path, int line, string context)
    {
        Assert.NotNull(span);
        var s = span!.Value;
        Assert.True(s.FilePath == path, $"{context}: file '{s.FilePath}' instead of '{path}'");
        Assert.True(s.Start.Line == line, $"{context}: line {s.Start.Line} instead of {line}");
        Assert.True(s.Length > 0 && !s.IsEmpty, $"{context}: the span has no extent, so the editor would ignore it");
        Assert.True(s.End.Line > s.Start.Line || s.End.Column > s.Start.Column, $"{context}: the span ends before it starts");
    }

    [Fact]
    public void A_native_therion_line_gives_its_file_line_and_a_span()
    {
        GeneratedRuns.ForEachSeed(SeedBase, 500, seed =>
        {
            var random = new Random(seed);
            var path = RandomPath(random);
            int line = RandomLine(random);
            var kind = random.Next(3) switch { 0 => "error", 1 => "warning", _ => "info" };
            var text = random.Next(3) switch
            {
                0 => $"therion: {kind} -- {path} [{line}] -- station does not exist -- E65a",
                1 => $"{kind} -- {path} [{line}] -- something odd",
                _ => $"C:\\Program Files\\Therion\\therion.exe: {kind} -- {path} [{line}] -- scrap id -- s{random.Next(9)}",
            };

            var result = _parser.Classify(text, isStderr: random.Next(2) == 0);

            AssertNavigable(result.Span, path, line, text);
        });
    }

    [Fact]
    public void A_file_colon_line_message_gives_its_file_line_and_a_span()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 10_000, 500, seed =>
        {
            var random = new Random(seed);
            var path = RandomPath(random);
            int line = RandomLine(random);
            var position = random.Next(2) == 0 ? $"{line}" : $"{line}:{random.Next(1, 200)}";
            var text = $"{path}:{position}: {(random.Next(2) == 0 ? "warning" : "error")} -- bad thing";

            var result = _parser.Classify(text, isStderr: false);

            AssertNavigable(result.Span, path, line, text);
        });
    }

    [Fact]
    public void A_labelled_path_line_gives_the_whole_path_and_a_span()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 20_000, 300, seed =>
        {
            var random = new Random(seed);
            var path = RandomPath(random);
            var label = new[] { "configuration file", "initialization file", "reading file", "output file" }[random.Next(4)];

            var result = _parser.Classify($"{label}: {path}", isStderr: false);

            AssertNavigable(result.Span, path, 1, $"{label}: {path}");
        });
    }

    [Fact]
    public void The_problem_matchers_follow_the_same_lines_and_never_throw()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 30_000, 500, seed =>
        {
            var random = new Random(seed);
            var path = RandomPath(random);
            int line = RandomLine(random);

            var native = ProblemMatchers.Match("therion", $"therion: error -- {path} [{line}] -- bad -- E1", true, null, "build");
            AssertNavigable(native?.Span, path, line, "therion matcher");

            var fileLine = ProblemMatchers.Match("file-line", $"{path}:{line}:{random.Next(1, 90)}: warning: odd", false, null, "lint");
            AssertNavigable(fileLine?.Span, path, line, "file-line matcher");
        });
    }

    [Theory]
    [InlineData("a.th:99999999999:3: error: x")]
    [InlineData("a.th:2147483648: error: x")]
    [InlineData("a.th:\u0661\u0662: error: x")]            // Arabic-Indic digits are \d to a regex, not to int.Parse
    [InlineData("a.th:1:\u0663: error: x")]
    [InlineData("therion: error -- a.th [99999999999] -- x")]
    [InlineData("therion: error -- a.th [\u0661\u0662] -- x")]
    [InlineData("a.th:0: error: x")]
    [InlineData("a.th:-4: error: x")]
    [InlineData(":::")]
    [InlineData("")]
    public void A_line_number_that_is_not_an_int_does_not_throw_and_still_gives_a_navigable_span(string text)
    {
        foreach (var matcher in new[] { "therion", "file-line" })
        {
            var diagnostic = Record.Exception(() => ProblemMatchers.Match(matcher, text, true, "/work", "task"));
            Assert.Null(diagnostic);
        }
        var classified = Record.Exception(() => _parser.Classify(text, true));
        Assert.Null(classified);

        var span = _parser.Classify(text, true).Span;
        if (span is { } s) Assert.True(s.Start.Line >= 1 && !s.IsEmpty, $"{text}: line {s.Start.Line}");
        var matched = ProblemMatchers.Match("file-line", text, true, "/work", "task");
        if (matched is not null) Assert.True(matched.Span.Start.Line >= 1 && !matched.Span.IsEmpty, $"{text}: line {matched.Span.Start.Line}");
    }

    [Fact]
    public void Arbitrary_log_text_never_throws_and_a_located_line_always_has_a_span_with_extent()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 40_000, 400, seed =>
        {
            var random = new Random(seed);
            const string alphabet = "ab. :[]()-\\/\t\"'0123456789\u0661é\U0001F600thcfile";
            var text = new string(Enumerable.Range(0, random.Next(0, 70)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            // Splice the odd tokens a real line holds into the soup.
            if (random.Next(2) == 0) text += " -- " + RandomPath(random) + " [" + random.Next(0, 999) + "] -- x";

            var result = _parser.Classify(text, isStderr: random.Next(2) == 0);

            Assert.Equal(text, result.Text);
            if (result.Span is { } span)
            {
                Assert.True(span.Start.Line >= 1 && span.Start.Column >= 1, $"'{text}': position {span.Start.Line}:{span.Start.Column}");
                Assert.True(!span.IsEmpty, $"'{text}': empty span");
                Assert.False(string.IsNullOrEmpty(span.FilePath), $"'{text}': no file");
            }
        });
    }

    // ---- process arguments ---------------------------------------------------------------------

    private static readonly string[] ArgumentPieces =
    [
        "plain", "", " ", "two words", "\"", "\"\"", "a\"b", "\\", "\\\\", "a\\", "a\\\\", "\\\"", "C:\\dir with space\\", "C:\\dir\\file.th",
        "it's", "'", "$HOME", "`x`", "$(x)", "*", "?", "[a]", "~", "-n", "--", "--help", "-", "a=b", "%PATH%", "^", "&", "|", ">", "<", ";", "!",
        "\u00E9", "e\u0301", "\u65E5\u672C", "\U0001F600", "\u05E9\u05DC\u05D5\u05DD", "\t", "a\tb", "trailing ",
    ];

    private static List<string> RandomArguments(Random random)
    {
        var args = new List<string>();
        int count = random.Next(1, 8);
        for (int i = 0; i < count; i++)
        {
            int pieces = random.Next(1, 4);
            args.Add(string.Concat(Enumerable.Range(0, pieces).Select(_ => ArgumentPieces[random.Next(ArgumentPieces.Length)])));
        }
        return args;
    }

    [Fact]
    public void The_arguments_of_a_program_spec_are_passed_through_one_for_one()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 50_000, 500, seed =>
        {
            var args = RandomArguments(new Random(seed));

            // By full path: on Windows a bare name that is not found is refused (see ExecutableSearch).
            var psi = ProcessRunner.StartInfo(new ProcessSpec(Environment.ProcessPath!, args, WorkingDirectory: _dir));

            Assert.Equal(args, psi.ArgumentList.ToList());
            Assert.True(string.IsNullOrEmpty(psi.Arguments), "the same arguments must not be given twice");
            Assert.False(psi.UseShellExecute);
        });
    }

    [Fact]
    public async Task The_arguments_of_a_started_program_arrive_as_written()
    {
        if (OperatingSystem.IsWindows()) return; // the shell used as the echo is /bin/sh
        // printf is given its format first; each argument is then printed NUL-terminated, so a newline
        // inside one cannot be taken for the end of another. A NUL cannot be in an argument at all.
        // The runner reads lines, so the arguments here have no newline or carriage return.
        var failures = new List<string>();
        for (int seed = SeedBase + 60_000; seed < SeedBase + 60_000 + GeneratedRuns.Count(25); seed++)
        {
            var args = RandomArguments(new Random(seed)).Select(a => a.Replace('\t', ' ')).ToList();
            var lines = new List<ProcessLine>();
            var spec = new ProcessSpec("/bin/sh", ["-c", "for a in \"$@\"; do printf '[%s]\\n' \"$a\"; done", "sh", .. args], WorkingDirectory: _dir);

            var outcome = await ProcessRunner.RunAsync(spec, new SyncProgress<ProcessLine>(lines.Add));

            var expected = args.Select(a => "[" + a + "]").ToList();
            var got = lines.Where(l => !l.IsError).Select(l => l.Text).ToList();
            if (!outcome.Succeeded || !expected.SequenceEqual(got))
                failures.Add($"seed {seed}: exit {outcome.ExitCode}, expected {string.Join(" ", expected)} but got {string.Join(" ", got)}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task A_value_quoted_for_the_shell_reaches_the_command_whole()
    {
        if (OperatingSystem.IsWindows()) return; // sh quoting; the cmd form is checked by its own parse below
        var failures = new List<string>();
        for (int seed = SeedBase + 70_000; seed < SeedBase + 70_000 + GeneratedRuns.Count(25); seed++)
        {
            var random = new Random(seed);
            var value = string.Concat(Enumerable.Range(0, random.Next(1, 5)).Select(_ => ArgumentPieces[random.Next(ArgumentPieces.Length)]))
                        + (random.Next(4) == 0 ? "\n" : "");
            var lines = new List<ProcessLine>();
            // printf '%s' prints the word exactly; the marker after it keeps a trailing newline visible.
            var spec = new ProcessSpec("printf '%s|END\\n' " + ShellQuote.ForArgument(value, windows: false), [], UseShell: true);

            var outcome = await ProcessRunner.RunAsync(spec, new SyncProgress<ProcessLine>(lines.Add));

            var got = string.Join("\n", lines.Where(l => !l.IsError).Select(l => l.Text));
            if (!outcome.Succeeded || got != value + "|END")
                failures.Add($"seed {seed}: value '{value}' came back as '{got}'");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // cmd.exe's reading of one double-quoted word, written from its documented rule for this form:
    // inside quotes a doubled quote is one literal quote.
    private static string UnquoteForCmd(string word)
    {
        Assert.True(word.Length >= 2 && word[0] == '"' && word[^1] == '"', $"not a quoted word: {word}");
        var inner = word[1..^1];
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '"')
            {
                Assert.True(i + 1 < inner.Length && inner[i + 1] == '"', $"a lone quote inside {word}");
                i++;
            }
            result.Append(inner[i]);
        }
        return result.ToString();
    }

    [Fact]
    public void A_value_quoted_for_cmd_reads_back_as_the_value()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 80_000, 500, seed =>
        {
            var random = new Random(seed);
            var value = string.Concat(Enumerable.Range(0, random.Next(0, 5)).Select(_ => ArgumentPieces[random.Next(ArgumentPieces.Length)]));

            var quoted = ShellQuote.ForArgument(value, windows: true);

            Assert.Equal(value, UnquoteForCmd(quoted));
        });
    }

    // ---- the Therion compiler's own command line -----------------------------------------------

    private sealed class FixedLocator : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ToolInfo?>(new ToolInfo(toolId, "/opt/fake/therion", "6.3.4", "test"));
    }

    [Fact]
    public async Task An_entry_path_with_odd_characters_reaches_therion_as_one_argument()
    {
        if (OperatingSystem.IsWindows()) return; // names with quotes and backslashes are not file names there
        var names = new[] { "my cave.thconfig", "it's \"the\" one.thconfig", "back\\slash.thconfig", "$HOME `x`.thconfig", "日本 \U0001F600.thconfig", "-q.thconfig", "trailing .thconfig", "  lead.thconfig" };
        foreach (var name in names)
        {
            var entry = Path.Combine(_dir, name);
            File.WriteAllText(entry, "");
            var seen = new List<ProcessStartInfo>();

            await new TherionCompiler(new FixedLocator()).CompileAsync(entry, new TherionCompileOptions
            {
                Quiet = true,
                LogFile = "logs/" + name + ".log",
                ConfigureProcess = psi =>
                {
                    seen.Add(psi);
                    var args = psi.ArgumentList.ToList();
                    psi.ArgumentList.Clear();
                    // Echo each argument back, bracketed, through a shell.
                    psi.FileName = "/bin/sh";
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add("for a in \"$@\"; do printf '[%s]\\n' \"$a\"; done");
                    psi.ArgumentList.Add("sh");
                    foreach (var a in args) psi.ArgumentList.Add(a);
                    return psi;
                },
            }, new Lines(out var echoed));

            var original = Assert.Single(seen);
            var echo = echoed.ToList();
            Assert.Equal(new[] { "[-q]", "[-l]", "[" + Path.Combine(_dir, "logs", name + ".log") + "]", "[" + entry + "]" }, echo);
        }
    }

    private sealed class Lines : IProgress<CompilerOutputLine>
    {
        private readonly List<string> _seen = [];
        public Lines(out List<string> seen) => seen = _seen;
        public void Report(CompilerOutputLine value) { lock (_seen) _seen.Add(value.Text); }
    }
}
