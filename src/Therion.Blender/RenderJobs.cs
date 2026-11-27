// Render jobs as records: what was asked, where it stands, how far it got and how it ended. A
// queue of renders (one running at a time) and a render worker that takes bundles from a folder
// keep these, in a small JSON file, so that a render that was cut short by a restart is known and
// can go on. Plain data and a serializer; nothing here starts a process.

using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.Blender.Sources;

namespace Therion.Blender;

/// <summary>Where a render job stands. A job is queued, then running, then one of the three ends.</summary>
public enum RenderJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>What a render job was asked to make: enough to start it again after a restart.</summary>
public sealed record RenderJobRequest
{
    /// <summary>The name the job is listed under.</summary>
    public string Name { get; init; } = "";

    /// <summary>Who asked: "panel", "agent", "worker", or a name of the caller's.</summary>
    public string? Owner { get; init; }

    /// <summary>The scene, as <see cref="SceneSpecSerializer"/> writes it; null for a film or a bundle.</summary>
    public string? Spec { get; init; }

    /// <summary>The film, as <see cref="StoryboardSerializer"/> writes it; null for a scene or a bundle.</summary>
    public string? Storyboard { get; init; }

    /// <summary>The render bundle the job plays (a render worker's): its file name.</summary>
    public string? Bundle { get; init; }

    /// <summary>The model file, for a scene or a film.</summary>
    public string? Model { get; init; }

    /// <summary>The leads marked in the model.</summary>
    public IReadOnlyList<SourceLead> Leads { get; init; } = [];

    public bool Draft { get; init; }

    public bool KeepFrames { get; init; }

    /// <summary>Where the frames are kept, so that the job goes on with them.</summary>
    public string? FramesDirectory { get; init; }
}

/// <summary>How far a running job has got.</summary>
public sealed record RenderJobProgress
{
    /// <summary>The phase: the camel-case name of a <see cref="RenderPhase"/>.</summary>
    public string Phase { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>The share of the job that is done, 0 to 1, when known.</summary>
    public double? Fraction { get; init; }

    /// <summary>Which segment (shot) of a film, from 1, and of how many.</summary>
    public int? Segment { get; init; }

    public int? SegmentCount { get; init; }

    /// <summary>The frame being rendered, and of how many (of the segment, in a film).</summary>
    public int? Frame { get; init; }

    public int? FrameCount { get; init; }

    public double? SecondsPerFrame { get; init; }

    public double? SecondsLeft { get; init; }

    /// <summary>A progress tick as a record.</summary>
    public static RenderJobProgress From(RenderProgress tick) => new()
    {
        Phase = RenderJobFile.Camel(tick.Phase.ToString()),
        Message = tick.Message,
        Fraction = tick.Fraction is { } f ? Math.Round(f, 4) : null,
        Segment = tick.Segment,
        SegmentCount = tick.SegmentCount,
        Frame = tick.Frame,
        FrameCount = tick.FrameCount,
        SecondsPerFrame = tick.SecondsPerFrame is { } s ? Math.Round(s, 3) : null,
        SecondsLeft = tick.SecondsLeft is { } l ? Math.Round(l, 1) : null,
    };
}

/// <summary>How a job ended.</summary>
public sealed record RenderJobResult
{
    public bool Succeeded { get; init; }

    /// <summary>The camel-case name of a <see cref="RenderFailureKind"/>, or another word for a
    /// failure that came before Blender (a bundle that was refused: "refused").</summary>
    public string? FailureKind { get; init; }

    public string? Error { get; init; }

    /// <summary>The files the job wrote: the movie and its subtitles.</summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>Blender's output.</summary>
    public string? Log { get; init; }

    /// <summary>The device Blender rendered on.</summary>
    public string? Device { get; init; }

    /// <summary>Seconds a frame took, measured.</summary>
    public double? SecondsPerFrame { get; init; }

    public double Seconds { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];

    /// <summary>A render's result as a record.</summary>
    public static RenderJobResult From(RenderResult result, double? secondsPerFrame = null) => new()
    {
        Succeeded = result.Succeeded,
        FailureKind = result.Succeeded ? null : RenderJobFile.Camel(result.FailureKind.ToString()),
        Error = result.Succeeded ? null : result.ErrorMessage,
        Outputs = result.OutputPaths,
        Log = result.JobLogPath,
        Device = result.Device,
        SecondsPerFrame = secondsPerFrame is { } s ? Math.Round(s, 3) : null,
        Seconds = Math.Round(result.Duration.TotalSeconds, 1),
        Notices = result.Warnings,
    };

    /// <summary>A job that ended before it rendered: refused, or failed on its way.</summary>
    public static RenderJobResult Failure(string kind, string error) => new() { FailureKind = kind, Error = error };
}

/// <summary>One render job.</summary>
public sealed record RenderJobRecord
{
    public string Id { get; init; } = "";

    public RenderJobRequest Request { get; init; } = new();

    public RenderJobState State { get; init; } = RenderJobState.Queued;

    public DateTimeOffset QueuedUtc { get; init; }

    public DateTimeOffset? StartedUtc { get; init; }

    public DateTimeOffset? EndedUtc { get; init; }

    public RenderJobProgress? Progress { get; init; }

    public RenderJobResult? Result { get; init; }

    /// <summary>Succeeded, failed or cancelled.</summary>
    [JsonIgnore]
    public bool IsFinished => State is RenderJobState.Succeeded or RenderJobState.Failed or RenderJobState.Cancelled;

    /// <summary>The job, ended: its state from the result, unless it was cancelled.</summary>
    public RenderJobRecord Ended(RenderJobResult result, DateTimeOffset at, bool cancelled = false) => this with
    {
        State = cancelled || result.FailureKind == RenderNodeFailure.Cancelled ? RenderJobState.Cancelled
            : result.Succeeded ? RenderJobState.Succeeded : RenderJobState.Failed,
        EndedUtc = at,
        Result = result,
    };
}

/// <summary>Render job records as JSON: the file a queue or a worker keeps them in.</summary>
public static class RenderJobFile
{
    /// <summary>The version of the file's layout.</summary>
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record Document(int Version, IReadOnlyList<RenderJobRecord> Jobs);

    /// <summary>The records, oldest first.</summary>
    public static string Write(IReadOnlyList<RenderJobRecord> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return JsonSerializer.Serialize(new Document(Version, jobs), Options);
    }

    /// <summary>Reads what <see cref="Write"/> wrote. A file of another version, or one that is
    /// not this file at all, reads as no jobs: the file is a convenience, not a contract.</summary>
    public static IReadOnlyList<RenderJobRecord> Read(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json, Options);
            return document is { Version: Version, Jobs: { } jobs } ? jobs.Where(j => j is not null && j.Id.Length > 0).ToList() : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>One record (a worker's status file).</summary>
    public static string WriteOne(RenderJobRecord job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return JsonSerializer.Serialize(job, Options);
    }

    /// <summary>Reads what <see cref="WriteOne"/> wrote; null when it is not such a record.</summary>
    public static RenderJobRecord? ReadOne(string json)
    {
        try { return JsonSerializer.Deserialize<RenderJobRecord>(json, Options) is { Id.Length: > 0 } job ? job : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The jobs as they stand after a restart: a job that was running is queued again, where it
    /// was (its frames are still there, so it goes on), and the jobs that had not ended keep their
    /// order, oldest first. The ended ones are kept as they are.
    /// </summary>
    public static IReadOnlyList<RenderJobRecord> Restarted(IReadOnlyList<RenderJobRecord> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return jobs
            .Select(j => j.State == RenderJobState.Running ? j with { State = RenderJobState.Queued, Progress = null } : j)
            .OrderBy(j => j.QueuedUtc)
            .ThenBy(j => j.Id, StringComparer.Ordinal)
            .ToList();
    }

    internal static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
