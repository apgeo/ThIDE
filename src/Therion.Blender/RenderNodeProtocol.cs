// The protocol a render worker speaks to a server it takes render jobs from (a render node of a
// server such as SilexGIS): the messages as records, their JSON, and the rules both ends keep
// (which addresses are allowed, how long to wait after a failure, what a job id and a file name
// may be). docs/dev/render-worker-protocol.md describes it for whoever writes the server, and
// render-node.schema.json is its schema. Plain data; nothing here opens a connection.

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Therion.Blender.Plan;

namespace Therion.Blender;

/// <summary>The constants and rules of the render node protocol.</summary>
public static partial class RenderNodeProtocol
{
    /// <summary>The protocol's version; a server answers a claim of another version with 400.</summary>
    public const int Version = 1;

    /// <summary>The endpoints, relative to the server URL the worker is given.</summary>
    public const string ClaimPath = "claim";

    public static string HeartbeatPath(string jobId) => $"jobs/{Uri.EscapeDataString(jobId)}/heartbeat";

    public static string FilePath(string jobId, string name, int attempt) =>
        $"jobs/{Uri.EscapeDataString(jobId)}/files/{Uri.EscapeDataString(name)}?attempt={attempt.ToString(CultureInfo.InvariantCulture)}";

    public static string CompletePath(string jobId) => $"jobs/{Uri.EscapeDataString(jobId)}/complete";

    public static string FailPath(string jobId) => $"jobs/{Uri.EscapeDataString(jobId)}/fail";

    /// <summary>The header an uploaded file's SHA-256 travels in, as 64 lower-case hex digits.</summary>
    public const string Sha256Header = "X-Content-SHA256";

    /// <summary>The plans a job may name.</summary>
    public const string FullPlan = "full";

    public const string DraftPlan = "draft";

    /// <summary>The answers to a heartbeat.</summary>
    public const string Continue = "continue";

    public const string Cancel = "cancel";

    /// <summary>The containers a job may ask for: those of the player's output module.</summary>
    public static IReadOnlyList<string> Containers { get; } =
        Enum.GetValues<PlanContainer>().Select(c => c.ToString().ToLowerInvariant()).ToList();

    /// <summary>The container of a job that names none.</summary>
    public const string DefaultContainer = "mp4";

    /// <summary>The container <paramref name="name"/> names; false for one the player does not write.</summary>
    public static bool TryContainer(string? name, out PlanContainer container)
    {
        var known = Enum.GetValues<PlanContainer>();
        int index = Containers.ToList().IndexOf(name ?? DefaultContainer);
        container = index < 0 ? PlanContainer.Mp4 : known[index];
        return index >= 0;
    }

    /// <summary>A job id is a safe file name: letters, digits, '.', '_' and '-', 1 to 64 of them,
    /// starting with a letter or a digit.</summary>
    public static bool IsJobId(string? id) => id is not null && JobIdPattern().IsMatch(id);

    /// <summary>A result file's name: the job id, maybe a frame number, and one of the result extensions.</summary>
    public static bool IsFileName(string? name) => name is not null && FileNamePattern().IsMatch(name);

    /// <summary>A SHA-256 as the protocol writes it: 64 lower-case hex digits.</summary>
    public static bool IsSha256(string? hex) => hex is not null && Sha256Pattern().IsMatch(hex);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex JobIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    /// <summary>
    /// Whether the worker may talk to <paramref name="uri"/>: HTTPS, or plain HTTP to this machine
    /// (a loopback address or <c>localhost</c>) only. A node token never crosses a network in the clear.
    /// </summary>
    public static bool IsAllowedAddress(Uri uri, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(uri);
        problem = null;
        if (!uri.IsAbsoluteUri) problem = "the address is not absolute";
        else if (!string.IsNullOrEmpty(uri.UserInfo)) problem = "the address carries a user name or password";
        else if (uri.Scheme == Uri.UriSchemeHttps) return true;
        else if (uri.Scheme != Uri.UriSchemeHttp) problem = $"'{uri.Scheme}' is not HTTP or HTTPS";
        else if (IsLoopback(uri)) return true;
        else problem = "plain HTTP is allowed to this machine only; use HTTPS";
        return false;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    /// <summary>Whether <paramref name="uri"/> is on the server's own origin (scheme, host and
    /// port): the only place the node token is sent to.</summary>
    public static bool IsSameOrigin(Uri server, Uri uri) =>
        Uri.Compare(server, uri, UriComponents.SchemeAndServer, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// How long to wait before try <paramref name="failures"/> + 1 after that many failures in a
    /// row: <paramref name="first"/> doubled each time up to <paramref name="max"/>, then
    /// scaled by 0.5 to 1.0 by <paramref name="jitter"/> (0 to 1) so that nodes that failed together
    /// do not come back together.
    /// </summary>
    public static TimeSpan Backoff(int failures, TimeSpan first, TimeSpan max, double jitter)
    {
        if (failures <= 0) return TimeSpan.Zero;
        double seconds = Math.Min(max.TotalSeconds, first.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 30)));
        return TimeSpan.FromSeconds(seconds * (0.5 + 0.5 * Math.Clamp(jitter, 0, 1)));
    }

    /// <summary>The heartbeat interval of a lease: a third of it, so that one heartbeat can be
    /// missed; the server's own when it names a shorter one; never under a second.</summary>
    public static TimeSpan HeartbeatInterval(int leaseSeconds, int? heartbeatSeconds)
    {
        double third = Math.Max(1, leaseSeconds) / 3.0;
        double seconds = heartbeatSeconds is { } s && s > 0 && s <= third ? s : third;
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write<T>(T message) => JsonSerializer.Serialize(message, Options);

    /// <summary>The message in <paramref name="json"/>; null when it is not JSON of that shape.</summary>
    public static T? Read<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }

    private static readonly Lazy<string> LoadedSchema = new(() =>
    {
        using var stream = typeof(RenderNodeProtocol).Assembly.GetManifestResourceStream("render-node/" + SchemaFileName)
                           ?? throw new InvalidOperationException("The render node schema is not in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    });

    public const string SchemaFileName = "render-node.schema.json";

    /// <summary>The JSON schema of the messages (its <c>$defs</c> name each one).</summary>
    public static string Schema => LoadedSchema.Value;

    /// <summary>The names of the messages <see cref="Validate"/> knows: the schema's <c>$defs</c>
    /// that are bodies.</summary>
    public static IReadOnlyList<string> MessageKinds { get; } =
        ["claim", "job", "heartbeat", "heartbeatReply", "complete", "fail"];

    /// <summary>Holds a message to the schema as a receiver should: a property the schema does not
    /// know is accepted (the protocol says receivers ignore them); a missing required property, a
    /// wrong type or value, or another <c>protocol</c> version is a problem.</summary>
    /// <param name="messageKind">One of <see cref="MessageKinds"/>.</param>
    /// <param name="json">The body received.</param>
    /// <returns>The problems, each with the path of the value; empty when the message is good.</returns>
    /// <exception cref="ArgumentException"><paramref name="messageKind"/> is not a message.</exception>
    public static IReadOnlyList<string> Validate(string messageKind, string json)
    {
        if (!MessageKinds.Contains(messageKind)) throw new ArgumentException($"'{messageKind}' is not a render node message.", nameof(messageKind));
        return RenderNodeValidator.Validate(Schema, messageKind, json);
    }
}

/// <summary>The reasons a node gives when a job fails.</summary>
public static class RenderNodeFailure
{
    /// <summary>The bundle broke a rule of the worker (paths, links, module folders, features,
    /// plans that do not validate, sizes, triangles, work): nothing of it was played.</summary>
    public const string Refused = "refused";

    /// <summary>The claim's job was not one this worker can take (its id, its plan, its container,
    /// its bundle's size or checksum as written, the protocol's version).</summary>
    public const string BadJob = "badJob";

    /// <summary>The bundle downloaded is not the one the claim described (size or SHA-256).</summary>
    public const string Checksum = "checksum";

    /// <summary>The bundle could not be downloaded.</summary>
    public const string Download = "download";

    /// <summary>A result could not be uploaded, or the server would not take one (too large).</summary>
    public const string Upload = "upload";

    /// <summary>The server cancelled the job, and the node stopped it.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The job ran longer than its limit; its frames stay on the node.</summary>
    public const string TimedOut = "timedOut";

    /// <summary>There is no Blender on the node, or the one named cannot be started.</summary>
    public const string BlenderNotFound = "blenderNotFound";

    /// <summary>The node's Blender is older than the player needs.</summary>
    public const string BlenderTooOld = "blenderTooOld";

    /// <summary>The node's disk is too full for the render.</summary>
    public const string DiskSpace = "diskSpace";

    /// <summary>The player failed on the plans.</summary>
    public const string ScriptError = "scriptError";

    /// <summary>Blender stopped without finishing.</summary>
    public const string Crashed = "crashed";

    /// <summary>The render ended without writing the movie or still the plan asks for.</summary>
    public const string NoOutput = "noOutput";

    /// <summary>The node failed in some other way.</summary>
    public const string Error = "error";

    /// <summary>Every reason, in the order of the schema's enum.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Refused, BadJob, Checksum, Download, Upload, Cancelled, TimedOut,
        BlenderNotFound, BlenderTooOld, DiskSpace, ScriptError, Crashed, NoOutput, Error,
    ];

    /// <summary>Whether another try (on this node or another) may go better: a hint to the server.</summary>
    public static bool IsRetryable(string reason) => reason is Checksum or Download or Upload or BlenderNotFound
        or BlenderTooOld or DiskSpace or Crashed or Error;
}

/// <summary>A node asks for a job: <c>POST claim</c>.</summary>
public sealed record RenderNodeClaim
{
    public int Protocol { get; init; } = RenderNodeProtocol.Version;

    /// <summary>The node's name, as its operator gave it.</summary>
    public string Node { get; init; } = "";

    /// <summary>The worker's version (the therion-cli it is).</summary>
    public string? Worker { get; init; }

    /// <summary>The Blender versions the node can render with, newest first.</summary>
    public IReadOnlyList<string> BlenderVersions { get; init; } = [];

    /// <summary>The device it renders on, when it knows: "GPU (CUDA)", "CPU"…</summary>
    public string? Device { get; init; }

    /// <summary>Free bytes on the node's scratch disk.</summary>
    public long? FreeDiskBytes { get; init; }

    /// <summary>The containers the node writes.</summary>
    public IReadOnlyList<string> Containers { get; init; } = RenderNodeProtocol.Containers;

    /// <summary>The jobs whose frames the node still holds: taken again, they go on from them.</summary>
    public IReadOnlyList<string> KeptJobs { get; init; } = [];

    /// <summary>How long the server may hold the claim open waiting for a job (a long poll); 0 answers at once.</summary>
    public int WaitSeconds { get; init; }
}

/// <summary>The bundle a job renders.</summary>
public sealed record RenderNodeBundle
{
    /// <summary>Where it is downloaded from: absolute, or relative to the server URL. The node
    /// token is sent with it only when it is on the server's own origin.</summary>
    public string Url { get; init; } = "";

    public long Size { get; init; }

    public string Sha256 { get; init; } = "";
}

/// <summary>What the server limits a job to, below the node's own limits (a node keeps the smaller).</summary>
public sealed record RenderNodeLimits
{
    public double? MaxHours { get; init; }

    public long? MaxUnpackedBytes { get; init; }

    public long? MaxTriangles { get; init; }

    public double? MaxWork { get; init; }

    /// <summary>The largest result file the server takes.</summary>
    public long? MaxResultBytes { get; init; }
}

/// <summary>The server gives a node a job (200 to a claim; 204 when it has none).</summary>
public sealed record RenderNodeJob
{
    public int Protocol { get; init; } = RenderNodeProtocol.Version;

    public string JobId { get; init; } = "";

    /// <summary>Which try of the job this is (the server's attempt count): every later call names it.</summary>
    public int Attempt { get; init; } = 1;

    /// <summary>How long the job is the node's without a heartbeat.</summary>
    public int LeaseSeconds { get; init; } = 120;

    /// <summary>How often the node sends a heartbeat; a third of the lease when absent.</summary>
    public int? HeartbeatSeconds { get; init; }

    /// <summary>A name for the logs; not used for files.</summary>
    public string? Name { get; init; }

    public RenderNodeBundle Bundle { get; init; } = new();

    /// <summary><c>full</c> or <c>draft</c>.</summary>
    public string Plan { get; init; } = RenderNodeProtocol.FullPlan;

    /// <summary>The movie's container: <c>mp4</c> (the default), <c>webm</c> or <c>mkv</c>.</summary>
    public string? Container { get; init; }

    public RenderNodeLimits? Limits { get; init; }
}

/// <summary>A node says how far the job got and keeps its lease: <c>POST jobs/{id}/heartbeat</c>.</summary>
public sealed record RenderNodeHeartbeat
{
    public int Attempt { get; init; }

    /// <summary>downloading, checking, rendering, composing, collectingOutputs, uploading…</summary>
    public string Phase { get; init; } = "";

    public string? Message { get; init; }

    public int? Segment { get; init; }

    public int? SegmentCount { get; init; }

    /// <summary>Frames done of <see cref="FrameCount"/>, over the whole job: ProgressDone and ProgressTotal.</summary>
    public int? Frame { get; init; }

    public int? FrameCount { get; init; }

    public double? SecondsPerFrame { get; init; }

    public double? SecondsLeft { get; init; }
}

/// <summary>The server's answer to a heartbeat (200); 409 when the job is no longer the node's.</summary>
public sealed record RenderNodeHeartbeatReply
{
    /// <summary><c>continue</c> or <c>cancel</c>.</summary>
    public string Action { get; init; } = RenderNodeProtocol.Continue;

    /// <summary>The lease from now, when the server changes it.</summary>
    public int? LeaseSeconds { get; init; }
}

/// <summary>A file of a job's result.</summary>
public sealed record RenderNodeFile
{
    public string Name { get; init; } = "";

    /// <summary><c>movie</c>, <c>still</c>, <c>subtitles</c>, <c>log</c> or <c>status</c>.</summary>
    public string Role { get; init; } = "";

    public long Size { get; init; }

    public string Sha256 { get; init; } = "";
}

/// <summary>A node says a job succeeded, after uploading its files: <c>POST jobs/{id}/complete</c>.</summary>
public sealed record RenderNodeComplete
{
    public int Attempt { get; init; }

    public IReadOnlyList<RenderNodeFile> Files { get; init; } = [];

    public string? Device { get; init; }

    public double? SecondsPerFrame { get; init; }

    public double Seconds { get; init; }

    public int? FrameCount { get; init; }

    public IReadOnlyList<string> Notices { get; init; } = [];
}

/// <summary>A node says a job failed: <c>POST jobs/{id}/fail</c>.</summary>
public sealed record RenderNodeFail
{
    public int Attempt { get; init; }

    /// <summary>One of <see cref="RenderNodeFailure.All"/>.</summary>
    public string Reason { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>Whether another try may go better: a hint, the server decides.</summary>
    public bool Retryable { get; init; }
}
