// Small row view models of the "Derive survey from 3-D model" dialog: a translated choice for the
// combo boxes, one guide point, and one guide leg with its live check status.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Cave3D;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Plan;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

/// <summary>A value with its translated label, for a combo box.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>How deep the laterals go; the four choices of the depth combo box.</summary>
public enum LateralDepthChoice { Mainline, One, Two, Unlimited }

public static class MeshSurveyChoices
{
    /// <summary>One choice per value of the enum, labelled by the resource <c>MeshSurvey_{group}_{value}</c>.</summary>
    public static IReadOnlyList<Choice<T>> Of<T>(string group) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new Choice<T>(v, Label(group, v.ToString()))).ToList();

    public static string Label(string group, string value) => Tr.Get($"MeshSurvey_{group}_{value}");

    public static IReadOnlyList<Choice<string>> Presets() =>
        MeshSurveyPresets.Names.Select(n => new Choice<string>(n, Label("Preset", n))).ToList();
}

/// <summary>The traffic-light of a leg check: green, amber, red, or grey when there is nothing to show.</summary>
public enum LegStatusKind { None, Pending, Ok, Warn, Bad }

public sealed partial class GuidePointRow : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _x = "0";
    [ObservableProperty] private string _y = "0";
    [ObservableProperty] private string _z = "0";
    [ObservableProperty] private GuideSnap _snap = GuideSnap.None;

    /// <summary>Raised with the old name when the name changes (legs follow the rename).</summary>
    internal Action<GuidePointRow, string>? Renamed;

    /// <summary>Raised when the position or the snap changes (the legs using the point are checked again).</summary>
    internal Action<GuidePointRow>? Moved;

    public GuidePointRow(string name) => _name = name;

    partial void OnNameChanged(string? oldValue, string newValue) { if (oldValue is not null) Renamed?.Invoke(this, oldValue); }
    partial void OnXChanged(string value) => Moved?.Invoke(this);
    partial void OnYChanged(string value) => Moved?.Invoke(this);
    partial void OnZChanged(string value) => Moved?.Invoke(this);
    partial void OnSnapChanged(GuideSnap value) => Moved?.Invoke(this);

    public bool TryGetPosition(out CaveVector3 position)
    {
        position = default;
        if (!TryParse(X, out var x) || !TryParse(Y, out var y) || !TryParse(Z, out var z)) return false;
        position = new CaveVector3(x, y, z);
        return true;
    }

    public void SetPosition(CaveVector3 p)
    {
        X = Format(p.X);
        Y = Format(p.Y);
        Z = Format(p.Z);
    }

    internal static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    internal static string Format(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed partial class GuideLegRow : ObservableObject
{
    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
    [ObservableProperty] private bool _force;
    [ObservableProperty] private LegStatusKind _statusKind = LegStatusKind.None;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusColor = Colors.None;

    /// <summary>The last finished check; null while there is none.</summary>
    public LegCheck? Check { get; private set; }

    internal Action<GuideLegRow>? Changed;
    internal System.Threading.CancellationTokenSource? Cts;

    private static class Colors
    {
        public const string None = "#8A8A8A", Ok = "#2E9E4F", Warn = "#E0A100", Bad = "#D13438";
    }

    partial void OnFromChanged(string value) => Changed?.Invoke(this);
    partial void OnToChanged(string value) => Changed?.Invoke(this);

    public void SetNone(string text) => Set(null, LegStatusKind.None, text);

    public void SetPending() => Set(null, LegStatusKind.Pending, Tr.Get("MeshSurvey_Leg_Checking"));

    public void SetCheck(LegCheck check, bool rejected = false)
    {
        var kind = check.Status switch
        {
            LegCheckStatus.Clear => LegStatusKind.Ok,
            LegCheckStatus.Tight or LegCheckStatus.OutOfRange => LegStatusKind.Warn,
            _ => LegStatusKind.Bad,
        };
        if (rejected) kind = LegStatusKind.Bad;
        Set(check, kind, Describe(check));
    }

    private void Set(LegCheck? check, LegStatusKind kind, string text)
    {
        Check = check;
        StatusKind = kind;
        StatusText = text;
        StatusColor = kind switch
        {
            LegStatusKind.Ok => Colors.Ok,
            LegStatusKind.Warn => Colors.Warn,
            LegStatusKind.Bad => Colors.Bad,
            _ => Colors.None,
        };
    }

    private static string Describe(LegCheck c)
    {
        var inv = CultureInfo.InvariantCulture;
        return c.Status switch
        {
            LegCheckStatus.Clear => Tr.Get("MeshSurvey_Leg_Clear"),
            LegCheckStatus.Tight => string.Format(inv, Tr.Get("MeshSurvey_Leg_Tight"), c.MinClearance, c.MinClearanceAt),
            LegCheckStatus.OutOfRange => string.Format(inv, Tr.Get(c.TooShort ? "MeshSurvey_Leg_TooShort" : "MeshSurvey_Leg_TooLong"), c.Length),
            LegCheckStatus.Blocked => string.Format(inv, Tr.Get("MeshSurvey_Leg_Blocked"), c.FirstHit ?? 0, c.Crossings, c.RockThickness),
            LegCheckStatus.EndpointOutside => Tr.Get(c.FromInVoid ? "MeshSurvey_Leg_EndOutside" : "MeshSurvey_Leg_StartOutside"),
            _ => Tr.Get(c.FromInVoid ? "MeshSurvey_Leg_EndInRock" : "MeshSurvey_Leg_StartInRock"),
        };
    }
}
