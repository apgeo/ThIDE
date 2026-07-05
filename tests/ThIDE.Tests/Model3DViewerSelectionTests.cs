// artifact selection for the 3D viewer: .lox is preferred over .3d, the newest of a
// kind wins, staleness is carried through, and an empty set yields nothing.

using System;
using System.Collections.Generic;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class Model3DViewerSelectionTests
{
    private static ArtifactRow Row(string path, int ageMinutes, bool stale = false) =>
        new(path, "model", 100, DateTimeOffset.UtcNow.AddMinutes(-ageMinutes)) { IsStale = stale };

    [Fact]
    public void Prefers_lox_over_newer_3d()
    {
        var rows = new List<ArtifactRow>
        {
            Row("/out/cave.3d", ageMinutes: 0),   // newest overall, but .3d
            Row("/out/cave.lox", ageMinutes: 5),  // older, but the preferred format
        };
        var best = Model3DViewerViewModel.PickBestModel(rows);
        Assert.NotNull(best);
        Assert.EndsWith(".lox", best!.Path);
    }

    [Fact]
    public void Picks_newest_when_multiple_lox()
    {
        var rows = new List<ArtifactRow>
        {
            Row("/out/old.lox", ageMinutes: 30),
            Row("/out/new.lox", ageMinutes: 1),
        };
        Assert.Equal("/out/new.lox", Model3DViewerViewModel.PickBestModel(rows)!.Path);
    }

    [Fact]
    public void Falls_back_to_3d_when_no_lox()
    {
        var rows = new List<ArtifactRow> { Row("/out/cave.3d", 2), Row("/out/map.pdf", 0) };
        Assert.Equal("/out/cave.3d", Model3DViewerViewModel.PickBestModel(rows)!.Path);
    }

    [Fact]
    public void Carries_stale_flag_from_chosen_artifact()
    {
        var rows = new List<ArtifactRow> { Row("/out/cave.lox", 0, stale: true) };
        Assert.True(Model3DViewerViewModel.PickBestModel(rows)!.IsStale);
    }

    [Fact]
    public void Returns_null_when_no_model_artifacts()
    {
        var rows = new List<ArtifactRow> { Row("/out/map.pdf", 0), Row("/out/notes.svg", 1) };
        Assert.Null(Model3DViewerViewModel.PickBestModel(rows));
    }
}
