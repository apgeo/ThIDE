// View model of the "Derive Survey from 3-D Model" dialog: choose a model, adjust the settings, give
// the planner an optional guide, run it in the background, look at the report, preview the result in
// the 3-D viewer and save it as a .th with its report. The work itself is done by Therion.MeshSurvey
// and Therion.Workspace; this class only gathers input, runs it off the UI thread and shows results.
// Settings are in MeshSurveyViewModel.Settings.cs, the guide and its live check in .Guide.cs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Cave3D.Parsing;
using Therion.MeshSurvey;
using Therion.MeshSurvey.Output;
using Therion.MeshSurvey.Plan;
using Therion.Workspace.Import;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public sealed partial class MeshSurveyViewModel : ObservableObject
{
    private readonly IStoragePicker? _picker;
    private readonly INotificationService? _notifications;
    private readonly Func<string, Task>? _openDocument;
    private readonly Action<string>? _showPreview;

    private MeshSurveySession? _session;
    private InputOptions? _openedInput;
    private int _openGeneration;
    private CancellationTokenSource? _runCts;
    private MeshSurveyImportResult? _result;

    // ----- model and what was found out about it -----
    [ObservableProperty] private string _modelPath = "";
    [ObservableProperty] private bool _isOpening;
    [ObservableProperty] private string _inspectSummary = "";
    [ObservableProperty] private bool _inputChanged;
    public InspectReport? Inspect { get; private set; }

    // ----- run -----
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _reportSummary = "";
    [ObservableProperty] private bool _hasResult;

    /// <summary>The plan report of the last successful run.</summary>
    public PlanReport? ReportPlan => _result?.Survey.Report.Plan;
    public MeshSurveyImportResult? Result => _result;

    /// <summary>The .lox written for the last preview.</summary>
    public string? PreviewPath { get; private set; }

    /// <summary>The .th written by the last save.</summary>
    public string? SavedPath { get; private set; }

    public bool HasModel => _session is not null;
    public string? ModelFolder => string.IsNullOrWhiteSpace(ModelPath) ? null : Path.GetDirectoryName(Path.GetFullPath(ModelPath));

    /// <summary>Designer / fallback constructor.</summary>
    public MeshSurveyViewModel() : this(null, null, null, null) { }

    /// <param name="picker">Open and save dialogs.</param>
    /// <param name="notifications">Where toasts go.</param>
    /// <param name="openDocument">Opens the saved .th in the editor.</param>
    /// <param name="showPreview">Shows a preview .lox in the 3-D viewer.</param>
    /// <param name="checkDebounce">Pause before a changed leg is checked (default 300 ms).</param>
    public MeshSurveyViewModel(IStoragePicker? picker, INotificationService? notifications,
        Func<string, Task>? openDocument, Action<string>? showPreview, TimeSpan? checkDebounce = null)
    {
        _picker = picker;
        _notifications = notifications;
        _openDocument = openDocument;
        _showPreview = showPreview;
        CheckDebounce = checkDebounce ?? TimeSpan.FromMilliseconds(300);
        StartPoint = Hook(new GuidePointRow("start"));
        _selectedPreset = MeshSurveyPresets.Names[0];
        LoadFrom(MeshSurveyPresets.Get(_selectedPreset)!);
        GuideStructureChanged(recheck: false);
    }

    private void UpdateCommands()
    {
        RunCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => UpdateCommands();
    partial void OnIsOpeningChanged(bool value) => UpdateCommands();
    partial void OnHasResultChanged(bool value) => UpdateCommands();

    private void Fail(Exception ex)
    {
        ErrorMessage = ex.Message;
        _notifications?.Error(Tr.Get("MeshSurvey_Title"), ex.Message);
    }

    // ----- the model -----

    [RelayCommand]
    public async Task BrowseAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickOpenFileAsync(Tr.Get("MeshSurvey_BrowseTitle")).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        ModelPath = path;
        await OpenModelAsync().ConfigureAwait(true);
    }

    /// <summary>Reads the model in <see cref="ModelPath"/> with the input settings and shows what is known about it.</summary>
    [RelayCommand]
    public async Task OpenModelAsync()
    {
        var path = ModelPath.Trim();
        ErrorMessage = "";
        if (!File.Exists(path)) { ErrorMessage = Tr.Get("MeshSurvey_Issue_NoFile"); return; }
        var issues = new List<string>();
        var input = BuildInput(issues);
        var options = BuildOptions(issues);
        if (issues.Count > 0) { ErrorMessage = string.Join(Environment.NewLine, issues); return; }

        int generation = ++_openGeneration;
        IsOpening = true;
        try
        {
            var (session, inspect) = await Task.Run(() =>
            {
                var s = MeshSurveySession.Open(path, input);
                return (s, s.Inspect(options));
            }).ConfigureAwait(true);
            if (generation != _openGeneration) return;
            _session = session;
            _openedInput = input;
            Inspect = inspect;
            InspectSummary = FormatInspect(inspect);
            InputChanged = false;
            ClearResult();
            OnPropertyChanged(nameof(HasModel));
            RecheckAllLegs();
        }
        catch (Exception ex)
        {
            if (generation != _openGeneration) return;
            _session = null;
            Inspect = null;
            InspectSummary = "";
            OnPropertyChanged(nameof(HasModel));
            Fail(ex);
            RecheckAllLegs();
        }
        finally { if (generation == _openGeneration) IsOpening = false; }
    }

    private static string FormatInspect(InspectReport r)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Inspect_Size"), r.Size.X, r.Size.Y, r.Size.Z));
        var unit = MeshSurveyChoices.Label("Units", r.Normalization.Units.ToString());
        sb.AppendLine(string.Format(inv, Tr.Get(r.Normalization.UnitsGuessed ? "MeshSurvey_Inspect_UnitsGuessed" : "MeshSurvey_Inspect_Units"), unit));
        sb.AppendLine(string.Format(inv, Tr.Get(r.IsPointCloud ? "MeshSurvey_Inspect_Points" : "MeshSurvey_Inspect_Triangles"), r.IsPointCloud ? r.Points : r.Triangles));
        if (!r.IsPointCloud)
        {
            sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Inspect_Holes"), r.Holes, r.LargestHoleArea));
            sb.AppendLine(r.ClosedSurfaceLoops is { } loops
                ? string.Format(inv, Tr.Get("MeshSurvey_Inspect_Loops"), loops)
                : Tr.Get("MeshSurvey_Inspect_LoopsUnknown"));
        }
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Inspect_Voxels"), r.VoxelSize, r.EstimatedVoxels, r.EstimatedBytes / (1024.0 * 1024.0)));
        foreach (var w in r.Warnings.Concat(r.Normalization.Warnings).Distinct()) sb.AppendLine("• " + w);
        return sb.ToString().TrimEnd();
    }

    // ----- run -----

    private bool CanRun() => _session is not null && !IsRunning && !IsOpening && !HasIssues;

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task RunAsync()
    {
        if (_session is not { } session || IsRunning) return;
        var issues = new List<string>();
        var options = BuildOptions(issues);
        issues.AddRange(GuideIssues());
        if (issues.Count > 0) { ErrorMessage = string.Join(Environment.NewLine, issues.Distinct()); return; }
        // The model was read with the input settings of that moment; the plan must use the same.
        options = options with { Input = _openedInput ?? options.Input };
        var guide = BuildGuide();

        ErrorMessage = "";
        ClearResult();
        foreach (var leg in Legs) leg.SetPending();
        IsRunning = true;
        ProgressValue = 0;
        ProgressText = Tr.Get("MeshSurvey_Progress_Start");
        using var cts = _runCts = new CancellationTokenSource();
        var token = cts.Token;
        var progress = new Progress<MeshSurveyProgress>(p =>
        {
            int index = Array.IndexOf(StageOrder, p.Stage);
            if (index >= 0) ProgressValue = 100.0 * (index + Math.Clamp(p.Fraction, 0, 1)) / StageOrder.Length;
            ProgressText = string.Format(CultureInfo.InvariantCulture, Tr.Get("MeshSurvey_Progress_Stage"),
                Tr.Find("MeshSurvey_Stage_" + p.Stage) ?? p.Stage, p.Fraction * 100);
        });
        try
        {
            var result = await Task.Run(() =>
            {
                var cave = session.Prepare(options, guide, progress, token);
                return MeshSurveyImport.Plan(session, cave, options, guide, token);
            }, token).ConfigureAwait(true);
            _result = result;
            ReportSummary = FormatReport(result);
            HasResult = true;
            ProgressValue = 100;
            ProgressText = Tr.Get("MeshSurvey_Progress_Done");
            OnPropertyChanged(nameof(ReportPlan));
            OnPropertyChanged(nameof(Result));
            foreach (var g in result.Survey.Report.Plan.GuideLegs) ShowGuideLegResult(g, rejected: false);
        }
        catch (OperationCanceledException)
        {
            ProgressText = Tr.Get("MeshSurvey_Progress_Cancelled");
            RecheckAllLegs();
        }
        catch (GuideRejectedException ex)
        {
            ErrorMessage = ex.Message;
            ProgressText = Tr.Get("MeshSurvey_Progress_Rejected");
            RecheckAllLegs();
            foreach (var f in ex.Failures) ShowGuideLegResult(f, rejected: true);
        }
        catch (Exception ex)
        {
            ProgressText = "";
            Fail(ex);
            RecheckAllLegs();
        }
        finally
        {
            IsRunning = false;
            _runCts = null;
        }
    }

    private static readonly string[] StageOrder = ["void", "skeleton", "chambers", "plan", "splays"];

    private void ShowGuideLegResult(GuideLegResult g, bool rejected)
    {
        foreach (var leg in Legs.Where(l => l.From == g.From && l.To == g.To))
        {
            leg.Cts?.Cancel();
            leg.SetCheck(g.Check, rejected);
        }
    }

    [RelayCommand]
    public void Cancel() => _runCts?.Cancel();

    private void ClearResult()
    {
        _result = null;
        PreviewPath = null;
        ReportSummary = "";
        HasResult = false;
        OnPropertyChanged(nameof(ReportPlan));
        OnPropertyChanged(nameof(Result));
    }

    private static string FormatReport(MeshSurveyImportResult result)
    {
        var inv = CultureInfo.InvariantCulture;
        var plan = result.Survey.Report.Plan;
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Totals"), plan.Stations, plan.Legs, plan.Length, plan.MainlineLength));
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Laterals"), plan.Laterals, plan.LoopClosures, plan.SideStations));
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_LegLengths"), plan.LegMin, plan.LegMedian, plan.LegMax));
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Coverage"), plan.CoverageReached * 100, plan.CoverageTarget * 100));
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Stop"), MeshSurveyChoices.Label("Stop", plan.StopReason.ToString())));
        if (result.Survey.Report.Splays is { } splays)
            sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Splays"), splays.Splays, MeshSurveyChoices.Label("Splay", splays.Mode.ToString())));
        sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Problems"), plan.BlockedLegs, plan.TightLegs, plan.Relaxations.Count));
        if (plan.GuideLegs.Count > 0)
            sb.AppendLine(string.Format(inv, Tr.Get("MeshSurvey_Report_Guide"), plan.GuideLegsKept, plan.GuideLegs.Count));
        foreach (var w in result.GuideWarnings.Concat(plan.Warnings).Distinct()) sb.AppendLine("• " + w);
        return sb.ToString().TrimEnd();
    }

    // ----- preview and save -----

    /// <summary>Writes the result as a .lox in a temporary folder and hands it to the 3-D viewer.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    public async Task PreviewAsync()
    {
        if (_result is not { } result) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "ThIDE", "mesh-survey", Guid.NewGuid().ToString("N")[..8]);
            var path = await Task.Run(() =>
            {
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, ScaffoldValueRules.SafeSurveyName(result.Survey.Name) + ".lox");
                LoxWriter.WriteFile(PreviewModel.Build(result.Survey, result.Session), target);
                return target;
            }).ConfigureAwait(true);
            PreviewPath = path;
            _showPreview?.Invoke(path);
        }
        catch (Exception ex) { Fail(ex); }
    }

    /// <summary>Asks where to save, writes the .th and its report (and the debug layers when asked), and opens the .th.</summary>
    [RelayCommand(CanExecute = nameof(HasResult))]
    public async Task SaveAsync()
    {
        if (_picker is null || _result is not { } result) return;
        try
        {
            var stem = ScaffoldValueRules.SafeSurveyName(Path.GetFileNameWithoutExtension(ModelPath));
            var target = await _picker.PickSaveFileAsync(Tr.Get("MeshSurvey_SaveTitle"), stem + ".th", ModelFolder).ConfigureAwait(true);
            if (string.IsNullOrEmpty(target)) return;
            bool layers = WriteDebugLayers;
            await Task.Run(() =>
            {
                File.WriteAllText(target, result.Therion);
                File.WriteAllText(MeshSurveyImport.ReportPath(target), result.ReportJson);
                if (layers) MeshSurveyImport.WriteDebugLayers(result, target);
            }).ConfigureAwait(true);
            SavedPath = target;
            if (_openDocument is not null) await _openDocument(target).ConfigureAwait(true);
            _notifications?.Success(Tr.Get("MeshSurvey_Title"), target);
        }
        catch (Exception ex) { Fail(ex); }
    }
}
