// Outcome of a render job (BA-B1 scaffold; filled in by BA-B10 runner, extended by BA-B11).
//
// Returned by IBlenderRenderService.RenderAsync and BlenderRunner.RunAsync. Carries the
// success/failure verdict + taxonomy, the collected outputs, the device actually used, the
// job.log path for postmortem, timing, and any non-fatal warnings the script surfaced.

using System.Collections.Immutable;

namespace Therion.Blender;

/// <summary>Why a render job did not succeed (or <see cref="None"/> when it did).</summary>
public enum RenderFailureKind
{
    None,
    /// <summary>No usable Blender was located.</summary>
    BlenderNotFound,
    /// <summary>The located Blender is older than the supported minimum.</summary>
    BlenderTooOld,
    /// <summary>The generated script reported an error (<c>THIDE:error</c> / exit 64).</summary>
    ScriptError,
    /// <summary>Blender exited abnormally without finishing (nonzero exit, no done marker).</summary>
    Crashed,
    /// <summary>The job was cancelled by the caller.</summary>
    Cancelled,
    /// <summary>The disk preflight estimated insufficient free space.</summary>
    DiskSpace,
    /// <summary>Blender reported success but no output files were found on disk (BA-B11).</summary>
    NoOutput,
    /// <summary>The render cannot be made with the scene builder that was asked for: the player
    /// does not carry the scene, or the script generator cannot do what was asked.
    /// <see cref="RenderResult.NotCarried"/> names what the player does not carry.</summary>
    BuilderRefused,
}

/// <summary>Result of a render job.</summary>
public sealed class RenderResult
{
    /// <summary>Whether the job produced its expected outputs.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Failure category (<see cref="RenderFailureKind.None"/> on success).</summary>
    public RenderFailureKind FailureKind { get; init; }

    /// <summary>Human-facing failure detail (developer text; the UI localizes surroundings).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The Blender executable that could not be started (<see cref="RenderFailureKind.Crashed"/> before the
    /// process ran); null for every other outcome.</summary>
    public string? StartFailedPath { get; init; }

    /// <summary>Paths of the produced output files (video/frames/stills); BA-B11 verifies them.</summary>
    public ImmutableArray<string> OutputPaths { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>Render device actually used (e.g. "OPTIX", "CPU", "EEVEE"), once reported.</summary>
    public string? Device { get; init; }

    /// <summary>Frames the job rendered, once known.</summary>
    public int? FrameCount { get; init; }

    /// <summary>Path to the job's full output log, for the error dialog / bell.</summary>
    public string? JobLogPath { get; init; }

    /// <summary>Wall-clock duration of the render.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Non-fatal notices the script surfaced (label caps, GPU fallback, …).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>What built the scene, the player or the script generator; null when no scene
    /// was built, or when whoever made the result does not say.</summary>
    public SceneBuilder? Builder { get; init; }

    /// <summary>The settings of the spec the player does not carry, as field paths, when they
    /// decided the builder: with <see cref="SceneBuilder.Auto"/> they are why the script
    /// generator built the scene, with <see cref="SceneBuilder.Player"/> why the render was
    /// refused (<see cref="RenderFailureKind.BuilderRefused"/>). Empty otherwise.</summary>
    public IReadOnlyList<string> NotCarried { get; init; } = [];

    /// <summary>The settings of the spec the render does not show, as field paths: texts only
    /// the player draws when the script generator built the scene; what the player does not
    /// carry when it rendered a draft all the same.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];

    /// <summary>The folder with the frames of a render in two stages, when they are still
    /// there: they were kept, or the render did not finish and can go on from them.</summary>
    public string? FramesDirectory { get; init; }
}
