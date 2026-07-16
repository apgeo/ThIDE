// Shared fixture helpers: locate the real-world corpus (.lox/.3d exports under
// tests/Corpus/sample_projects, which is gitignored — local-only, absent on CI) and
// deep-compare CaveModels (record equality alone won't do — records holding
// IReadOnlyList members compare list references, not elements).

using Therion.Blender;

namespace Therion.Blender.Tests;

internal static class TestCorpus
{
    /// <summary>Walks up from the test output dir to tests/Corpus (same convention as
    /// the other corpus-backed test projects).</summary>
    public static string? LocateCorpusRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Whether the gitignored real-world corpus is checked out here. False on CI, where
    /// only tests/Corpus/Synthetic is committed — [CorpusFact] skips on that.</summary>
    public static bool SampleProjectsAvailable => SampleProjectsRoot() is not null;

    private static string? SampleProjectsRoot()
    {
        if (LocateCorpusRoot() is not { } root) return null;
        var samples = Path.Combine(root, "sample_projects");
        return Directory.Exists(samples) && Directory.EnumerateFileSystemEntries(samples).Any()
            ? samples
            : null;
    }

    public static string RequireCorpusFile(params string[] relativeParts)
    {
        var root = LocateCorpusRoot();
        Assert.True(root is not null, "tests/Corpus not found above the test output directory.");
        var path = Path.Combine([root!, .. relativeParts]);
        Assert.True(File.Exists(path), $"Expected corpus file missing: {path}");
        return path;
    }

    /// <summary>The small real-world export that exists in BOTH formats (same cave,
    /// same Therion run) — the cross-format ground truth.</summary>
    public static string AvCerbulLox()
        => RequireCorpusFile("sample_projects", "av_cerbul_de_aur", "therion", "rez", "av_cerbul_de_aur_20260505_v1.lox");

    public static string AvCerbul3d()
        => RequireCorpusFile("sample_projects", "av_cerbul_de_aur", "therion", "rez", "av_cerbul_de_aur_20260505_v1.3d");

    /// <summary>The large (21 MB) real-world .lox used as the big-file smoke test.</summary>
    public static string GrindLox()
        => RequireCorpusFile("sample_projects", "grind", "therion", "rez", "grind2025_v1_aven.lox");

    public static void AssertModelsEqual(CaveModel expected, CaveModel actual)
    {
        Assert.Equal(expected.SourceFormat, actual.SourceFormat);
        Assert.Equal(expected.Surveys, actual.Surveys);
        Assert.Equal(expected.Stations, actual.Stations);
        Assert.Equal(expected.Shots, actual.Shots);

        Assert.Equal(expected.Scraps.Count, actual.Scraps.Count);
        for (int i = 0; i < expected.Scraps.Count; i++)
        {
            Assert.Equal(expected.Scraps[i].Id, actual.Scraps[i].Id);
            Assert.Equal(expected.Scraps[i].SurveyId, actual.Scraps[i].SurveyId);
            Assert.Equal(expected.Scraps[i].Points, actual.Scraps[i].Points);
            Assert.Equal(expected.Scraps[i].Triangles, actual.Scraps[i].Triangles);
        }

        Assert.Equal(expected.Surfaces.Count, actual.Surfaces.Count);
        for (int i = 0; i < expected.Surfaces.Count; i++)
        {
            Assert.Equal(expected.Surfaces[i].Id, actual.Surfaces[i].Id);
            Assert.Equal(expected.Surfaces[i].Width, actual.Surfaces[i].Width);
            Assert.Equal(expected.Surfaces[i].Height, actual.Surfaces[i].Height);
            Assert.Equal(expected.Surfaces[i].Calibration, actual.Surfaces[i].Calibration);
            Assert.Equal(expected.Surfaces[i].Heights, actual.Surfaces[i].Heights);
        }

        Assert.Equal(expected.SurfaceBitmaps.Count, actual.SurfaceBitmaps.Count);
        for (int i = 0; i < expected.SurfaceBitmaps.Count; i++)
        {
            Assert.Equal(expected.SurfaceBitmaps[i].SurfaceId, actual.SurfaceBitmaps[i].SurfaceId);
            Assert.Equal(expected.SurfaceBitmaps[i].Type, actual.SurfaceBitmaps[i].Type);
            Assert.Equal(expected.SurfaceBitmaps[i].Calibration, actual.SurfaceBitmaps[i].Calibration);
            Assert.Equal(expected.SurfaceBitmaps[i].Data, actual.SurfaceBitmaps[i].Data);
        }

        Assert.Equal(expected.Passages.Count, actual.Passages.Count);
        for (int i = 0; i < expected.Passages.Count; i++)
            Assert.Equal(expected.Passages[i].Stations, actual.Passages[i].Stations);

        Assert.Equal(expected.TraverseErrors, actual.TraverseErrors);
    }
}
