// Labels & annotations spec (BA-B8, FR-05, scope D-03 = all four groups) — the knobs the
// label engine turns into billboarded 3-D text, lead markers, HUD overlays, and keyframed
// visibility. Plain, JSON-serializable data on SceneSpec. Label *data* (names, positions)
// comes from scene-meta.json; this only carries the presentation choices. Every group
// defaults off, so a default spec emits no labels.

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

    /// <summary>Also place the station name / note next to the marker.</summary>
    public bool ShowText { get; init; }
}

/// <summary>Presentation overlays — screen-space HUD parented to the camera (FR-05).</summary>
public sealed record OverlaySpec
{
    /// <summary>Title-card text; null/blank = no title.</summary>
    public string? Title { get; init; }

    public bool ScaleBar { get; init; }

    public bool NorthArrow { get; init; }

    public bool DepthLegend { get; init; }
}

/// <summary>A keyframed show/hide/fade applied to a whole label group (FR-05).</summary>
public sealed record VisibilityEvent
{
    public required VisibilityTarget Target { get; init; }

    /// <summary>Frame the group appears (null = visible from the start).</summary>
    public int? ShowFrame { get; init; }

    /// <summary>Frame the group disappears (null = never hidden).</summary>
    public int? HideFrame { get; init; }

    /// <summary>Cross-fade duration in seconds (0 = a hard cut).</summary>
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

    /// <summary>Emissive tint for label text (readable in dark scenes).</summary>
    public ColorRgb Color { get; init; } = new(0.95, 0.93, 0.82);
}
