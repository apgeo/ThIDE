// Library diagnostics are English; the app shows them through Diag_Msg_/Diag_Hint_ templates.
// All inputs are invented.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class DiagnosticTextTests
{
    private static readonly CultureInfo Ro = new("ro");

    // Codes passed as string literals; constants of DiagnosticCodes / SemanticDiagnosticCodes are read by reflection.
    private static readonly string[] LiteralCodes =
    {
        SemanticRuleRunner.RuleFailureCode, "TH_EDIT_001", "TH_EDIT_002",
        "TH_BUILD_001", "TH_BUILD_002", "TH_BUILD_003", "TH_WS_001", "TH_WS_002",
        "TH_LINK_404", "THP-LARGE-FILE",
    };

    // Not translated on purpose: Therion's own output lines and automation task output.
    private static readonly string[] Untranslated = { "TH_BUILD_OUT", "task:" };

    // Declared but emitted nowhere.
    private static readonly string[] NotEmitted = { SemanticDiagnosticCodes.MalformedReference };

    private static IEnumerable<string> ConstantCodes(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static IEnumerable<string> EmittedCodes() =>
        ConstantCodes(typeof(DiagnosticCodes)).Concat(ConstantCodes(typeof(SemanticDiagnosticCodes)))
            .Concat(LiteralCodes).Except(NotEmitted).Distinct();

    private static Dictionary<string, string> Set(CultureInfo culture)
    {
        var set = Tr.Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void Every_emitted_code_has_an_English_and_a_Romanian_message_template()
    {
        var en = Set(CultureInfo.InvariantCulture);
        var ro = Set(Ro);
        var missing = EmittedCodes()
            .Where(code => !en.ContainsKey(DiagnosticText.MessagePrefix + code) || !ro.ContainsKey(DiagnosticText.MessagePrefix + code))
            .ToList();
        Assert.True(missing.Count == 0, "No Diag_Msg_ template in EN and RO for: " + string.Join(", ", missing));

        var templates = en.Keys.Where(k => k.StartsWith(DiagnosticText.MessagePrefix, StringComparison.Ordinal)
                                        || k.StartsWith(DiagnosticText.HintPrefix, StringComparison.Ordinal)).ToList();
        foreach (var key in templates)
        {
            Assert.True(ro.TryGetValue(key, out var r) && r != en[key], $"{key} has no Romanian translation");
            Assert.True(DiagnosticText.ToPattern(en[key]) is not null, $"{key}: the English template is not a valid format");
            Assert.Equal(Placeholders(en[key]), Placeholders(r!));
            Assert.Contains(DiagnosticText.CodeOf(key), EmittedCodes());
        }
    }

    [Fact]
    public void Every_literal_code_in_the_sources_is_known_to_the_test()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ThIDE.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var rx = new Regex(@"Diagnostic\.Create\(\s*""([^""]+)""");
        var found = new[] { "src", "ThIDE" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root!.FullName, d), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(p => rx.Matches(File.ReadAllText(p)).Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        var unknown = found.Except(LiteralCodes).Except(Untranslated).ToList();
        Assert.True(unknown.Count == 0, "New literal diagnostic codes — add a Diag_Msg_ template and list them here: " + string.Join(", ", unknown));
    }

    private static string[] Placeholders(string s) =>
        Regex.Matches(s.Replace("{{", "").Replace("}}", ""), @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();

    // ---- end to end: invented files full of mistakes --------------------------------------

    private const string Survey =
        "encoding utf-8\n" +
        "#@region \"open\"\n" +
        "survey demo -title \"Demo\n" +
        "  centreline\n" +
        "    units lenght metres\n" +
        "    data normal from to length compass clino\n" +
        "    1 2 5.0 10 -5\n" +
        "    2 3 abc 20 5\n" +
        "    3 4 -4.0 400 5\n" +
        "    4 5 4.0 20\n" +
        "    flags bogus\n" +
        "    mark 1 wrong\n" +
        "    extend weird 2\n" +
        "    station 2 \"x\" nonsense\n" +
        "    fix 1 0 0 q\n" +
        "    fix 1 0 0 0\n" +
        "    fix 1 0 0 0\n" +
        "    declination\n" +
        "    infer foo\n" +
        "    cs NOTACS\n" +
        "    data dimensions station\n" +
        "    data normal from to from length compass clino\n" +
        "    data nonsense from to\n" +
        "    data normal from to lenght compass clino\n" +
        "    equate 1\n" +
        "    grid-angle\n" +
        "    sd\n" +
        "    calibrate\n" +
        "    team \"A B\" explorer\n" +
        "    date 2020.13.01\n" +
        "  endcentreline\n" +
        "  frobnicate now\n" +
        "  survey bad!id\n" +
        "  endsurvey other\n" +
        "  map m1\n" +
        "  endmap\n" +
        "endsurvey wrong\n";

    private const string Sketch =
        "encoding utf-8\n" +
        "#@endregion\n" +
        "scrap s1 -projection plan\n" +
        "point a 1 station\n" +
        "point 1 2 bogus-type\n" +
        "point 1 2 station -orientation 10 -foo x\n" +
        "point 1 2 label -orientation 400\n" +
        "line weird\n" +
        "  0 0\n" +
        "  1 1\n" +
        "endline\n" +
        "line wall -head end\n" +
        "  0 0\n" +
        "  1 1\n" +
        "endline\n" +
        "area blah\n" +
        "endarea\n" +
        "frob\n" +
        "endscrap\n";

    private const string Config =
        "encoding utf-8\n" +
        "source x.th\n" +
        "cs FOO\n" +
        "export nonsense\n" +
        "export map -fmt zzz\n" +
        "export model -output rez\\cave.lox\n" +
        "bogus top level\n" +
        "layout l\n" +
        "  wrongopt 1\n" +
        "endlayout\n";

    private const string Xvi =
        "set XVIgrids {1 m}\n" +
        "set XVIfoo {}\n" +
        "garbage here\n" +
        "set XVIgrid {1 2 3}\n";

    private static List<Diagnostic> Invented()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thide-diagtext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var all = new List<Diagnostic>();
            all.AddRange(DocumentParser.Parse(Path.Combine(dir, "demo.th"), Survey).Diagnostics);
            all.AddRange(DocumentParser.Parse(Path.Combine(dir, "demo.th2"), Sketch).Diagnostics);
            all.AddRange(DocumentParser.Parse(Path.Combine(dir, "demo.thconfig"), Config).Diagnostics);
            all.AddRange(new XviParser().Parse(Path.Combine(dir, "demo.xvi"), Xvi).Diagnostics);
            return all;
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_Romanian_run_over_files_with_many_mistakes_shows_no_English_message()
    {
        var diags = Invented();
        // Enough variety to mean something: parser, schema, semantic, directive, th2, thconfig and xvi codes.
        Assert.True(diags.Select(d => d.Code.Value).Distinct().Count() >= 25,
            "only " + string.Join(", ", diags.Select(d => d.Code.Value).Distinct()));

        UiLanguageCollection.In(Ro, () =>
        {
            var english = diags.Where(d => DiagnosticText.Message(d) == d.Message)
                .Select(d => $"{d.Code}: {d.Message}")
                .Concat(diags.Where(d => !string.IsNullOrEmpty(d.Hint) && DiagnosticText.Hint(d) == d.Hint)
                    .Select(d => $"{d.Code} hint: {d.Hint}"))
                .Distinct().ToList();
            Assert.True(english.Count == 0, "Still English in Romanian:\n" + string.Join("\n", english));
        });
    }

    [Fact]
    public void In_English_every_message_is_the_library_text()
    {
        var diags = Invented();
        UiLanguageCollection.In(new CultureInfo("en"), () =>
        {
            foreach (var d in diags)
            {
                Assert.Equal(d.Message, DiagnosticText.Message(d));
                Assert.Equal(d.Hint, DiagnosticText.Hint(d));
            }
        });
    }

    // ---- the template mechanics --------------------------------------------------------------

    private static Diagnostic D(string code, string message, string? hint = null) =>
        Diagnostic.Create(code, DiagnosticSeverity.Warning, message, SourceSpan.None, hint);

    [Fact]
    public void Arguments_keep_their_order_when_the_translation_moves_them()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            Assert.Equal("„endsurvey b” nu corespunde cu „survey a”.",
                DiagnosticText.Message(D(DiagnosticCodes.BlockIdMismatch, "'endsurvey b' does not match 'survey a'.")));
            // {0} and {1} swap places in Romanian.
            Assert.Equal("Valoarea „x” pentru „length” nu este un număr valid.",
                DiagnosticText.Message(D(SemanticDiagnosticCodes.DataValueInvalid, "'length' value 'x' is not a valid number.")));
            Assert.Equal("Valoarea 400 pentru <angle> este în afara intervalului [0, 360).",
                DiagnosticText.Message(D(DiagnosticCodes.ValueOutOfRange, "<angle> value 400 is outside [0, 360).")));
        });
    }

    [Fact]
    public void Text_no_template_reproduces_stays_English()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            // A user's own naming-rule message, an unknown code, a code with a reworded message.
            Assert.Equal("Use cave prefixes.", DiagnosticText.Message(D(SemanticDiagnosticCodes.NamingConvention, "Use cave prefixes.")));
            Assert.Equal("Something new.", DiagnosticText.Message(D("TH9999", "Something new.")));
            Assert.Equal("Unknown command 'x' here.", DiagnosticText.Message(D(DiagnosticCodes.UnknownCommand, "Unknown command 'x' here.")));
            Assert.Null(DiagnosticText.Hint(D(DiagnosticCodes.UnknownCommand, "Unknown command 'x'.")));
        });
    }

    [Fact]
    public void The_editor_tooltip_names_the_severity_and_the_hint_in_the_UI_language()
    {
        UiLanguageCollection.In(Ro, () =>
            Assert.Equal("Avertisment: Comandă de nivel superior necunoscută „frob”.\nSe așteaptă una dintre: source, layout",
                DiagnosticText.Tooltip(D(DiagnosticCodes.UnknownCommand, "Unknown top-level command 'frob'.",
                    "Expected one of: source, layout"))));
    }

    [Fact]
    public void The_diagnostics_panel_rows_carry_the_translated_message()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            var vm = new ThIDE.ViewModels.DiagnosticsViewModel();
            vm.Load(new[] { D(DiagnosticCodes.UnknownCommand, "Unknown command 'frob'.") });
            Assert.Equal("Comandă necunoscută „frob”.", Assert.Single(vm.Rows).Message);
        });
    }
}
