// SceneSpec — the full, JSON-serializable description of a render job (BA-B5).
//
// The single source of truth the whole module orbits: the UI (BA-B12) binds to it,
// presets (BA-B9) persist it, the emitter turns it into a Blender Python script, and
// the CLI (BA-B14) consumes it as JSON. Plain data, no behavior (NFR-03 determinism:
// same spec + seed ⇒ byte-identical script). Validation lives in SceneSpecValidator,
// (de)serialization + versioning in SceneSpecSerializer.
//
// v1 carries the fields the emitter CORE consumes. The camera engine (BA-B6),
// materials/lighting (BA-B7) and labels (BA-B8) add their sub-objects in their own
// batches — additive JSON with defaults, same schema version. Until then the core
// emits a documented baseline material/light and a static auto-framed camera.

namespace Therion.Blender;

/// <summary>Which render engine the generated script selects (D-09).</summary>
public enum RenderEngineKind
{
    /// <summary>Cycles — the default; works headless everywhere.</summary>
    Cycles,
    /// <summary>EEVEE (Next) — faster; probed by name at runtime, may need a GPU/display context (R-04).</summary>
    Eevee,
}

/// <summary>Requested Cycles compute device (FR-08). Auto tries the full cascade.</summary>
public enum GpuMode
{
    Auto,
    OptiX,
    Cuda,
    Hip,
    OneApi,
    Metal,
    CpuOnly,
}

/// <summary>What the render produces (FR-09; still-set lands with BA-B6 viewpoints).</summary>
public enum OutputKind
{
    /// <summary>A video file via Blender's built-in FFmpeg (D-10).</summary>
    Video,
    /// <summary>A numbered PNG frame sequence.</summary>
    FrameSequence,
    /// <summary>A single PNG still.</summary>
    Still,
}

/// <summary>Video container for <see cref="OutputKind.Video"/> (H.264 in all three).</summary>
public enum VideoContainer
{
    Mp4,
    Mkv,
    WebM,
}

/// <summary>Where the scene's assets come from (written by the conversion pipeline).</summary>
public sealed record SourceSpec
{
    /// <summary>Path of the transport mesh (binary PLY) to import.</summary>
    public string PlyPath { get; init; } = "";

    /// <summary>Path of the scene-meta.json sidecar, when available (labels/overlays
    /// consume it in later batches; the self-contained mode embeds it).</summary>
    public string? SceneMetaPath { get; init; }

    /// <summary>Self-contained script (D-14): embed scene-meta into the .py so the
    /// script is portable without its sidecars.</summary>
    public bool SelfContained { get; init; }

    /// <summary>Additionally embed the PLY itself (base64) — a single mailable .py.
    /// Only meaningful with <see cref="SelfContained"/>.</summary>
    public bool EmbedMesh { get; init; }
}

/// <summary>Engine + device configuration (D-09, FR-07/08).</summary>
public sealed record EngineSpec
{
    public RenderEngineKind Kind { get; init; } = RenderEngineKind.Cycles;

    /// <summary>Cycles sample count (EEVEE uses its own TAA sample interpretation).</summary>
    public int Samples { get; init; } = 128;

    /// <summary>Enable the denoiser (Cycles).</summary>
    public bool Denoise { get; init; } = true;

    /// <summary>Render with a transparent background (film transparency).</summary>
    public bool TransparentBackground { get; init; }

    /// <summary>Requested compute device; the script cascades and reports the one
    /// actually enabled (<c>THIDE:device=</c>). Ignored for EEVEE.</summary>
    public GpuMode Gpu { get; init; } = GpuMode.Auto;
}

/// <summary>Camera configuration (BA-B6). <see cref="Template"/> picks the motion; the
/// matching per-template params object carries its knobs (null ⇒ template defaults). The
/// core's <see cref="CameraTemplate.Static"/> camera uses only <see cref="FocalLength"/>
/// and <see cref="AutoFramePadding"/> (runtime auto-framed, no keyframes).</summary>
public sealed record CameraSpec
{
    /// <summary>Which camera motion to generate (FR-04).</summary>
    public CameraTemplate Template { get; init; } = CameraTemplate.Static;

    /// <summary>Focal length in millimetres (the base focal; viewpoints may override).</summary>
    public double FocalLength { get; init; } = 35.0;

    /// <summary>Multiplier on the auto-framing distance (1 = model exactly fills the
    /// frame; larger backs the camera off).</summary>
    public double AutoFramePadding { get; init; } = 1.15;

    /// <summary>Depth-of-field settings; focus tracks the look-at target.</summary>
    public DepthOfFieldSpec Dof { get; init; } = new();

    /// <summary>Turntable knobs (used when <see cref="Template"/> is
    /// <see cref="CameraTemplate.Orbit"/>; null ⇒ defaults).</summary>
    public OrbitParams? Orbit { get; init; }

    /// <summary>Helical-descent knobs (<see cref="CameraTemplate.Helix"/>).</summary>
    public HelixParams? Helix { get; init; }

    /// <summary>Flythrough knobs (<see cref="CameraTemplate.Flythrough"/>).</summary>
    public FlythroughParams? Flythrough { get; init; }

    /// <summary>Viewpoint-sequence knobs (<see cref="CameraTemplate.Viewpoints"/>).</summary>
    public ViewpointParams? Viewpoints { get; init; }

    /// <summary>Still-set knobs (<see cref="CameraTemplate.StillSet"/>).</summary>
    public StillSetParams? Stills { get; init; }
}

/// <summary>Timing for animated outputs (frame range = round(fps · duration)).</summary>
public sealed record AnimationSpec
{
    public int Fps { get; init; } = 30;

    public double DurationSeconds { get; init; } = 10.0;
}

/// <summary>What file(s) the render writes and at what resolution (FR-09).</summary>
public sealed record OutputSpec
{
    public OutputKind Kind { get; init; } = OutputKind.Video;

    /// <summary>Container for <see cref="OutputKind.Video"/>; ignored otherwise.</summary>
    public VideoContainer Container { get; init; } = VideoContainer.Mp4;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>Directory the render writes into.</summary>
    public string OutputDirectory { get; init; } = "";

    /// <summary>Base file name (no extension, no directory separators).</summary>
    public string BaseName { get; init; } = "cave-render";
}

/// <summary>
/// Complete, serializable description of one render job. See the file header for the
/// batch-by-batch growth plan.
/// </summary>
public sealed record SceneSpec
{
    /// <summary>Schema version for forward migration; defaults to the current module version.</summary>
    public int Version { get; init; } = BlenderModule.SceneSpecSchemaVersion;

    /// <summary>Determinism seed: same spec + seed ⇒ identical script bytes and (where
    /// Blender allows) identical placements (NFR-03).</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Optional human-readable job/preset name (shown in the UI and script header).</summary>
    public string? Name { get; init; }

    /// <summary>Optional provenance stamp (app version / user).</summary>
    public string? CreatedBy { get; init; }

    public SourceSpec Source { get; init; } = new();

    public EngineSpec Engine { get; init; } = new();

    public CameraSpec Camera { get; init; } = new();

    public AnimationSpec Animation { get; init; } = new();

    public OutputSpec Output { get; init; } = new();
}
