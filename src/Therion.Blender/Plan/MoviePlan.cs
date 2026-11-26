// The plan of a movie of several scenes. It is a plan as the player reads one (version, features,
// frame, timeline) with three sections and no scene of its own: its segments name the plans of
// the scenes, each an ordinary scene plan beside it; its texts lie over the whole movie, in the
// movie's time; its output is the movie's. The player renders every segment's frames in a Blender
// of its own and joins them in its second stage (the module "segments").
//
// A StoryboardPlanner writes one, with the segments' plans, from a Storyboard. It is resolved
// data like a scene plan: times in seconds, nothing left to work out.

using System.Text.Json.Serialization;

namespace Therion.Blender.Plan;

/// <summary>How a segment of a movie begins.</summary>
public enum PlanTransitionKind
{
    /// <summary>On the frame after the last of the segment before.</summary>
    Cut,

    /// <summary>As a cut, with a colour that comes over the last frames of the segment before
    /// and goes from the first frames of this one.</summary>
    Dip,

    /// <summary>The two segments overlap, and the picture goes evenly from the one to the other.</summary>
    Dissolve,
}

/// <summary>What joins a segment to the one before it.</summary>
public sealed record PlanTransition
{
    public PlanTransitionKind Kind { get; init; } = PlanTransitionKind.Cut;

    /// <summary>How long a dip (both halves together) or a dissolve takes; a cut has none.</summary>
    public double? Seconds { get; init; }

    /// <summary>The colour of a dip; null is black.</summary>
    public ColorRgb? Color { get; init; }
}

/// <summary>One scene of a movie: the plan it is rendered from and where its frames go.</summary>
public sealed record PlanSegment
{
    /// <summary>Unique in the movie ("s01").</summary>
    public string Id { get; init; } = "";

    /// <summary>What the segment is called in the progress of the render.</summary>
    public string? Name { get; init; }

    /// <summary>The plan of the segment's scene, relative to the movie's plan.</summary>
    public string Plan { get; init; } = "";

    /// <summary>The folder of the segment's frames, inside the frames folder of the movie.</summary>
    public string Frames { get; init; } = "";

    /// <summary>When the segment's first frame shows, in seconds of the movie.</summary>
    public double Start { get; init; }

    public int FrameCount { get; init; }

    /// <summary>How the segment begins; null is a cut.</summary>
    public PlanTransition? In { get; init; }
}

/// <summary>The plan of a movie of several scenes: see the file header.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MoviePlan
{
    /// <summary>The module that renders the segments and joins their frames
    /// (<see cref="Segments"/>).</summary>
    public const string SegmentsFeature = "segments";

    /// <summary>The modules a movie's plan needs, in the order they run.</summary>
    public static readonly IReadOnlyList<string> MovieFeatures = [SegmentsFeature, ScenePlan.TextsFeature, "output"];

    /// <summary>The name of the plan of the segment <paramref name="id"/> beside the movie's
    /// plan, and of its draft.</summary>
    public static string SegmentFileName(string id, bool draft = false)
        => $"scene-plan.{id}{(draft ? ".draft" : "")}.json";

    public int Version { get; init; } = BlenderModule.ScenePlanSchemaVersion;

    /// <summary>What wrote the plan.</summary>
    public string Generator { get; init; } = "ThIDE Blender module";

    public string? Name { get; init; }

    public IReadOnlyList<string> Features { get; init; } = MovieFeatures;

    /// <summary>The frame of the segments' scenes; the movie itself has no positions.</summary>
    public PlanFrame Frame { get; init; } = new();

    /// <summary>The movie's length: from the first frame of the first segment to the last of
    /// the last one.</summary>
    public PlanTimeline Timeline { get; init; } = new();

    public IReadOnlyList<PlanSegment> Segments { get; init; } = [];

    /// <summary>What is written over the movie, in the movie's time.</summary>
    public PlanScreenTexts? Texts { get; init; }

    public PlanOutput Output { get; init; } = new();

    /// <summary>The frames of the finished movie: the segments', and those the last one is
    /// held for (<see cref="PlanScreenTexts.Hold"/>).</summary>
    [JsonIgnore]
    public int OutputFrameCount => Timeline.FrameCount + (Texts is { } texts ? (int)Math.Round(texts.Hold * Timeline.Fps) : 0);

    /// <summary>The first frame of every segment in the movie (the movie's first is 1), and the
    /// frames it shares with the segment before it: those of a dissolve.</summary>
    public IReadOnlyList<(int First, int Overlap)> Placed()
    {
        var placed = new List<(int, int)>(Segments.Count);
        foreach (var segment in Segments)
        {
            int overlap = segment.In is { Kind: PlanTransitionKind.Dissolve, Seconds: { } seconds }
                ? ScenePlanValidator.FrameAt(seconds, Timeline.Fps) - 1
                : 0;
            placed.Add((ScenePlanValidator.FrameAt(segment.Start, Timeline.Fps), overlap));
        }
        return placed;
    }
}
