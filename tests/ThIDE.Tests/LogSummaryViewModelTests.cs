using System.Linq;
using Therion.Build;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class LogSummaryViewModelTests
{
    private const string SuccessLog = """
therion 6.3.3 (2025-01-06)
  - using Proj 9.4.1, compiled against 9.4.1
configuration file: C:\caves\grind\thconfig.thconfig
reading ... done
output coordinate system: UTM35
meridian convergence (deg): 1.2788
geomag declinations (deg):
  2026.1.1  6.3976
average loop error: 6.46%
####################### cavern log file ########################
 1> Survex 1.4.22
 4> Survey contains 72267 survey stations, joined by 72283 shots.
 6> Total length of survey shots = 3134.02m (3129.85m adjusted)
#################### end of cavern log file ####################
writing rez\grind.lox ... done
compilation time: 24 sec
""";

    private static LogSummaryViewModel Build(string text) =>
        LogSummaryViewModel.Build(TherionLogParser.Parse(text));

    [Fact]
    public void Groups_the_extracted_fields_into_sections()
    {
        var vm = Build(SuccessLog);
        var titles = vm.Sections.Select(s => s.Title).ToList();

        Assert.Contains("Summary", titles);
        Assert.Contains("Versions", titles);
        Assert.Contains("Coordinate system", titles);
        Assert.Contains("Survey statistics", titles);
        Assert.Contains("Output files", titles);
        // Nothing failed, so the diagnostics section is omitted entirely rather than shown empty.
        Assert.DoesNotContain("Errors and warnings", titles);
        Assert.All(vm.Sections, s => Assert.NotEmpty(s.Fields));
    }

    [Fact]
    public void Formats_values_invariantly_and_copies_each_one()
    {
        var stats = vm_section(Build(SuccessLog), "Survey statistics");

        Assert.Equal("72,267", Field(stats, "Stations").Value);
        Assert.Equal("3134.02 m", Field(stats, "Total length").Value);
        Assert.Equal("3129.85 m", Field(stats, "Total length (adjusted)").Value);
        Assert.Equal("6.46%", Field(stats, "Average loop error").Value);
        // Every row copies its own displayed value by default.
        Assert.All(stats.Fields, f => Assert.False(string.IsNullOrEmpty(f.CopyText)));
    }

    [Fact]
    public void Copy_section_and_copy_all_produce_label_value_text()
    {
        var vm = Build(SuccessLog);
        var versions = vm_section(vm, "Versions");

        var text = versions.ToText();
        Assert.StartsWith("Versions", text);
        Assert.Contains("Therion version: 6.3.3", text);
        Assert.Contains("Survex (cavern): 1.4.22", text);
    }

    [Fact]
    public void A_failed_run_surfaces_its_diagnostics_and_the_stage_it_stopped_in()
    {
        var vm = Build("""
therion 6.3.3 (2025-01-06)
configuration file: D:\caves\cave.th
reading ...
C:\Program Files\Therion\therion.exe: error -- D:\caves\cave.th [7] -- unknown configuration command -- survey
Press ENTER to exit!
""");

        Assert.Equal(TherionLogOutcome.Failed, vm.Log.Outcome);
        Assert.Equal("Compilation failed", vm.OutcomeText);

        var summary = vm_section(vm, "Summary");
        Assert.Equal("reading", Field(summary, "Stopped at").Value);
        Assert.Equal("1", Field(summary, "Errors").Value);

        var diag = vm_section(vm, "Errors and warnings").Fields.Single();
        Assert.Equal("Error", diag.Label);
        Assert.Contains("unknown configuration command", diag.Value);
        Assert.Contains("cave.th [7]", diag.Value);
        // The raw log line is what lands on the clipboard, so it can be pasted back verbatim.
        Assert.StartsWith(@"C:\Program Files\Therion\therion.exe: error --", diag.CopyText);
    }

    private static LogSectionViewModel vm_section(LogSummaryViewModel vm, string title) =>
        vm.Sections.Single(s => s.Title == title);

    private static LogFieldViewModel Field(LogSectionViewModel section, string label) =>
        section.Fields.Single(f => f.Label == label);
}
