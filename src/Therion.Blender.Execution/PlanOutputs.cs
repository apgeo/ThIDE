// What a scene plan writes, in the terms the runner collects outputs by.

using Therion.Blender.Plan;

namespace Therion.Blender.Execution;

/// <summary>The outputs of scene plans, for <see cref="RenderJob"/>s that play them.</summary>
public static class PlanOutputs
{
    /// <summary>The folder name of the frames a render in two stages keeps beside its plan.</summary>
    public const string FramesFolderName = "frames";

    /// <summary>The output of <paramref name="plan"/> as the runner collects it, written into
    /// <paramref name="directory"/>.</summary>
    public static OutputSpec SpecOf(ScenePlan plan, string directory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new OutputSpec
        {
            Kind = plan.Output.Kind switch
            {
                PlanOutputKind.Video => OutputKind.Video,
                PlanOutputKind.Frames => OutputKind.FrameSequence,
                _ => OutputKind.Still,
            },
            Container = plan.Output.Container switch
            {
                PlanContainer.Mkv => VideoContainer.Mkv,
                PlanContainer.Webm => VideoContainer.WebM,
                _ => VideoContainer.Mp4,
            },
            Width = plan.Look.Width,
            Height = plan.Look.Height,
            BaseName = plan.Output.BaseName,
            OutputDirectory = directory,
        };
    }

    /// <summary>The output of a film as the runner collects it, written into
    /// <paramref name="directory"/>: the movie's, at the size of its segments.</summary>
    public static OutputSpec SpecOf(StoryboardPlan film, string directory)
    {
        ArgumentNullException.ThrowIfNull(film);
        return SpecOf(film.Segments[0] with { Output = film.Movie.Output }, directory);
    }

    /// <summary>A rough upper estimate of the bytes a film's render needs for the frames of all
    /// its segments, beside its output.</summary>
    public static long FrameBytes(StoryboardPlan film)
    {
        ArgumentNullException.ThrowIfNull(film);
        return film.Segments.Sum(FrameBytes);
    }

    /// <summary>The frames the first of two stages writes into <paramref name="framesDirectory"/>,
    /// as the runner collects them: <c>frame_0001.png</c> onwards.</summary>
    public static OutputSpec FramesOf(ScenePlan plan, string framesDirectory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new OutputSpec
        {
            Kind = OutputKind.FrameSequence,
            Width = plan.Look.Width,
            Height = plan.Look.Height,
            BaseName = "frame_",
            OutputDirectory = framesDirectory,
        };
    }

    /// <summary>How many frames the first of two stages renders: the scene's, or one for a still.</summary>
    public static int StageFrameCount(ScenePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Output.Kind == PlanOutputKind.Still ? 1 : plan.Timeline.FrameCount;
    }

    /// <summary>A rough upper estimate of the bytes a render of <paramref name="plan"/> in two
    /// stages needs for its frames, beside its output.</summary>
    public static long FrameBytes(ScenePlan plan)
        => DiskPreflight.EstimateBytes(StageFrameCount(plan), plan.Look.Width, plan.Look.Height, OutputKind.FrameSequence);
}
