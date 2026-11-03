using System;
using System.IO;
using Therion.Workspace.Import;

namespace Therion.Workspace.Tests;

// Scaffolding into a folder that already holds a project must not overwrite what is there.
public class ScaffoldExistingTargetsTests
{
    private static ScaffoldOptions Options() => new()
    {
        ProjectName = "demo",
        InnerSurveyName = "demo",
        SourceFileName = "demo.th",
    };

    [Fact]
    public void Existing_generated_files_and_survey_copy_are_reported()
    {
        var root = Path.Combine(Path.GetTempPath(), "thide-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = TopodroidProjectScaffold.BuildPlan(Options());
            var source = Path.Combine(root, "incoming", "demo.th");
            Assert.Empty(TopodroidProjectScaffold.ExistingTargets(plan, root, new[] { source }));

            File.WriteAllText(Path.Combine(root, plan.ThconfigRelativePath), "# edited\n");
            var copy = Path.Combine(root, plan.SourceCopyRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.WriteAllText(copy, "# edited survey\n");

            var hits = TopodroidProjectScaffold.ExistingTargets(plan, root, new[] { source });
            Assert.Contains(plan.ThconfigRelativePath, hits);
            Assert.Contains(plan.SourceCopyRelativePath, hits);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // Scaffolding around a survey already at its copy destination copies nothing, so it is no clash.
    [Fact]
    public void A_survey_already_in_place_is_not_an_existing_target()
    {
        var root = Path.Combine(Path.GetTempPath(), "thide-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = TopodroidProjectScaffold.BuildPlan(Options());
            var inPlace = Path.Combine(root, plan.SourceCopyRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(inPlace)!);
            File.WriteAllText(inPlace, "survey demo\nendsurvey\n");

            Assert.Empty(TopodroidProjectScaffold.ExistingTargets(plan, root, new[] { inPlace }));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
