// The "Log" document tab: everything TherionLogParser lifted out of a therion.log,
// laid out as titled sections of label/value rows. Every row, every section and the
// whole summary can be copied to the clipboard.
//
// Built off the UI thread (see FileDocumentViewModel.Reparse), so it holds no mutable
// Avalonia objects — the outcome badge uses an immutable brush.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels.Docking;

/// <summary>One label/value row. <see cref="CopyText"/> is what lands on the clipboard.</summary>
public sealed partial class LogFieldViewModel : ObservableObject
{
    public LogFieldViewModel(string label, string value, string? copyText = null, string? tooltip = null)
    {
        Label = label;
        Value = value;
        CopyText = copyText ?? value;
        Tooltip = tooltip;
    }

    public string Label { get; }
    public string Value { get; }
    public string CopyText { get; }
    public string? Tooltip { get; }

    [RelayCommand] private void Copy() => ClipboardHelper.SetText(CopyText);
}

public sealed partial class LogSectionViewModel : ObservableObject
{
    public LogSectionViewModel(string title, IReadOnlyList<LogFieldViewModel> fields)
    {
        Title = title;
        Fields = fields;
    }

    public string Title { get; }
    public IReadOnlyList<LogFieldViewModel> Fields { get; }

    [RelayCommand] private void CopySection() => ClipboardHelper.SetText(ToText());

    /// <summary>The section as plain text: a title line then one "label: value" line per row.</summary>
    public string ToText()
    {
        var sb = new StringBuilder().AppendLine(Title);
        foreach (var f in Fields) sb.Append(f.Label).Append(": ").AppendLine(f.CopyText);
        return sb.ToString();
    }
}

public sealed partial class LogSummaryViewModel : ObservableObject
{
    private LogSummaryViewModel(TherionLogSummary log, IReadOnlyList<LogSectionViewModel> sections)
    {
        Log = log;
        Sections = sections;
    }

    public TherionLogSummary Log { get; }
    public IReadOnlyList<LogSectionViewModel> Sections { get; }

    public string OutcomeText => Log.Outcome switch
    {
        TherionLogOutcome.Success => Tr.Get("Log_Outcome_Success"),
        TherionLogOutcome.SuccessWithWarnings => Tr.Get("Log_Outcome_Warnings"),
        _ => Tr.Get("Log_Outcome_Failed"),
    };

    public IBrush OutcomeBrush => Log.Outcome switch
    {
        TherionLogOutcome.Success => new ImmutableSolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
        TherionLogOutcome.SuccessWithWarnings => new ImmutableSolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)),
        _ => new ImmutableSolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
    };

    [RelayCommand]
    private void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (var s in Sections) sb.AppendLine(s.ToText());
        ClipboardHelper.SetText(sb.ToString());
    }

    // ----- construction ------------------------------------------------------

    public static LogSummaryViewModel Build(TherionLogSummary log)
    {
        var sections = new List<LogSectionViewModel>();
        Add(sections, "Log_Section_Summary", Summary(log));
        Add(sections, "Log_Section_Versions", Versions(log));
        Add(sections, "Log_Section_Inputs", Inputs(log));
        Add(sections, "Log_Section_Coordinates", Coordinates(log));
        Add(sections, "Log_Section_Statistics", Statistics(log));
        Add(sections, "Log_Section_LoopErrors", LoopErrors(log));
        Add(sections, "Log_Section_Outputs", Outputs(log));
        Add(sections, "Log_Section_Stages", Stages(log));
        Add(sections, "Log_Section_Diagnostics", Diagnostics(log));
        return new LogSummaryViewModel(log, sections);
    }

    /// <summary>Appends a section, skipping it when the log carried none of its fields.</summary>
    private static void Add(List<LogSectionViewModel> sections, string titleKey, List<LogFieldViewModel> fields)
    {
        if (fields.Count > 0) sections.Add(new LogSectionViewModel(Tr.Get(titleKey), fields));
    }

    private static List<LogFieldViewModel> Summary(TherionLogSummary log)
    {
        var f = new List<LogFieldViewModel>
        {
            Row("Log_Outcome", log.Outcome switch
            {
                TherionLogOutcome.Success => Tr.Get("Log_Outcome_Success"),
                TherionLogOutcome.SuccessWithWarnings => Tr.Get("Log_Outcome_Warnings"),
                _ => Tr.Get("Log_Outcome_Failed"),
            }),
        };
        if (log.CompilationTimeSeconds is { } secs) f.Add(Row("Log_CompilationTime", $"{secs} s"));
        int errors = log.Errors.Count(), warnings = log.Warnings.Count();
        if (errors > 0) f.Add(Row("Log_ErrorCount", errors.ToString(CultureInfo.InvariantCulture)));
        if (warnings > 0) f.Add(Row("Log_WarningCount", warnings.ToString(CultureInfo.InvariantCulture)));
        if (log.Outcome == TherionLogOutcome.Failed && log.IncompleteStage is { } stage)
            f.Add(Row("Log_IncompleteStage", stage));
        return f;
    }

    private static List<LogFieldViewModel> Versions(TherionLogSummary log)
    {
        var f = new List<LogFieldViewModel>();
        AddIf(f, "Log_TherionVersion", log.TherionVersion);
        AddIf(f, "Log_ReleaseDate", log.TherionReleaseDate);
        if (log.ProjVersion is { } proj)
            f.Add(Row("Log_Proj", log.ProjCompiledAgainst is { } built && built != proj
                ? string.Format(CultureInfo.InvariantCulture, Tr.Get("Log_ProjMismatch"), proj, built)
                : proj));
        AddIf(f, "Log_Survex", log.SurvexVersion);
        // Any other "- using …" banner line Therion may print (the Proj one is already split out).
        foreach (var lib in log.Libraries.Where(l => !l.StartsWith("Proj ", StringComparison.Ordinal)))
            f.Add(Row("Log_Library", lib));
        return f;
    }

    private static List<LogFieldViewModel> Inputs(TherionLogSummary log)
    {
        var f = new List<LogFieldViewModel>();
        AddIf(f, "Log_ConfigFile", log.ConfigurationFile);
        AddIf(f, "Log_InitFile", log.InitializationFile);
        AddIf(f, "Log_Encoding", log.Encoding);
        return f;
    }

    private static List<LogFieldViewModel> Coordinates(TherionLogSummary log)
    {
        var f = new List<LogFieldViewModel>();
        AddIf(f, "Log_OutputCs", log.OutputCoordinateSystem);
        if (log.MeridianConvergence is { } mc) f.Add(Row("Log_MeridianConvergence", Deg(mc)));
        AddIf(f, "Log_AreaOfUse", log.AreaOfUse);
        foreach (var d in log.Declinations)
            f.Add(new LogFieldViewModel($"{Tr.Get("Log_Declination")} {d.Date}", Deg(d.Degrees)));
        foreach (var c in log.CrsTransformations)
            f.Add(new LogFieldViewModel($"{c.From} → {c.To}",
                $"{c.Transformation} · {Tr.Get("Log_Accuracy")} {c.Accuracy}", c.RawLine, c.Definition));
        return f;
    }

    private static List<LogFieldViewModel> Statistics(TherionLogSummary log)
    {
        var f = new List<LogFieldViewModel>();
        if (log.StationCount is { } st) f.Add(Row("Log_Stations", Int(st)));
        if (log.ShotCount is { } sh) f.Add(Row("Log_Shots", Int(sh)));
        if (log.LoopCount is { } lp) f.Add(Row("Log_Loops", Int(lp)));
        if (log.ConnectedComponents is { } cc) f.Add(Row("Log_Components", Int(cc)));
        if (log.HasNoControlPoints) f.Add(Row("Log_NoControlPoints", Tr.Get("Log_Yes")));
        if (log.TotalLength is { } tl) f.Add(Row("Log_TotalLength", Metres(tl)));
        if (log.TotalLengthAdjusted is { } ta) f.Add(Row("Log_AdjustedLength", Metres(ta)));
        if (log.PlanLength is { } pl) f.Add(Row("Log_PlanLength", Metres(pl)));
        if (log.VerticalLength is { } vl) f.Add(Row("Log_VerticalLength", Metres(vl)));
        if (log.VerticalRange is { } vr) f.Add(Row("Log_VerticalRange", Range(vr)));
        if (log.NorthSouthRange is { } ns) f.Add(Row("Log_NsRange", Range(ns)));
        if (log.EastWestRange is { } ew) f.Add(Row("Log_EwRange", Range(ew)));
        if (log.AverageLoopErrorPercent is { } ale) f.Add(Row("Log_AverageLoopError", Percent(ale)));
        return f;
    }

    private static List<LogFieldViewModel> LoopErrors(TherionLogSummary log) =>
        log.LoopErrors.Select((e, i) => new LogFieldViewModel(
            $"#{i + 1}  {Percent(e.RelativeErrorPercent)}",
            string.Format(CultureInfo.InvariantCulture, "{0} / {1} · {2} {3} · Δ {4}, {5}, {6}",
                Metres(e.AbsoluteError), Metres(e.TotalLength), Int(e.StationCount), Tr.Get("Log_StationsShort"),
                Metres(e.ErrorX), Metres(e.ErrorY), Metres(e.ErrorZ)),
            e.RawLine.Trim(), e.Stations)).ToList();

    private static List<LogFieldViewModel> Outputs(TherionLogSummary log) =>
        log.OutputFiles
            .Select(o => new LogFieldViewModel(o.Path, Tr.Get(o.Completed ? "Log_Done" : "Log_NotCompleted"), o.Path))
            .ToList();

    private static List<LogFieldViewModel> Stages(TherionLogSummary log) =>
        log.Stages
            .Select(s => new LogFieldViewModel(s.Name, Tr.Get(s.Completed ? "Log_Done" : "Log_NotCompleted"), s.Name))
            .ToList();

    private static List<LogFieldViewModel> Diagnostics(TherionLogSummary log) =>
        log.Diagnostics.Select(d => new LogFieldViewModel(
            Tr.Get("Log_Sev_" + d.Severity),
            d.File is null ? d.Message : $"{d.Message} — {d.File}{(d.Line is { } n ? $" [{n}]" : string.Empty)}"
                + (d.Symbol is null ? string.Empty : $" — {d.Symbol}"),
            d.RawLine)).ToList();

    // ----- formatting --------------------------------------------------------

    private static LogFieldViewModel Row(string labelKey, string value) => new(Tr.Get(labelKey), value);

    private static void AddIf(List<LogFieldViewModel> fields, string labelKey, string? value)
    {
        if (!string.IsNullOrEmpty(value)) fields.Add(Row(labelKey, value));
    }

    // Values are formatted invariantly: they are copied straight out of (and back into) the
    // log and Therion's own files, which are invariant-culture too.
    private static string Int(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
    private static string Metres(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + " m";
    private static string Deg(double v) => v.ToString("0.####", CultureInfo.InvariantCulture) + "°";
    private static string Percent(double v) => v.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Range(TherionLogRange r) => string.Format(CultureInfo.InvariantCulture,
        "{0} ({1} {2} → {3} {4})", Metres(r.Length), r.FromStation, Metres(r.FromValue), r.ToStation, Metres(r.ToValue));
}
