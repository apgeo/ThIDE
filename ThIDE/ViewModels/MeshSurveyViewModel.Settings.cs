// The settings half of the "Derive survey from 3-D model" dialog: every field the user can edit as
// text or a choice, how a preset fills them, and how they turn back into a MeshSurveyOptions with
// the messages for what could not be read or does not make sense together.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.MeshSurvey;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class MeshSurveyViewModel
{
    // ----- choices for the combo boxes -----
    public IReadOnlyList<Choice<string>> Presets { get; } = MeshSurveyChoices.Presets();
    public IReadOnlyList<Choice<ModelLengthUnit>> UnitChoices { get; } = MeshSurveyChoices.Of<ModelLengthUnit>("Units");
    public IReadOnlyList<Choice<ModelUpAxisChoice>> UpAxisChoices { get; } = MeshSurveyChoices.Of<ModelUpAxisChoice>("UpAxis");
    public IReadOnlyList<Choice<LegStrategy>> LegStrategies { get; } = MeshSurveyChoices.Of<LegStrategy>("LegStrategy");
    public IReadOnlyList<Choice<AnchorMode>> AnchorModes { get; } = MeshSurveyChoices.Of<AnchorMode>("Anchor");
    public IReadOnlyList<Choice<LateralDepthChoice>> DepthChoices { get; } = MeshSurveyChoices.Of<LateralDepthChoice>("Depth");
    public IReadOnlyList<Choice<LoopPolicy>> LoopPolicies { get; } = MeshSurveyChoices.Of<LoopPolicy>("Loops");
    public IReadOnlyList<Choice<SplayMode>> SplayModes { get; } = MeshSurveyChoices.Of<SplayMode>("Splay");
    public IReadOnlyList<Choice<ChamberPolicy>> ChamberPolicies { get; } = MeshSurveyChoices.Of<ChamberPolicy>("Chamber");
    public IReadOnlyList<Choice<StationNaming>> NamingChoices { get; } = MeshSurveyChoices.Of<StationNaming>("Naming");

    // ----- model input -----
    [ObservableProperty] private ModelLengthUnit _units = ModelLengthUnit.Auto;
    [ObservableProperty] private ModelUpAxisChoice _upAxis = ModelUpAxisChoice.Auto;
    [ObservableProperty] private string _northRotation = "0";
    [ObservableProperty] private string _coordinateSystem = "";

    // ----- preset -----
    [ObservableProperty] private string? _selectedPreset;

    // ----- legs -----
    [ObservableProperty] private string _legPreferred = "5";
    [ObservableProperty] private string _legMin = "1";
    [ObservableProperty] private string _legMax = "15";
    [ObservableProperty] private string _legMaxVertical = "";
    [ObservableProperty] private LegStrategy _legStrategy = LegStrategy.Preferred;
    [ObservableProperty] private string _legClearance = "0.15";

    // ----- stations -----
    [ObservableProperty] private AnchorMode _anchorMode = AnchorMode.Axial;
    [ObservableProperty] private string _floorOffset = "0";

    // ----- laterals -----
    [ObservableProperty] private LateralDepthChoice _lateralDepth = LateralDepthChoice.Unlimited;
    [ObservableProperty] private string _lateralMinLength = "3";
    [ObservableProperty] private string _lateralMinWidth = "0.5";
    [ObservableProperty] private string _lateralMaxCount = "";
    [ObservableProperty] private string _lateralLengthBudget = "";
    [ObservableProperty] private LoopPolicy _loopPolicy = LoopPolicy.Close;
    [ObservableProperty] private bool _sideStations = true;

    // ----- coverage -----
    [ObservableProperty] private double _coverageTargetPercent = 90;
    [ObservableProperty] private string _coverageTargetText = "90";
    [ObservableProperty] private string _coverageMinFeature = "1";
    [ObservableProperty] private string _coverageRange = "25";

    // ----- splays -----
    [ObservableProperty] private SplayMode _splayMode = SplayMode.Ring;
    [ObservableProperty] private string _splayCount = "8";
    [ObservableProperty] private bool _splayScaleBySize = true;

    // ----- void and chambers -----
    [ObservableProperty] private string _voxelSize = "0";
    [ObservableProperty] private string _minPassageWidth = "0.25";
    [ObservableProperty] private string _minPillarSize = "0.4";
    [ObservableProperty] private ChamberPolicy _chamberPolicy = ChamberPolicy.Hub;

    // ----- readings and output -----
    [ObservableProperty] private bool _syntheticNoise;
    [ObservableProperty] private StationNaming _naming = StationNaming.Sequential;
    [ObservableProperty] private string _stationsPerSurvey = "";
    [ObservableProperty] private bool _cartesianReadings;
    [ObservableProperty] private bool _writeDebugLayers;

    // ----- validation -----
    [ObservableProperty] private string _issueText = "";
    public bool HasIssues => IssueText.Length > 0;

    /// <summary>The settings a preset does not set (and the dialog has no field for) stay as they were loaded.</summary>
    private MeshSurveyOptions _baseline = new();
    private bool _loading;
    private bool _syncingCoverage;

    // Properties whose change means the options (or the model's input) must be read again.
    private static readonly HashSet<string> SettingNames =
    [
        nameof(Units), nameof(UpAxis), nameof(NorthRotation), nameof(CoordinateSystem),
        nameof(LegPreferred), nameof(LegMin), nameof(LegMax), nameof(LegMaxVertical), nameof(LegStrategy), nameof(LegClearance),
        nameof(AnchorMode), nameof(FloorOffset),
        nameof(LateralDepth), nameof(LateralMinLength), nameof(LateralMinWidth), nameof(LateralMaxCount), nameof(LateralLengthBudget),
        nameof(LoopPolicy), nameof(SideStations),
        nameof(CoverageTargetPercent), nameof(CoverageMinFeature), nameof(CoverageRange),
        nameof(SplayMode), nameof(SplayCount), nameof(SplayScaleBySize),
        nameof(VoxelSize), nameof(MinPassageWidth), nameof(MinPillarSize), nameof(ChamberPolicy),
        nameof(SyntheticNoise), nameof(Naming), nameof(StationsPerSurvey), nameof(CartesianReadings),
    ];

    private static readonly HashSet<string> InputNames = [nameof(Units), nameof(UpAxis), nameof(NorthRotation), nameof(CoordinateSystem)];

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        var name = e.PropertyName;
        if (name is null || _loading) return;
        if (name == nameof(LegClearance)) RecheckAllLegs();
        if (SettingNames.Contains(name) || name == nameof(LegClearance)) RefreshIssues();
        if (_session is not null && InputNames.Contains(name)) InputChanged = true;
    }

    partial void OnSelectedPresetChanged(string? value)
    {
        if (value is null || MeshSurveyPresets.Get(value) is not { } preset) return;
        LoadFrom(preset);
    }

    partial void OnCoverageTargetPercentChanged(double value)
    {
        if (_syncingCoverage) return;
        _syncingCoverage = true;
        CoverageTargetText = value.ToString("0.#", CultureInfo.InvariantCulture);
        _syncingCoverage = false;
    }

    partial void OnCoverageTargetTextChanged(string value)
    {
        if (_syncingCoverage) return;
        _syncingCoverage = true;
        if (GuidePointRow.TryParse(value, out var v)) CoverageTargetPercent = Math.Clamp(v, 0, 100);
        _syncingCoverage = false;
        // An unreadable or out-of-range text is reported when the options are read.
        if (!_loading) RefreshIssues();
    }

    /// <summary>Puts a whole set of options into the fields (the model's input fields are left alone).</summary>
    public void LoadFrom(MeshSurveyOptions o)
    {
        _loading = true;
        try
        {
            _baseline = o;
            LegPreferred = Num(o.Legs.Preferred);
            LegMin = Num(o.Legs.Min);
            LegMax = Num(o.Legs.Max);
            LegMaxVertical = o.Legs.MaxVertical is { } mv ? Num(mv) : "";
            LegStrategy = o.Legs.Strategy;
            LegClearance = Num(o.Legs.Clearance);
            AnchorMode = o.Anchor.Mode;
            FloorOffset = Num(o.Anchor.FloorOffset);
            LateralDepth = o.Laterals.MaxDepth switch { 0 => LateralDepthChoice.Mainline, 1 => LateralDepthChoice.One, 2 => LateralDepthChoice.Two, _ => LateralDepthChoice.Unlimited };
            LateralMinLength = Num(o.Laterals.MinLength);
            LateralMinWidth = Num(o.Laterals.MinWidth);
            LateralMaxCount = o.Laterals.MaxCount is { } mc ? mc.ToString(CultureInfo.InvariantCulture) : "";
            LateralLengthBudget = o.Laterals.MaxTotalLength is { } mt ? Num(mt) : "";
            LoopPolicy = o.Laterals.Loops;
            SideStations = o.Laterals.SideStations;
            CoverageTargetPercent = Math.Round(o.Coverage.Target * 100, 1);
            CoverageTargetText = CoverageTargetPercent.ToString("0.#", CultureInfo.InvariantCulture);
            CoverageMinFeature = Num(o.Coverage.MinFeature);
            CoverageRange = Num(o.Coverage.Range);
            SplayMode = o.Splays.Mode;
            SplayCount = o.Splays.Count.ToString(CultureInfo.InvariantCulture);
            SplayScaleBySize = o.Splays.ScaleBySize;
            VoxelSize = Num(o.Void.VoxelSize);
            MinPassageWidth = Num(o.Void.MinPassageWidth);
            MinPillarSize = Num(o.Void.MinPillarSize);
            ChamberPolicy = o.Skeleton.ChamberPolicy;
            SyntheticNoise = o.Readings.Noise is not null;
            Naming = o.Output.Naming;
            StationsPerSurvey = o.Output.StationsPerSurvey is { } sp ? sp.ToString(CultureInfo.InvariantCulture) : "";
            CartesianReadings = o.Readings.Style == ReadingStyle.Cartesian;
        }
        finally { _loading = false; }
        RecheckAllLegs();
        RefreshIssues();
    }

    private static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>The model's input settings from the fields; unreadable ones are reported in <paramref name="issues"/>.</summary>
    public InputOptions BuildInput(List<string> issues)
    {
        var rotation = _baseline.Input.NorthRotation;
        if (GuidePointRow.TryParse(NorthRotation, out var r)) rotation = r;
        else issues.Add(NotANumber("MeshSurvey_Input_North"));
        return _baseline.Input with
        {
            Units = Units,
            UpAxis = UpAxis,
            NorthRotation = rotation,
            CoordinateSystem = string.IsNullOrWhiteSpace(CoordinateSystem) ? null : CoordinateSystem.Trim(),
        };
    }

    /// <summary>All settings from the fields; what cannot be read, and what the library finds wrong, goes to <paramref name="issues"/>.</summary>
    public MeshSurveyOptions BuildOptions(List<string> issues)
    {
        var o = _baseline;
        int before = issues.Count;
        double D(string text, string label, double fallback)
        {
            if (GuidePointRow.TryParse(text, out var v)) return v;
            issues.Add(NotANumber(label));
            return fallback;
        }
        double? DOpt(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return GuidePointRow.TryParse(text, out var v) ? v : Fail(label);
        }
        double? Fail(string label) { issues.Add(NotANumber(label)); return null; }
        int? IOpt(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            issues.Add(NotANumber(label));
            return null;
        }

        var legs = o.Legs with
        {
            Strategy = LegStrategy,
            Preferred = D(LegPreferred, "MeshSurvey_Legs_Preferred", o.Legs.Preferred),
            Min = D(LegMin, "MeshSurvey_Legs_Min", o.Legs.Min),
            Max = D(LegMax, "MeshSurvey_Legs_Max", o.Legs.Max),
            MaxVertical = DOpt(LegMaxVertical, "MeshSurvey_Legs_MaxVertical"),
            Clearance = D(LegClearance, "MeshSurvey_Legs_Clearance", o.Legs.Clearance),
        };
        if (!GuidePointRow.TryParse(CoverageTargetText, out var targetPercent)) { issues.Add(NotANumber("MeshSurvey_Coverage_Target")); targetPercent = CoverageTargetPercent; }
        var input = BuildInput(issues);
        var result = o with
        {
            Input = input,
            Void = o.Void with
            {
                VoxelSize = D(VoxelSize, "MeshSurvey_Void_Voxel", o.Void.VoxelSize),
                MinPassageWidth = D(MinPassageWidth, "MeshSurvey_Void_PassageWidth", o.Void.MinPassageWidth),
                MinPillarSize = D(MinPillarSize, "MeshSurvey_Void_Pillar", o.Void.MinPillarSize),
            },
            Skeleton = o.Skeleton with { ChamberPolicy = ChamberPolicy },
            Laterals = o.Laterals with
            {
                MaxDepth = LateralDepth switch { LateralDepthChoice.Mainline => 0, LateralDepthChoice.One => 1, LateralDepthChoice.Two => 2, _ => null },
                MinLength = D(LateralMinLength, "MeshSurvey_Laterals_MinLength", o.Laterals.MinLength),
                MinWidth = D(LateralMinWidth, "MeshSurvey_Laterals_MinWidth", o.Laterals.MinWidth),
                MaxCount = IOpt(LateralMaxCount, "MeshSurvey_Laterals_MaxCount"),
                MaxTotalLength = DOpt(LateralLengthBudget, "MeshSurvey_Laterals_Budget"),
                Loops = LoopPolicy,
                SideStations = SideStations,
            },
            Coverage = o.Coverage with
            {
                Target = targetPercent / 100.0,
                MinFeature = D(CoverageMinFeature, "MeshSurvey_Coverage_MinFeature", o.Coverage.MinFeature),
                Range = D(CoverageRange, "MeshSurvey_Coverage_Range", o.Coverage.Range),
            },
            Legs = legs,
            Anchor = o.Anchor with { Mode = AnchorMode, FloorOffset = D(FloorOffset, "MeshSurvey_Anchor_FloorOffset", o.Anchor.FloorOffset) },
            Splays = o.Splays with
            {
                Mode = SplayMode,
                Count = IOpt(SplayCount, "MeshSurvey_Splays_Count") ?? o.Splays.Count,
                ScaleBySize = SplayScaleBySize,
            },
            Readings = o.Readings with
            {
                Style = CartesianReadings ? ReadingStyle.Cartesian : ReadingStyle.Normal,
                Noise = SyntheticNoise ? o.Readings.Noise ?? new NoiseOptions() : null,
            },
            Output = o.Output with
            {
                Naming = Naming,
                StationsPerSurvey = IOpt(StationsPerSurvey, "MeshSurvey_Output_PerSurvey"),
            },
        };
        // The library's own checks only make sense when everything was read.
        if (issues.Count == before) issues.AddRange(result.Validate());
        return result;
    }

    private static string NotANumber(string labelKey) => string.Format(CultureInfo.InvariantCulture, Tr.Get("MeshSurvey_Issue_NotNumber"), Tr.Get(labelKey));

    /// <summary>Reads the fields again and shows what is wrong with them.</summary>
    public void RefreshIssues()
    {
        var issues = new List<string>();
        BuildOptions(issues);
        issues.AddRange(GuideIssues());
        IssueText = string.Join(Environment.NewLine, issues.Distinct());
        OnPropertyChanged(nameof(HasIssues));
        UpdateCommands();
    }
}
