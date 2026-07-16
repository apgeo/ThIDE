// .lox parser tests against the real Therion exports committed in tests/Corpus:
// av_cerbul_de_aur (small, has walls) and grind2025 (21 MB big-file smoke). These are
// the BA-B2 "real workspace .lox round-trips" done-when checks: parse → rewrite →
// reparse must reproduce the model exactly, and the extracted content must look like
// the cave the export came from (station labels, survey tree, UTM-scale coordinates).

using Therion.Blender;
using Therion.Blender.Parsing;

namespace Therion.Blender.Tests;

public class LoxRealFileTests
{
    [CorpusFact]
    public void AvCerbul_ParsesWithAllRecordKinds()
    {
        var model = LoxReader.ReadFile(TestCorpus.AvCerbulLox());

        Assert.Equal(CaveSourceFormat.Lox, model.SourceFormat);
        Assert.True(model.Surveys.Count > 0, "expected a survey tree");
        Assert.True(model.Stations.Count > 10, $"expected a real station list, found {model.Stations.Count}");
        Assert.True(model.Shots.Count > 10, $"expected a real shot list, found {model.Shots.Count}");
        Assert.True(model.HasWalls, "this export is known to carry wall geometry");

        // Every shot must reference existing stations (internal consistency).
        var ids = model.Stations.Select(s => s.Id).ToHashSet();
        Assert.All(model.Shots, shot =>
        {
            Assert.Contains(shot.FromStationId!.Value, ids);
            Assert.Contains(shot.ToStationId!.Value, ids);
        });

        // Survey parents must exist within the tree.
        var surveyIds = model.Surveys.Select(s => s.Id).ToHashSet();
        Assert.All(model.Surveys, survey => Assert.Contains(survey.ParentId, surveyIds));

        // The cave has an entrance and plausible non-degenerate coordinates.
        Assert.Contains(model.Stations, s => s.IsEntrance);
        var xs = model.Stations.Select(s => s.Position.X).ToList();
        Assert.True(xs.Max() - xs.Min() > 1.0, "cave should span more than a metre");
    }

    [CorpusFact]
    public void AvCerbul_RoundTripsExactly()
    {
        var first = LoxReader.ReadFile(TestCorpus.AvCerbulLox());

        var rewritten = LoxWriter.Write(first);
        var second = LoxReader.Read(rewritten);

        TestCorpus.AssertModelsEqual(first, second);
    }

    [CorpusFact]
    public void Grind_BigFile_ParsesCompletely()
    {
        var model = LoxReader.ReadFile(TestCorpus.GrindLox());

        Assert.True(model.Stations.Count > 100, $"found {model.Stations.Count} stations");
        Assert.True(model.Shots.Count > 100, $"found {model.Shots.Count} shots");
        Assert.True(model.HasWalls);

        // 21 MB of scrap geometry: triangles must all reference valid vertices
        // (LoxReader validates this during parsing — this asserts we really did
        // traverse wall data rather than skip it).
        Assert.True(model.Scraps.Sum(s => (long)s.Triangles.Count) > 1000, "expected substantial wall geometry");
    }
}
