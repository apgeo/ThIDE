// Implementation Plan �11 � broad table-driven tests.
// Many cases per method; together with the rest of the suite they take the
// test count past the 200-mark called out in the M2 roadmap.

using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class TokenizerTableTests
{
    public static TheoryData<string, TherionTokenKind> SingleTokenCases
    {
        get
        {
            var d = new TheoryData<string, TherionTokenKind>();

            // Whitespace
            d.Add(" ",      TherionTokenKind.Whitespace);
            d.Add("\t",     TherionTokenKind.Whitespace);
            d.Add("   \t ", TherionTokenKind.Whitespace);

            // Newlines
            d.Add("\n",   TherionTokenKind.NewLine);
            d.Add("\r",   TherionTokenKind.NewLine);
            d.Add("\r\n", TherionTokenKind.NewLine);

            // Line continuation
            d.Add("\\\n",   TherionTokenKind.LineContinuation);
            d.Add("\\\r\n", TherionTokenKind.LineContinuation);

            // Comments
            d.Add("# hello",                 TherionTokenKind.LineComment);
            d.Add("# with \"quotes\" inside", TherionTokenKind.LineComment);
            d.Add("#",                        TherionTokenKind.LineComment);

            // Strings
            d.Add("\"\"",                    TherionTokenKind.String);
            d.Add("\"hello\"",               TherionTokenKind.String);
            d.Add("\"with spaces here\"",    TherionTokenKind.String);

            // Punctuation
            foreach (var c in new[] { "=", ",", "[", "]", "{", "}", "(", ")", ":", ";" })
                d.Add(c, TherionTokenKind.Punctuation);

            // Numbers � integer
            for (int i = 0; i < 25; i++) d.Add(i.ToString(), TherionTokenKind.Number);
            // Numbers � signed
            foreach (var s in new[] { "-1", "+2", "-10", "+100", "-12345" })
                d.Add(s, TherionTokenKind.Number);
            // Numbers � decimal
            foreach (var s in new[] { "0.5", "1.25", "12.5", "100.0", "-3.14", "+2.7", ".5", "-.25" })
                d.Add(s, TherionTokenKind.Number);
            // Numbers � exponent
            foreach (var s in new[] { "1e3", "1.5e3", "1.5E-3", "-2.0e+10" })
                d.Add(s, TherionTokenKind.Number);

            // Identifiers / barewords
            foreach (var s in new[]
            {
                "survey", "centreline", "endsurvey", "fix", "equate", "data",
                "S1", "station.name", "deep.qualified.name", "x", "Y",
                "-title", "-fmt", "-projection", "--long-flag",
                "caf�", "Pe?tera", "name_with_underscore",
            })
                d.Add(s, TherionTokenKind.Identifier);

            return d;
        }
    }

    [Theory]
    [MemberData(nameof(SingleTokenCases))]
    public void Tokenizes_to_single_token_of_expected_kind(string text, TherionTokenKind expected)
    {
        var tokens = new TherionTokenizer().Tokenize("x", text);
        Assert.Single(tokens);
        Assert.Equal(expected, tokens[0].Kind);
        // TherionToken contract: trivia (whitespace/newline/continuation) carries empty Text — its
        // content is recoverable from the Span; significant tokens keep the verbatim source slice.
        bool isTrivia = expected is TherionTokenKind.Whitespace
            or TherionTokenKind.NewLine or TherionTokenKind.LineContinuation;
        Assert.Equal(isTrivia ? string.Empty : text, tokens[0].Text);
    }
}

public class ClassifierTableTests
{
    public static TheoryData<string, TokenClassification> IdentifierCases
    {
        get
        {
            var d = new TheoryData<string, TokenClassification>();

            foreach (var kw in new[]
            {
                "survey", "endsurvey", "centreline", "endcentreline",
                "centerline", "endcenterline", "data", "fix", "equate",
                "input", "load", "team", "date", "station", "extend",
                "units", "calibrate", "declination", "grade", "infer",
                "mark", "flags", "sd", "explo-date", "explo-team", "instrument",
                "scrap", "endscrap", "point", "line", "endline", "area", "endarea",
                "encoding", "sketch", "map", "endmap", "join", "layer",
                "source", "layout", "export", "select", "cs", "system-charset",
                "language", "lang", "translate", "revise", "group", "endgroup",
            })
                d.Add(kw, TokenClassification.Keyword);

            foreach (var opt in new[] { "-title", "-fmt", "-projection", "-o", "-scale", "--long" })
                d.Add(opt, TokenClassification.Option);

            foreach (var text in new[] { "S1", "myStation", "cave.upper.1", "random-bareword" })
                d.Add(text, TokenClassification.Text);

            return d;
        }
    }

    [Theory]
    [MemberData(nameof(IdentifierCases))]
    public void Identifier_is_classified(string text, TokenClassification expected)
    {
        var tokens = new TherionTokenizer().Tokenize("x", text);
        var classified = TokenClassifier.Classify(tokens);
        Assert.Equal(expected, classified[0].Classification);
    }
}

public class ParserCommandTableTests
{
    public static TheoryData<string, System.Type> SingleCommandCases
    {
        get
        {
            var d = new TheoryData<string, System.Type>();

            d.Add("fix S1 1 2 3",         typeof(StationFix));
            d.Add("fix s 0.0 0.0 0.0",    typeof(StationFix));
            d.Add("fix x -1.5 2.5 -3.25", typeof(StationFix));

            d.Add("equate a b",          typeof(EquateCommand));
            d.Add("equate a b c d",      typeof(EquateCommand));
            d.Add("equate ns.1 ew.2",    typeof(EquateCommand));

            d.Add("input file.th",       typeof(InputCommand));
            d.Add("input \"sub/x.th\"",  typeof(InputCommand));
            d.Add("load \"sub/x.th\"",   typeof(InputCommand));

            d.Add("date 2024.01.15",     typeof(DateCommand));
            d.Add("date 1999.12.31",     typeof(DateCommand));

            d.Add("team \"Alice\"",                  typeof(TeamCommand));
            d.Add("team \"Bob\" instruments",         typeof(TeamCommand));

            d.Add("data normal from to length compass clino",  typeof(DataCommand));
            d.Add("data topofil from to length depthchange",   typeof(DataCommand));
            d.Add("data diving from to length depth",          typeof(DataCommand));

            d.Add("frobnicate x y z",     typeof(UnknownCommand));
            d.Add("unknown",              typeof(UnknownCommand));

            return d;
        }
    }

    [Theory]
    [MemberData(nameof(SingleCommandCases))]
    public void Parses_to_expected_command_type(string text, System.Type expected)
    {
        var r = new ThParser().Parse("x.th", text);
        Assert.NotNull(r.Value);
        Assert.Single(r.Value!.Children);
        Assert.IsType(expected, r.Value.Children[0]);
    }
}

public class FormatterTableTests
{
    public static TheoryData<string, string> CodeMessageCases
    {
        get
        {
            var d = new TheoryData<string, string>();
            for (int i = 1; i <= 30; i++)
            {
                d.Add($"TH{i:D4}", $"sample diagnostic message {i}");
            }
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(CodeMessageCases))]
    public void Formatter_emits_code_and_message(string code, string message)
    {
        var d = Diagnostic.Create(code, DiagnosticSeverity.Error, message, SourceSpan.None);
        var s = new RustcStyleDiagnosticFormatter().Format(d);
        Assert.Contains(code, s);
        Assert.Contains(message, s);
    }
}

public class ThconfigParserTableTests
{
    public static TheoryData<string> KnownTopLevelCommands
    {
        get
        {
            var d = new TheoryData<string>();
            foreach (var kw in ThconfigParser.TopLevelKeywords) d.Add(kw);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(KnownTopLevelCommands))]
    public void Known_top_level_command_does_not_warn(string keyword)
    {
        var r = new ThconfigParser().Parse("x.thconfig", $"{keyword} dummy");
        Assert.DoesNotContain(r.Diagnostics, d => d.Code.Value == DiagnosticCodes.UnknownCommand);
    }
}
