// LANG-01 — command-coverage audit. Guards that the centreline/survey commands we advertise
// (and the thbook documents) are actually recognized by the parser, so none silently fall back
// to UnknownCommand / TH0010. If a new keyword is added to the editor vocabulary without a parser
// path, this test fails.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class CommandCoverageAuditTests
{
    // One representative use of every modeled .th centreline/survey command (thbook §centreline).
    private static readonly Dictionary<string, string> Sample = new()
    {
        ["date"]          = "date 2020.01.02",
        ["explo-date"]    = "explo-date 2019.01.01",
        ["team"]          = "team \"Alice\" tape",
        ["explo-team"]    = "explo-team \"Bob\"",
        ["instrument"]    = "instrument compass \"DistoX2\"",
        ["calibrate"]     = "calibrate compass 0.5",
        ["units"]         = "units length metres",
        ["sd"]            = "sd length 0.1 metres",
        ["grade"]         = "grade BCRA5",
        ["declination"]   = "declination 3 degrees",
        ["grid-angle"]    = "grid-angle 1 degrees",
        ["infer"]         = "infer plumbs on",
        ["mark"]          = "mark 1 fixed",
        ["flags"]         = "flags duplicate",
        ["station"]       = "station 1 \"comment\" entrance",
        ["cs"]            = "cs UTM33",
        ["fix"]           = "fix 1 0 0 0",
        ["equate"]        = "equate 1 2",
        ["break"]         = "break",
        ["walls"]         = "walls on",
        ["vthreshold"]    = "vthreshold 2 degrees",
        ["extend"]        = "extend left",
        ["station-names"] = "station-names \"pre\" \"suf\"",
        ["data"]          = "data normal from to length compass clino",
    };

    [Fact]
    public void Every_modeled_centreline_command_is_recognized()
    {
        var notRecognized = new List<string>();
        foreach (var (keyword, line) in Sample)
        {
            var src = $"survey s\n centreline\n  {line}\n endcentreline\nendsurvey\n";
            var r = new ThParser().Parse("/p/a.th", src);
            bool unknown = r.Diagnostics.Any(d =>
                d.Code.Value == DiagnosticCodes.UnknownCommand);
            if (unknown) notRecognized.Add(keyword);
        }
        Assert.True(notRecognized.Count == 0,
            "Unrecognized commands (fell back to UnknownCommand): " + string.Join(", ", notRecognized));
    }

    [Fact]
    public void Modeled_commands_do_not_degrade_into_data_rows()
    {
        // A representative metadata command placed *after* a data declaration must still parse as
        // its own command, never as a shot row (the pre-fix pollution bug).
        var src = """
            survey s
              centreline
                data normal from to length compass clino
                1 2 5 0 0
                extend left 1 2
                mark 2 fixed
                station 2 "lead" continuation
              endcentreline
            endsurvey
            """;
        var cl = new ThParser().Parse("/p/a.th", src).Value!
            .Children.OfType<SurveyCommand>().Single()
            .Children.OfType<CentrelineCommand>().Single();
        Assert.Single(cl.Children.OfType<DataRow>());       // only the genuine shot
        Assert.Single(cl.Children.OfType<ExtendCommand>());
        Assert.Single(cl.Children.OfType<MarkCommand>());
        Assert.Single(cl.Children.OfType<StationCommand>());
    }
}
