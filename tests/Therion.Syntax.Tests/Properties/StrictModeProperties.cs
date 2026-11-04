// Strict mode only raises severities. Over generated .th and .th2 text, the mutation-harness
// mutants and a table of one malformed line per check: a strict parse gives the same tree and the
// same (code, span) diagnostics as a lenient one, no strict severity is lower, and every code the
// diagnostic catalog marks "lenient" (docs/diagnostics.md) is a Warning in lenient mode and an
// Error in strict mode. Seeds are fixed; set THIDE_PROPERTY_RUNS to run more cases.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax.Tests.Mutation;

namespace Therion.Syntax.Tests.Properties;

public class StrictModeProperties
{
    private const int ThSeed = 141_000;
    private const int Th2Seed = 142_000;

    private static readonly ParserOptions Lenient = new(ParserMode.Lenient);
    private static readonly ParserOptions Strict = new(ParserMode.Strict);

    /// <summary>The codes docs/diagnostics.md lists with severity "lenient" (Warning, Error in strict mode).</summary>
    private static readonly HashSet<string> LenientCodes = new(StringComparer.Ordinal)
    {
        "TH0010", "TH0020", "TH0030", "TH0033", "TH0034", "TH0036", "TH0037", "TH0038", "TH0039", "TH0040",
        "TH0041", "TH0042", "TH0043", "TH0056", "TH0050", "TH0051", "TH0052", "TH0053", "TH0054", "TH0055",
        "TH0065", "TH0066", "TH0070", "TH0071", "TH0072", "TH0073", "TH0074",
        "TH2_001", "TH2_002", "TH2_003", "TH2_004", "TH2_005", "TH2_006", "TH2_008", "TH2_009", "TH2_010",
    };

    private static ParseResult<TherionFile> Parse(string ext, string text, ParserOptions options) => ext switch
    {
        ".th2" => new Th2Parser().Parse("gen/a.th2", text, options),
        ".thconfig" => new ThconfigParser().Parse("gen/a.thconfig", text, options),
        _ => new ThParser().Parse("gen/a.th", text, options),
    };

    private static List<string> Problems(string ext, string text)
    {
        var lenient = Parse(ext, text, Lenient);
        var strict = Parse(ext, text, Strict);
        var problems = new List<string>();

        if (AstShape.Dump(lenient.Value, spans: true) != AstShape.Dump(strict.Value, spans: true))
            problems.Add("the strict tree differs from the lenient tree");

        static List<Diagnostic> Ordered(ImmutableArray<Diagnostic> ds) => ds
            .OrderBy(d => d.Code.Value, StringComparer.Ordinal).ThenBy(d => d.Span.StartOffset).ThenBy(d => d.Span.Length)
            .ThenBy(d => d.Message, StringComparer.Ordinal).ToList();
        var l = Ordered(lenient.Diagnostics);
        var s = Ordered(strict.Diagnostics);
        if (l.Count != s.Count)
        {
            problems.Add($"lenient has {l.Count} diagnostics, strict {s.Count}");
            return problems;
        }
        for (int i = 0; i < l.Count; i++)
        {
            if (l[i].Code != s[i].Code || l[i].Span != s[i].Span || l[i].Message != s[i].Message)
            {
                problems.Add($"diagnostic {i}: lenient {l[i]} vs strict {s[i]}");
                continue;
            }
            if (s[i].Severity < l[i].Severity)
                problems.Add($"{l[i].Code}: strict {s[i].Severity} is below lenient {l[i].Severity}");
            if (LenientCodes.Contains(l[i].Code.Value))
            {
                if (l[i].Severity != DiagnosticSeverity.Warning)
                    problems.Add($"{l[i].Code} is documented lenient but is {l[i].Severity} in lenient mode: {l[i].Message}");
                if (s[i].Severity != DiagnosticSeverity.Error)
                    problems.Add($"{l[i].Code} is documented lenient but is {s[i].Severity} in strict mode: {s[i].Message}");
            }
        }
        return problems;
    }

    private static void AssertNone(string what, IEnumerable<(string Name, string Ext, string Text)> cases)
    {
        foreach (var (name, ext, text) in cases)
        {
            var problems = Problems(ext, text);
            if (problems.Count > 0)
                Assert.Fail($"{what} {name}:\n  {string.Join("\n  ", problems.Take(8))}\nsource: \"{SpanChecks.Escape(text)}\"");
        }
    }

    [Fact]
    public void Generated_th_parses_alike_in_both_modes_with_strict_severities_never_lower()
    {
        AssertNone("th seed", Enumerable.Range(ThSeed, PropertyRuns.Count(250))
            .Select(seed => ($"{seed}", ".th", SourceSoup.SurveyShaped(seed, loneCr: true, surfaceBlocks: seed % 3 == 0))));
    }

    [Fact]
    public void Generated_th2_parses_alike_in_both_modes_with_strict_severities_never_lower()
    {
        AssertNone("th2 seed", Enumerable.Range(Th2Seed, PropertyRuns.Count(250))
            .Select(seed => ($"{seed}", ".th2", Th2Soup.Generate(seed).Text)));
    }

    [Fact]
    public void Mutation_harness_mutants_parse_alike_in_both_modes()
    {
        var cases = new List<(string, string, string)>();
        foreach (var (seedId, mutation) in MutationCatalog.All)
        {
            var (ext, seedText) = MutationSeeds.ById[seedId];
            if (mutation.Apply(seedText, new Random(mutation.Id.Length * 7919 + seedId.Length)) is { } mutated)
                cases.Add((mutation.Id, ext, mutated));
        }
        Assert.NotEmpty(cases);
        AssertNone("mutant", cases);
    }

    /// <summary>One malformed line (or block) per parser check whose severity follows the mode.</summary>
    public static IEnumerable<object[]> UpgradableCases() => new (string Ext, string Code, string Text)[]
    {
        (".th", DiagnosticCodes.BlockIdMismatch, "survey a\nendsurvey b\n"),
        (".th", DiagnosticCodes.UnterminatedBlock, "survey a\n"),
        (".th", DiagnosticCodes.UnterminatedBlock, "map m\n  s1\n"),
        (".th", DiagnosticCodes.UnterminatedBlock, "surface\n  1 2\n"),
        (".th", DiagnosticCodes.UnterminatedBlock, "scan\n  file a.stl\n"),
        (".th", DiagnosticCodes.UnterminatedBlock, "layout l\n  scale 1 500\n"),
        (".th", DiagnosticCodes.UnknownDataStyle, "centreline\ndata wobbly from to length\nendcentreline\n"),
        (".th", DiagnosticCodes.UnknownDataReading, "centreline\ndata normal from to lengthy compass clino\nendcentreline\n"),
        (".th", DiagnosticCodes.MissingFromTo, "centreline\ndata normal from length compass clino\nendcentreline\n"),
        (".th", DiagnosticCodes.MalformedUnits, "units length\n"),
        (".th", DiagnosticCodes.MalformedUnits, "units lengthy metres\n"),
        (".th", DiagnosticCodes.MalformedCalibrate, "calibrate length\n"),
        (".th", DiagnosticCodes.MalformedDeclination, "declination Inc degrees\n"),
        (".th", DiagnosticCodes.MalformedMeasurement, "grid-angle x\n"),
        (".th", DiagnosticCodes.MalformedMeasurement, "vthreshold\n"),
        (".th", DiagnosticCodes.MalformedSd, "sd length metres\n"),
        (".th", DiagnosticCodes.InvalidFlag, "centreline\nflags wobbly\nendcentreline\n"),
        (".th", DiagnosticCodes.MalformedFix, "fix p1 1 abc 3\n"),
        (".th", DiagnosticCodes.MalformedFix, "fix p1\n"),
        (".th", DiagnosticCodes.InvalidInferSpec, "infer plumbs maybe\n"),
        (".th", DiagnosticCodes.InvalidMarkType, "mark 1 shiny\n"),
        (".th", DiagnosticCodes.InvalidExtendSpec, "extend sideways\n"),
        (".th", DiagnosticCodes.InvalidStationFlag, "station 1 \"x\" wobbly\n"),
        (".th", DiagnosticCodes.UnknownCoordinateSystem, "cs NOT-A-CS\n"),
        (".th", DiagnosticCodes.IllegalIdentifier, "survey a!b\nendsurvey\n"),
        (".th2", DiagnosticCodes.BlockIdMismatch, "scrap s1\nendscrap s2\n"),
        (".th2", DiagnosticCodes.UnknownCommand, "scrap s1\nwobble 1 2\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2UnterminatedScrap, "scrap s1\npoint 1 2 station\n"),
        (".th2", DiagnosticCodes.IllegalIdentifier, "scrap s!1\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2MalformedPoint, "scrap s1\npoint 1 2\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2MalformedPoint, "scrap s1\npoint 1 y station\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2UnknownPointType, "scrap s1\npoint 1 2 wobbly\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2UnknownLineType, "scrap s1\nline wobbly\n1 1\nendline\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2MalformedLine, "scrap s1\nline wall\n1 1\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2UnknownAreaType, "scrap s1\narea wobbly\nl1\nendarea\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2MalformedArea, "scrap s1\narea water\nl1\nendscrap\n"),
        (".th2", DiagnosticCodes.Th2UnknownOption, "scrap s1\npoint 1 2 station -wobble 3\nendscrap\n"),
    }.Select(c => new object[] { c.Ext, c.Code, c.Text });

    [Theory]
    [MemberData(nameof(UpgradableCases))]
    public void Check_is_a_warning_in_lenient_mode_and_an_error_in_strict_mode(string ext, string code, string text)
    {
        var lenient = Parse(ext, text, Lenient).Diagnostics.Where(d => d.Code == code).ToList();
        var strict = Parse(ext, text, Strict).Diagnostics.Where(d => d.Code == code).ToList();
        Assert.NotEmpty(lenient);
        Assert.All(lenient, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Equal(lenient.Count, strict.Count);
        Assert.All(strict, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Empty(Problems(ext, text));
    }
}
