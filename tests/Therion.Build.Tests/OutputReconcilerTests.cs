using Therion.Build;

namespace Therion.Build.Tests;

public class OutputReconcilerTests
{
    [Fact]
    public void Splits_expected_outputs_into_produced_and_missing_and_lists_the_rest()
    {
        var logged = new[]
        {
            new TherionLogOutputFile("rez/cave.lox", true),
            new TherionLogOutputFile("rez/cave.3d", true),
            new TherionLogOutputFile("rez/cave-plan.pdf", true),
            new TherionLogOutputFile("rez/cave.sql", true),
        };

        var r = OutputReconciler.Reconcile(new[] { "rez/cave.lox", "rez/cave.3d", "rez/cave.kml", "rez/cave-plan.pdf" }, logged);

        Assert.Equal(new[] { "rez/cave.lox", "rez/cave.3d", "rez/cave-plan.pdf" }, r.Produced);
        Assert.Equal(new[] { "rez/cave.kml" }, r.Missing);
        Assert.Equal(new[] { "rez/cave.sql" }, r.Unexpected);
        Assert.False(r.AllProduced);
    }

    [Fact]
    public void A_write_the_log_never_finished_is_missing()
    {
        var r = OutputReconciler.Reconcile(new[] { "rez/cave-plan.pdf" },
            new[] { new TherionLogOutputFile("rez/cave-plan.pdf", false) });

        Assert.Empty(r.Produced);
        Assert.Equal(new[] { "rez/cave-plan.pdf" }, r.Missing);
        Assert.Empty(r.Unexpected);
    }

    [Fact]
    public void Separators_and_a_leading_dot_slash_do_not_matter()
    {
        var r = OutputReconciler.Reconcile(new[] { "./rez/cave.lox" },
            new[] { new TherionLogOutputFile(@"rez\cave.lox", true) });

        Assert.Equal(new[] { "./rez/cave.lox" }, r.Produced);
        Assert.True(r.AllProduced);
        Assert.Empty(r.Unexpected);
    }

    [Fact]
    public void Reconciles_with_a_parsed_log()
    {
        const string log = """
            writing rez/cave.lox ...
            processing projection plan ... done
             done
            writing rez/cave.3d ... done
            compilation time: 1 sec
            """;
        var summary = TherionLogParser.Parse(log);

        var r = OutputReconciler.Reconcile(new[] { "rez/cave.lox", "rez/cave.3d" }, summary.OutputFiles);

        Assert.True(r.AllProduced);
    }
    // Given the configuration by a full path, Therion logs full output paths.
    [Fact]
    public void Full_logged_paths_are_compared_relative_to_the_working_directory()
    {
        var work = Path.Combine(Path.GetTempPath(), "project");
        var logged = new[]
        {
            new TherionLogOutputFile(Path.Combine(work, "rez", "cave.lox"), true),
            new TherionLogOutputFile(Path.Combine(work, "rez", "cave.sql"), true),
        };

        var r = OutputReconciler.Reconcile(new[] { "rez/cave.lox" }, logged, work);

        Assert.Equal(new[] { "rez/cave.lox" }, r.Produced);
        Assert.Equal(new[] { "rez/cave.sql" }, r.Unexpected);
    }

    [Fact]
    public void A_full_path_outside_the_working_directory_is_kept_as_logged()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "cave.lox");
        var r = OutputReconciler.Reconcile(new[] { "rez/cave.lox" },
            new[] { new TherionLogOutputFile(outside, true) }, Path.Combine(Path.GetTempPath(), "project"));

        Assert.Equal(new[] { "rez/cave.lox" }, r.Missing);
        Assert.Equal(new[] { outside }, r.Unexpected);
    }
}
