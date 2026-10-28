using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build.Automation;
using Therion.Workspace;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

/// <summary>One export of a thconfig, ticked to build it.</summary>
public sealed partial class BatchTargetRow(ExportTargetInfo target, bool isChecked) : ObservableObject
{
    public ExportTargetInfo Target { get; } = target;
    public string Title => Target.Title;
    public string Key => $"{Target.StartLine}-{Target.EndLine}";
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>A thconfig in the batch, with its exports.</summary>
public sealed partial class BatchConfigRow : ObservableObject
{
    public BatchConfigRow(string path, string label, IReadOnlyList<BatchTargetRow> targets, bool isChecked)
    {
        Path = path;
        Label = label;
        Targets = targets;
        _isChecked = isChecked;
    }

    public string Path { get; }
    public string Label { get; }
    public IReadOnlyList<BatchTargetRow> Targets { get; }
    public bool HasTargets => Targets.Count > 0;
    [ObservableProperty] private bool _isChecked;

    /// <summary>All exports ticked (or none listed): build the thconfig as it is.</summary>
    public bool Whole => Targets.Count == 0 || Targets.All(t => t.IsChecked);
}

/// <summary>What the batch window needs from the app.</summary>
/// <param name="Build">Builds one entry (deleting <c>temp</c> afterwards when given) and reports how it ended.</param>
/// <param name="Cancel">Cancels the build in progress.</param>
/// <param name="SelectionFile">Where the window remembers what was ticked (per project).</param>
public sealed record BatchBuildEnvironment(
    IReadOnlyList<string> Thconfigs,
    string? Root,
    Func<string, string?, string, Task<BuildOutcome?>> Build,
    Action Cancel,
    IAutomationOutput? Output,
    string? SelectionFile);

/// <summary>Build ▸ Build multiple…: a checklist of thconfigs × exports, built one after the other.</summary>
public sealed partial class BatchBuildViewModel : ObservableObject
{
    private sealed class Selection
    {
        public List<string> Checked { get; set; } = [];
        public List<string> UncheckedTargets { get; set; } = [];
        public bool StopOnFailure { get; set; }
    }

    private readonly BatchBuildEnvironment _env;
    private bool _cancelled;

    public BatchBuildViewModel(BatchBuildEnvironment env, IReadOnlyCollection<string>? preselect = null)
    {
        _env = env;
        var saved = env.SelectionFile is { } f ? ProjectLocalConfig.TryReadJson<Selection>(f).Value : null;
        _stopOnFailure = saved?.StopOnFailure ?? false;
        foreach (var path in env.Thconfigs)
        {
            IReadOnlyList<ExportTargetInfo> exports;
            try { exports = ThconfigExportEditor.ParseExports(File.ReadAllText(path)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { exports = []; }
            var targets = exports.Select(e => new BatchTargetRow(e,
                saved?.UncheckedTargets.Contains(path + "::" + $"{e.StartLine}-{e.EndLine}") != true)).ToList();
            bool isChecked = preselect is not null ? preselect.Contains(path) : saved?.Checked.Contains(path) ?? true;
            var label = env.Root is { } root ? Path.GetRelativePath(root, path).Replace('\\', '/') : Path.GetFileName(path);
            Configs.Add(new BatchConfigRow(path, label, targets, isChecked));
        }
    }

    public ObservableCollection<BatchConfigRow> Configs { get; } = new();
    [ObservableProperty] private bool _stopOnFailure;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = string.Empty;

    public IReadOnlyList<BuildOutcome> Outcomes { get; private set; } = [];

    /// <summary>Builds the ticked thconfigs in order; a thconfig with some exports unticked is built
    /// through a temporary copy that comments the others out.</summary>
    [RelayCommand]
    public async Task RunAsync()
    {
        var chosen = Configs.Where(c => c.IsChecked && (c.Whole || c.Targets.Any(t => t.IsChecked))).ToList();
        if (chosen.Count == 0 || IsRunning) return;
        SaveSelection();
        IsRunning = true;
        _cancelled = false;
        var session = _env.Output?.BeginSession(Tr.Get("Batch_Title"), AutomationKind.BatchBuild);
        session?.OnCancel(CancelRun);
        var outcomes = new List<BuildOutcome>();
        try
        {
            for (int i = 0; i < chosen.Count && !_cancelled; i++)
            {
                var row = chosen[i];
                var label = $"Batch {i + 1}/{chosen.Count}";
                Status = $"{label}: {row.Label}";
                session?.Write($"{label}: {row.Label}");
                string entry = row.Path;
                string? temp = null;
                if (!row.Whole)
                {
                    temp = Path.Combine(Path.GetDirectoryName(row.Path)!, ProjectLocalConfig.GeneratedFilePrefix + "batch-" + Path.GetFileName(row.Path));
                    var keep = row.Targets.Where(t => t.IsChecked).Select(t => t.Target).ToList();
                    await File.WriteAllTextAsync(temp, ThconfigExportEditor.IsolateExports(await File.ReadAllTextAsync(row.Path), keep));
                    entry = temp;
                    label += " " + string.Format(Tr.Get("Batch_SomeTargets"), keep.Count, row.Targets.Count);
                }
                var outcome = await _env.Build(entry, temp, label);
                if (outcome is null) { session?.Write(Tr.Get("Batch_NotStarted"), isError: true); break; }
                outcome = outcome with { Entry = row.Path };
                outcomes.Add(outcome);
                session?.Write(outcome.Succeeded ? "  ok" : $"  failed (exit {outcome.ExitCode})", !outcome.Succeeded);
                if (!outcome.Succeeded && (StopOnFailure || outcome.Cancelled)) break;
            }
        }
        finally
        {
            Outcomes = outcomes;
            foreach (var line in BatchSummary.Lines(outcomes, _env.Root)) session?.Write(line);
            int ok = outcomes.Count(o => o.Succeeded);
            session?.Complete(ok == chosen.Count ? AutomationState.Succeeded : _cancelled ? AutomationState.Cancelled : AutomationState.Failed);
            Status = string.Format(Tr.Get("Batch_Done"), ok, chosen.Count);
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelRun()
    {
        _cancelled = true;
        _env.Cancel();
    }

    private void SaveSelection()
    {
        if (_env.SelectionFile is not { } file) return;
        var selection = new Selection
        {
            Checked = Configs.Where(c => c.IsChecked).Select(c => c.Path).ToList(),
            UncheckedTargets = Configs.SelectMany(c => c.Targets.Where(t => !t.IsChecked).Select(t => c.Path + "::" + t.Key)).ToList(),
            StopOnFailure = StopOnFailure,
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(selection, ProjectLocalConfig.JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
