using Therion.Workspace.Import;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>
/// Scaffolding a project into a folder that already holds a project must not silently replace
/// the files the user edited there (the thconfig, the wrapper survey and the copied survey).
/// </summary>
public class ScaffoldProjectOverwriteTests
{
    [Fact]
    public async Task Scaffold_into_existing_project_keeps_the_edited_thconfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "thide-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourceDir = Path.Combine(root, "incoming");
        Directory.CreateDirectory(sourceDir);
        try
        {
            var source = Path.Combine(sourceDir, "demo.th");
            File.WriteAllText(source, "survey demo\n  centerline\n    data normal from to length compass clino\n    1 2 1.0 0 0\n  endcenterline\nendsurvey\n");

            // The user's hand-edited thconfig from an earlier scaffold of the same survey.
            var thconfig = Path.Combine(root, "thconfig.thc");
            const string edited = "# keep: user layouts and extra exports live here\n";
            File.WriteAllText(thconfig, edited);

            var vm = new MainWindowViewModel();
            var options = new ScaffoldOptions
            {
                ProjectName = "demo",
                InnerSurveyName = "demo",
                SourceFileName = "demo.th",
            };

            var ok = await vm.RunScaffoldAsync(options, source, root);

            var now = File.ReadAllText(thconfig);
            Assert.True(now == edited,
                "the existing thconfig.thc was overwritten by the scaffold instead of being refused; "
                + $"RunScaffoldAsync returned {ok}, file now starts with: {now.Split('\n')[0]}");
            Assert.False(ok, "scaffolding over an existing thconfig.thc should be refused");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
