// Labels & annotations spec (BA-B8, FR-05, scope D-03 = all four groups) — the knobs the
// label engine turns into billboarded 3-D text, lead markers, HUD overlays, and keyframed
// visibility. Plain, JSON-serializable data on SceneSpec. Label *data* (names, positions)
// comes from scene-meta.json; this only carries the presentation choices. Every group
// defaults off, so a default spec emits no labels.

using System.Text.Json.Serialization;

namespace Therion.Blender;

/// <summary>Which stations get a label (FR-05).</summary>
public enum StationFilter
{
    /// <summary>Only cave entrances.</summary>
    Entrances,
    /// <summary>Every named station (anonymous ones are already dropped from scene-meta).</summary>
    Named,
    /// <summary>Stations whose name matches <see cref="StationLabelSpec.Pattern"/>.</summary>
    Regex,
    /// <summary>Stations whose depth (local Z) is within
    /// [<see cref="StationLabelSpec.MinDepth"/>, <see cref="StationLabelSpec.MaxDepth"/>].</summary>
    DepthRange,
}

/// <summary>A group the visibility/fade events can target.</summary>
public enum VisibilityTarget
{
    StationLabels,
    ComponentLabels,
    LeadMarkers,
    Overlays,
}

/// <summary>How big a lead marker is drawn.</summary>
public enum LeadMarkerSizing
{
    /// <summary>From the passage in a flythrough (the camera is inside the cave), from the whole
    /// cave for every other camera.</summary>
    Auto,
    /// <summary>From the whole cave (1 % of its diagonal), whatever the camera: seen from inside,
    /// a marker can be wider than its passage.</summary>
    Cave,
    /// <summary>From the passage it is in: a fraction of the roof height at its station.</summary>
    Passage,
    /// <summary>Grows with its distance from the camera, so it keeps about the same size in the
    /// picture: never tiny far away, never filling the picture close up.</summary>
    CameraDistance,
    /// <summary>From the whole cave, and not drawn at all in a flythrough.</summary>
    HiddenInFlythrough,
}

/// <summary>Where a station label's roof (the height it is lifted above its station) comes from.</summary>
public enum LabelRoofSource
{
    /// <summary>The surveyed up distance of a tube model's level legs, else the walls above.</summary>
    SurveyFirst,
    /// <summary>The walls above the station, else the surveyed up distance.</summary>
    WallsFirst,
    /// <summary>The walls above the station only (a station with none is labelled where it is).</summary>
    WallsOnly,
    /// <summary>No roof: every label sits on its station.</summary>
    None,
}

/// <summary>Station/entrance label configuration (FR-05). The <see cref="MaxCount"/> cap +
/// distance thinning keep thousands of stations from melting render times (R-13/NFR-06).</summary>
public sealed record StationLabelSpec
{
    public bool Show { get; init; }

    public StationFilter Filter { get; init; } = StationFilter.Entrances;

    /// <summary>Regex applied to the station name for <see cref="StationFilter.Regex"/>.</summary>
    public string? Pattern { get; init; }

    /// <summary>Inclusive local-Z lower bound for <see cref="StationFilter.DepthRange"/>.</summary>
    public double? MinDepth { get; init; }

    /// <summary>Inclusive local-Z upper bound for <see cref="StationFilter.DepthRange"/>.</summary>
    public double? MaxDepth { get; init; }

    /// <summary>Hard cap on labelled stations; the planner thins to the most spread-out
    /// subset when the filter yields more (R-13).</summary>
    public int MaxCount { get; init; } = 200;

    /// <summary>Multiplier on the auto text size (relative to the model bounds).</summary>
    public double TextScale { get; init; } = 1.0;
}

/// <summary>Component (connected-piece) label configuration (FR-05).</summary>
public sealed record ComponentLabelSpec
{
    public bool Show { get; init; }

    /// <summary>Only label components with at least this many stations (skip tiny bits).</summary>
    public int MinStationCount { get; init; } = 5;

    public double TextScale { get; init; } = 2.0;
}

/// <summary>Lead / QM marker configuration (FR-05; data from the leads register via
/// scene-meta.json).</summary>
public sealed record LeadMarkerSpec
{
    public bool Show { get; init; }

    /// <summary>Give each marker a cyclic scale pulse so it draws the eye.</summary>
    public bool Pulse { get; init; } = true;

    public double MarkerScale { get; init; } = 1.0;

    /// <summary>How the markers are sized (see <see cref="LeadMarkerSizing"/>).</summary>
    public LeadMarkerSizing Sizing { get; init; } = LeadMarkerSizing.Auto;

    /// <summary>Also place the station name / note next to the marker.</summary>
    public bool ShowText { get; init; }
}

/// <summary>The card a movie ends on: its text over a dark picture, for
/// <see cref="Seconds"/> seconds after the last frame of the scene, which stands still under it.
/// <see cref="Backdrop"/> lets that frame show through, and <see cref="OverScene"/> brings the
/// card over the end of the scene itself. A still has no end card.</summary>
public sealed record EndCardSpec
{
    /// <summary>What the card says; line breaks are kept.</summary>
    public string Text { get; init; } = "";

    /// <summary>A smaller line under the text; null or blank is none.</summary>
    public string? Subtitle { get; init; }

    /// <summary>How long the card stays, in seconds; the movie is that much longer.</summary>
    public double Seconds { get; init; } = 4.0;

    /// <summary>How dark the card is under its text, from 0 to 1. 1 is a black card; 0 is no
    /// card at all, the text standing over the picture on a dark box of its own; between the two
    /// the picture shows through, dimmed.</summary>
    public double Backdrop { get; init; } = 1.0;

    /// <summary>True brings the card over the last <see cref="Seconds"/> of the scene, which
    /// goes on under it: the movie is no longer for it. False holds the last frame for the card.</summary>
    public bool OverScene { get; init; }
}

/// <summary>A line of text shown at the bottom of the picture from <see cref="Start"/> to
/// <see cref="End"/>, in seconds from the start of the movie. Captions are also written as
/// subtitle files beside the movie (<see cref="Subtitles"/>).</summary>
public sealed record CaptionSpec
{
    public string Text { get; init; } = "";

    public double Start { get; init; }

    public double End { get; init; }
}

/// <summary>Presentation overlays: what is written over the picture. A scene built by the player
/// draws them in two dimensions, in a second stage after the frames are rendered, so nothing in
/// the scene can hide them; the script generator draws the first four as objects in front of the
/// camera and in the scene, and leaves the others out.</summary>
public sealed record OverlaySpec
{
    /// <summary>Title-card text; null/blank = no title.</summary>
    public string? Title { get; init; }

    public bool ScaleBar { get; init; }

    public bool NorthArrow { get; init; }

    public bool DepthLegend { get; init; }

    /// <summary>A small line that stays in a corner of the picture throughout: who surveyed,
    /// who drew, a licence. Null or blank is none.</summary>
    public string? Credits { get; init; }

    /// <summary>The card the movie ends on; null is none.</summary>
    public EndCardSpec? EndCard { get; init; }

    /// <summary>Timed lines of text at the bottom of the picture. Where two overlap in time,
    /// the later one replaces the earlier: one caption is shown at a time.</summary>
    public IReadOnlyList<CaptionSpec> Captions { get; init; } = [];

    /// <summary>A smaller line under the title; null or blank is none. Not shown without a
    /// title.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Set, the title is a card: it stands in the middle of the picture from the first
    /// frame for this many seconds, then fades out. Null keeps the title at the top of the
    /// picture throughout.</summary>
    public double? TitleSeconds { get; init; }

    /// <summary>A line that stays in the top left corner of the picture throughout, beside the
    /// credits line: a place, a date. Null or blank is none.</summary>
    public string? Corner { get; init; }

    /// <summary>Timed lines in the top left corner, under <see cref="Corner"/>; one at a time,
    /// as with <see cref="Captions"/>. They are not written as subtitles.</summary>
    public IReadOnlyList<CaptionSpec> CornerCaptions { get; init; } = [];

    /// <summary>How long the movie takes to come out of black at its start, in seconds.</summary>
    public double FadeFromBlack { get; init; }

    /// <summary>How long the movie takes to go to black at its end, in seconds.</summary>
    public double FadeToBlack { get; init; }

    /// <summary>True when the spec asks for something only the two-dimensional stage draws.</summary>
    [JsonIgnore]
    public bool HasFlatOnlyTexts
        => !string.IsNullOrWhiteSpace(Credits) || EndCard is not null || Captions.Count > 0
           || (!string.IsNullOrWhiteSpace(Title) && (!string.IsNullOrWhiteSpace(Subtitle) || TitleSeconds is not null))
           || !string.IsNullOrWhiteSpace(Corner) || CornerCaptions.Count > 0 || FadeFromBlack > 0 || FadeToBlack > 0;

    /// <summary>True when anything is written over the picture.</summary>
    [JsonIgnore]
    public bool Any
        => !string.IsNullOrWhiteSpace(Title) || ScaleBar || NorthArrow || DepthLegend || HasFlatOnlyTexts;
}

/// <summary>A keyframed show/hide/fade applied to a whole label group (FR-05). A scene plan
/// carries it as the group's changes (<see cref="Plan.ScenePlan.Changes"/>), or, for the overlays,
/// as the times of the texts that stand for them.</summary>
public sealed record VisibilityEvent
{
    public required VisibilityTarget Target { get; init; }

    /// <summary>Frame the group appears (null = visible from the start).</summary>
    public int? ShowFrame { get; init; }

    /// <summary>Frame the group disappears (null = never hidden).</summary>
    public int? HideFrame { get; init; }

    /// <summary>How long the group takes to appear, from <see cref="ShowFrame"/> on, in seconds
    /// (0 = at once): the player fades it in, a generated script grows it from nothing. Hiding
    /// is at once in both.</summary>
    public double FadeSeconds { get; init; }
}

/// <summary>All label &amp; annotation configuration (FR-05).</summary>
public sealed record LabelsSpec
{
    public StationLabelSpec Stations { get; init; } = new();

    public ComponentLabelSpec Components { get; init; } = new();

    public LeadMarkerSpec Leads { get; init; } = new();

    public OverlaySpec Overlays { get; init; } = new();

    public IReadOnlyList<VisibilityEvent> Events { get; init; } = [];

    /// <summary>Where the roof that station labels are lifted above comes from; also the roof a
    /// passage-sized lead marker is sized from.</summary>
    public LabelRoofSource Roof { get; init; } = LabelRoofSource.SurveyFirst;

    /// <summary>Emissive tint for label text (readable in dark scenes).</summary>
    public ColorRgb Color { get; init; } = new(0.95, 0.93, 0.82);
}
