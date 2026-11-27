// What the reader gathers from a project before the merged file is written: the surveys (for name
// resolution), every shot reduced to metres and degrees, and the station-level commands — each station
// still as written, with the survey it was written in, so names are resolved only once every survey is
// known (as Therion resolves them, after reading everything).

using System.Collections.Immutable;
using Therion.Core;
using Therion.Semantics;

namespace Therion.Workspace.Flatten;

/// <summary>One survey of the source tree.</summary>
internal sealed class SurveyNode
{
    public SurveyNode(string name, SurveyNode? parent)
    {
        Name = name;
        Parent = parent;
        Path = parent is null ? ImmutableArray<string>.Empty : parent.Path.Add(name);
    }

    public string Name { get; }
    public SurveyNode? Parent { get; }
    /// <summary>Top-down names, the root (the implicit top level) being empty.</summary>
    public ImmutableArray<string> Path { get; }
    public Dictionary<string, SurveyNode> Children { get; } = new(StringComparer.Ordinal);
    public bool NamespaceOff { get; set; }
    public SurveyDeclination? Declination { get; set; }
    public string? Title { get; set; }
    public RawStation? Entrance { get; set; }

    public bool IsRoot => Parent is null;

    /// <summary>The survey whose namespace the stations written here belong to (<c>-namespace off</c> passes them up).</summary>
    public SurveyNode Namespace => NamespaceOff && Parent is not null ? Parent.Namespace : this;

    /// <summary>Therion's name of the survey: innermost first, dot-separated (<c>sump.cave</c>).</summary>
    public string TherionName => string.Join('.', Path.Reverse());

    public SurveyNode Child(string name)
    {
        if (!Children.TryGetValue(name, out var child))
            Children[name] = child = new SurveyNode(name, this);
        return child;
    }
}

/// <summary>A survey's <c>-declination</c>: one value for undated data and/or a table of dated values.</summary>
internal sealed record SurveyDeclination(double? Undated, ImmutableArray<(double Year, double Degrees)> Table)
{
    /// <summary>
    /// The declination for data of <paramref name="year"/> (null: undated), as Therion evaluates it:
    /// undated data takes the undated value, else the table at the middle of its years; a date takes
    /// the table interpolated linearly, held at its first and last value outside it.
    /// </summary>
    public double Evaluate(double? year, out bool usedMiddle)
    {
        usedMiddle = false;
        if (year is null && Undated is { } u) return u;
        if (Table.IsDefaultOrEmpty) return Undated ?? 0.0;
        double x;
        if (year is { } y) x = y;
        else
        {
            usedMiddle = true;
            x = (Table.Min(t => t.Year) + Table.Max(t => t.Year)) / 2.0;
        }
        if (Table.Length == 1 || x <= Table[0].Year) return Table[0].Degrees;
        if (x >= Table[^1].Year) return Table[^1].Degrees;
        for (int i = 1; i < Table.Length; i++)
            if (Table[i].Year >= x)
            {
                var (x1, y1) = Table[i - 1];
                var (x2, y2) = Table[i];
                return x1 == x2 ? y1 : y1 + (x - x1) * (y2 - y1) / (x2 - x1);
            }
        return Table[^1].Degrees;
    }
}

/// <summary>A station name as written (station-names prefix/suffix applied), with the survey it was written in.</summary>
internal readonly record struct RawStation(string Text, SurveyNode Scope, SourceSpan Span);

/// <summary>A resolved station: a point name in the namespace of a survey.</summary>
internal sealed record StationId(SurveyNode Namespace, string Point)
{
    public string TherionName => Namespace.IsRoot ? Point : Point + "@" + Namespace.TherionName;
    public override string ToString() => TherionName;
}

/// <summary>Up / down / left / right at one end of a shot (metres, NaN when not given).</summary>
internal readonly record struct Dims(double Up, double Down, double Left, double Right)
{
    public static Dims None { get; } = new(double.NaN, double.NaN, double.NaN, double.NaN);
    public bool IsEmpty => double.IsNaN(Up) && double.IsNaN(Down) && double.IsNaN(Left) && double.IsNaN(Right);
}

/// <summary>Extended-elevation bits, as Therion keeps them.</summary>
[Flags]
internal enum ExtendBits
{
    None = 0,
    Normal = 1,
    Reverse = 2,
    Left = 4,
    Right = 8,
    Vertical = 16,
    Direction = Normal | Reverse | Left | Right | Vertical,
    Start = 64,
    Ignore = 128,
    Hide = 256,
}

/// <summary>The state in force at a shot that is written as state commands, not in the row.</summary>
internal sealed record LegState(
    bool InferPlumbs,
    bool InferEquates,
    string? Walls,
    string? VThreshold,
    string SdSignature,
    ImmutableArray<string> SdCommands,
    ExtendBits ExtendDirection,
    double ExtendRatio)
{
    public static LegState Default { get; } = new(false, false, null, null, "", ImmutableArray<string>.Empty,
        ExtendBits.Normal, double.NaN);
}

internal enum LegKind { Polar, NoSurvey }

/// <summary>A shot reduced to metres and degrees: calibration, units, backsights and the data style resolved.</summary>
internal sealed class FlatLeg
{
    public RawStation? From;
    public RawStation? To;
    /// <summary>"-" or "." for an unnamed end (a splay); null when the end is a station.</summary>
    public string? FromAnonymous;
    public string? ToAnonymous;
    public LegKind Kind;
    /// <summary>The style the shot is written in: normal, diving, cylpolar or cartesian (topofil is normal).</summary>
    public Therion.Syntax.DataStyle Style = Therion.Syntax.DataStyle.Normal;
    /// <summary>The length along the shot, metres (for cylpolar the slope length worked out from the two readings).</summary>
    public double Length = double.NaN;
    /// <summary>Diving and cylpolar: the depth change (metres, as Therion signs it) and, for cylpolar, the horizontal length.</summary>
    public double DepthChange = double.NaN;
    public double HorizontalLength = double.NaN;
    /// <summary>Cartesian: east / north / up, metres, before any declination.</summary>
    public double Dx, Dy, Dz;
    /// <summary>Degrees from north as read (no declination); NaN when not read.</summary>
    public double Bearing = double.NaN;
    /// <summary>Degrees; +∞ / −∞ for a shot read as <c>up</c> / <c>down</c>.</summary>
    public double Gradient = double.NaN;
    /// <summary>The flags written with <c>flags</c> (not the implicit splay of an unnamed end).</summary>
    public ShotFlags Flags;
    /// <summary>The centreline's <c>declination</c>/<c>grid-angle</c> or the survey's, degrees; NaN when none.</summary>
    public double DeclaredDeclination = double.NaN;
    public bool DeclaredFromSurvey;
    public bool GridNorth;
    public string? GridCs;
    public ExtendBits ExtendOnce;      // start / ignore / hide on this shot
    public required CentrelineInfo Centreline;
    public required LegState State;
    public Dims FromDims = Dims.None;
    public Dims ToDims = Dims.None;
    public string? Comment;
    public SourceSpan Span;

    // Worked out after reading.
    public double AppliedDeclination;
    public FlattenDeclinationSource DeclinationSource;
    public StationId? FromId;
    public StationId? ToId;
    public bool Dropped;

    public bool IsSplay => FromAnonymous is not null || ToAnonymous is not null || (Flags & ShotFlags.Splay) != 0;
}

/// <summary>One source <c>centreline</c> block.</summary>
internal sealed class CentrelineInfo
{
    public required int Id;
    public required SurveyNode Survey;
    public required SourceSpan Span;
    public List<string> Dates { get; } = new();
    public double? StartYear;
    public double? EndYear;
    public List<string> Team { get; } = new();
    public List<string> ExploDates { get; } = new();
    public List<string> ExploTeam { get; } = new();
    public List<string> Instruments { get; } = new();

    /// <summary>The middle of the date range, as Therion takes it for the declination; null when undated.</summary>
    public double? AverageYear => StartYear is { } s ? (EndYear is { } e ? (s + e) / 2.0 : s) : null;

    public string? DateText => Dates.Count == 0 ? null : string.Join(", ", Dates);
}

internal sealed record FixRecord(RawStation Station, string? Cs, ImmutableArray<string> Values, SourceSpan Span)
{
    public StationId? Id { get; set; }
}

internal sealed record StationCommandRecord(RawStation Station, string Rest, SourceSpan Span)
{
    public StationId? Id { get; set; }
}

internal sealed record MarkRecord(ImmutableArray<RawStation> Stations, string Type, SourceSpan Span);

internal sealed record ExtendRecord(string Spec, ImmutableArray<RawStation> Stations, SourceSpan Span);

internal sealed record DimsRecord(RawStation Station, Dims Dims, SourceSpan Span);

internal sealed record EquateRecordRaw(ImmutableArray<RawStation> Stations, SourceSpan Span);

/// <summary>Everything read from the project.</summary>
internal sealed class FlatProject
{
    public SurveyNode Root { get; } = new("", null);
    public List<string> Files { get; } = new();
    public List<CentrelineInfo> Centrelines { get; } = new();
    public List<FlatLeg> Legs { get; } = new();
    public List<FixRecord> Fixes { get; } = new();
    public List<StationCommandRecord> StationCommands { get; } = new();
    public List<MarkRecord> Marks { get; } = new();
    public List<ExtendRecord> Extends { get; } = new();
    public List<DimsRecord> DimsRows { get; } = new();
    public List<EquateRecordRaw> Equates { get; } = new();
    /// <summary>The raw text of the <c>grade … endgrade</c> blocks the project defines.</summary>
    public List<string> GradeDefinitions { get; } = new();
    /// <summary>The configuration's <c>cs</c> (output coordinate system), if it sets one.</summary>
    public string? ConfigCs { get; set; }
    public List<Diagnostic> Diagnostics { get; } = new();

    public void Report(string code, DiagnosticSeverity severity, string message, SourceSpan span) =>
        Diagnostics.Add(Diagnostic.Create(code, severity, message, span));
}
