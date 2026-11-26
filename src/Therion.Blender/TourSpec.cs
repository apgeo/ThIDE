// A tour of a cave's branches, as somebody asks for it: which way the main passage runs, which
// side passages are visited and how each is left, and how long the film may be. It is lowered to
// a storyboard (Plan/TourBuilder): the storyboard is what is rendered, kept and edited by hand; a
// tour is the rules it was made by.
//
// As JSON (a tour file): camelCase keys, enums by name, and a key that is not known refused.

using System.Text.Json;
using System.Text.Json.Serialization;
using Therion.CaveGeometry;

namespace Therion.Blender;

/// <summary>What the camera does about a side passage.</summary>
public enum TourBranchMode
{
    /// <summary>Flies in to the end, holds, and cuts back to the junction. A bypass is flown
    /// through to where it comes back to the main passage, then the film cuts back.</summary>
    Cut,

    /// <summary>As <see cref="Cut"/>, but it turns round at the end and flies back out.</summary>
    Return,

    /// <summary>Stays at the junction and looks into the side passage for a few seconds.</summary>
    Peek,

    /// <summary>Passes by.</summary>
    Skip,
}

/// <summary>A tour: see the file header. Stations are named as the model names them, or
/// <c>name@survey</c>.</summary>
public sealed record TourSpec
{
    /// <summary>The film's name; null is the cave's, with "tour". Placeholders are filled in.</summary>
    public string? Name { get; init; }

    // ---- the way (TourOptions) ----

    /// <summary>Where the tour starts; null is the first station flagged as an entrance, or an
    /// end of the longest passage.</summary>
    public string? Start { get; init; }

    public TourTrunkKind Trunk { get; init; } = TourTrunkKind.Auto;

    /// <summary>For a trunk via stations: the stations it passes.</summary>
    public IReadOnlyList<string> Via { get; init; } = [];

    /// <summary>For a trunk that goes through, or via stations: where it ends.</summary>
    public string? End { get; init; }

    // ---- the choice (TourOptions) ----

    /// <summary>Side passages shorter than this (metres) are left out; null is the greater of
    /// 50 m and 2 % of the main passage.</summary>
    public double? MinLength { get; init; }

    public int MaxCount { get; init; } = TourPlanner.DefaultMaxCount;

    /// <summary>A side passage with a lead is visited whatever its length.</summary>
    public bool WithLeads { get; init; } = true;

    /// <summary>Visited whatever the rules say: branch ids ("B3"), station names, survey names.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>Left out whatever the rules say.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>1 visits what hangs on the main passage; 2 also what hangs on those.</summary>
    public int Depth { get; init; } = 1;

    // ---- the visits ----

    /// <summary>What the camera does about a side passage that is chosen.</summary>
    public TourBranchMode Mode { get; init; } = TourBranchMode.Cut;

    /// <summary>By branch id ("B5"), what it does about that one: a mode named here is a choice
    /// by hand, so <see cref="TourBranchMode.Cut"/> and <see cref="TourBranchMode.Return"/> visit
    /// the branch whatever the rules say, and <see cref="TourBranchMode.Skip"/> leaves it out.</summary>
    public IReadOnlyDictionary<string, TourBranchMode> Modes { get; init; } = new Dictionary<string, TourBranchMode>();

    /// <summary>Side passages left out as too short get a look from the junction
    /// (<see cref="TourBranchMode.Peek"/>).</summary>
    public bool PeekShort { get; init; }

    // ---- the time ----

    /// <summary>How long the whole film may be (seconds).</summary>
    public double TourSeconds { get; init; } = 180.0;

    /// <summary>How long the visit of one side passage may be (seconds).</summary>
    public double BranchSeconds { get; init; } = 20.0;

    // ---- the film ----

    /// <summary>The preset the film takes its materials, lighting and render engine from: they
    /// are written into the storyboard's defaults, so the storyboard no longer needs the preset.</summary>
    public string Preset { get; init; } = "Full flythrough";

    public int Fps { get; init; } = 30;

    /// <summary>What the film is written as; null is a video named <c>cave-tour</c>.</summary>
    public OutputSpec? Output { get; init; }

    /// <summary>The language of the texts the tour writes over the picture: <c>en</c> or <c>ro</c>.</summary>
    public string Language { get; init; } = "en";

    /// <summary>The options of the tour that the passage network alone decides.</summary>
    public TourOptions ToOptions()
    {
        // A mode named for a branch is a choice by hand: a visit is included, anything else is
        // kept out of the rules' way (a peek is not a visit, and takes no place).
        var include = Include.Concat(Modes.Where(m => m.Value is TourBranchMode.Cut or TourBranchMode.Return).Select(m => m.Key));
        var exclude = Exclude.Concat(Modes.Where(m => m.Value is TourBranchMode.Skip or TourBranchMode.Peek).Select(m => m.Key));
        return new TourOptions
        {
            Start = Start, Trunk = Trunk, Via = Via, End = End, MinLength = MinLength, MaxCount = MaxCount, WithLeads = WithLeads,
            Include = include.Distinct(StringComparer.Ordinal).ToList(), Exclude = exclude.Distinct(StringComparer.Ordinal).ToList(), Depth = Depth,
        };
    }
}

/// <summary>Thrown when a tour file is not one.</summary>
public sealed class TourFormatException(string message) : Exception(message);

/// <summary>Reads and writes <see cref="TourSpec"/>s as JSON.</summary>
public static class TourSpecSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(TourSpec tour)
    {
        ArgumentNullException.ThrowIfNull(tour);
        return JsonSerializer.Serialize(tour, Options).ReplaceLineEndings("\n") + "\n";
    }

    /// <exception cref="TourFormatException">The text is not a tour. The message quotes nothing
    /// of it beyond the key or the kind of value at fault.</exception>
    public static TourSpec Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using (var probe = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
                if (probe.RootElement.ValueKind != JsonValueKind.Object)
                    throw new TourFormatException("A tour file is a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new TourFormatException(JsonFault.NotJson(ex));
        }
        try
        {
            return JsonSerializer.Deserialize<TourSpec>(json, Options) ?? throw new TourFormatException("The tour deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new TourFormatException(JsonFault.OfShape(ex));
        }
    }

    public static TourSpec ReadFile(string path) => Read(File.ReadAllText(path));
}
