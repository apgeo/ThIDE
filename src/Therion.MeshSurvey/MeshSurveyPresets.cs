// Named starting points for the settings. Each changes only what makes it what it is; everything else
// keeps its default, and any setting can still be changed after the preset is applied.

namespace Therion.MeshSurvey;

public static class MeshSurveyPresets
{
    /// <summary>The names, in the order a user would meet them.</summary>
    public static readonly string[] Names = ["scan-backbone", "mainline-only", "main-and-major-laterals", "thorough", "synthetic-survey"];

    /// <summary>The preset's settings, or null for an unknown name.</summary>
    public static MeshSurveyOptions? Get(string name) => name.Trim().ToLowerInvariant() switch
    {
        // A scan turned into a survey backbone: axial stations, ring splays, coverage 90 %.
        "scan-backbone" or "default" => new MeshSurveyOptions(),
        // One line from the start to the farthest end; no laterals, no loops.
        "mainline-only" => new MeshSurveyOptions
        {
            Laterals = new LateralOptions { MaxDepth = 0, SideStations = false, Loops = LoopPolicy.Never },
        },
        // The mainline and the galleries that leave it, if they are long and wide enough.
        "main-and-major-laterals" => new MeshSurveyOptions
        {
            Laterals = new LateralOptions { MaxDepth = 1, MinLength = 10, MinWidth = 1.0, SideStations = false },
        },
        // As much as can be seen: every level of lateral, side stations, a high target.
        "thorough" => new MeshSurveyOptions
        {
            Laterals = new LateralOptions { MinLength = 2, MinWidth = 0.4, MinEfficiency = 0.1 },
            Coverage = new CoverageOptions { Target = 0.98, MinFeature = 0.7 },
            Splays = new SplayOptions { Mode = SplayMode.Adaptive, Count = 24 },
        },
        // What a team would have brought back: stations on the floor, a DistoX-style ring, instrument error.
        "synthetic-survey" => new MeshSurveyOptions
        {
            Anchor = new AnchorOptions { Mode = AnchorMode.Floor },
            Legs = new LegOptions { Preferred = 6, Max = 20, Strategy = LegStrategy.Preferred, FeatureWeight = 0.8 },
            Splays = new SplayOptions { Mode = SplayMode.Ring, Count = 6, ForeBack = true },
            Readings = new ReadingOptions { Noise = new NoiseOptions() },
        },
        _ => null,
    };
}
