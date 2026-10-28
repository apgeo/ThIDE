using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Linking;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels.Pickers;

namespace ThIDE.ViewModels;

/// <summary>What the link dialog needs from the app, captured when it opens (so it can work off the UI thread).</summary>
/// <param name="ReadText">A file's text: the editor buffer when open, else the file decoded by its declared encoding.</param>
/// <param name="FilesBeside">Every <c>.th2</c> under a folder (the source's sketches).</param>
/// <param name="CurrentFile">The active editor's file, when it is a workspace <c>.th</c>.</param>
/// <param name="CurrentCaret">Caret offset in <paramref name="CurrentFile"/>.</param>
public sealed record LinkSurveyEnvironment(
    WorkspaceSemanticModel Workspace,
    string? WorkspaceRoot,
    Func<string, string?> ReadText,
    Func<string, IEnumerable<string>> FilesBeside,
    Func<string, string, bool> SameContent,
    IWorkspaceEditService? Editor,
    string? CurrentFile,
    int CurrentCaret,
    DateOnly Today);

/// <summary>One destination the user can choose, with its explanation.</summary>
public sealed record DestinationRow(DestinationCandidate Candidate, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A sketch beside the source file, offered for copying.</summary>
public sealed partial class SketchRow(string path, string label, bool isChecked) : ObservableObject
{
    public string Path { get; } = path;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>A plan warning, localized, and whether it stops the insert.</summary>
public sealed record WarningRow(string Text, bool Blocks);

/// <summary>
/// "Link survey file": pick a station of a standalone survey file and a station of the workspace,
/// and get the <c>input</c> and <c>equate</c> lines that join them — inserted where similar lines
/// already live, or copied to the clipboard. Computation lives in <see cref="SurveyLinkPlanner"/>.
/// </summary>
public sealed partial class LinkSurveyViewModel : ObservableObject
{
    private readonly LinkSurveyEnvironment _env;
    private readonly Dictionary<string, TherionFile?> _asts = new(FilePaths.Comparer);
    private FileScopes? _scopes;
    private LinkContext? _ctx;
    private LinkDestination? _preset;
    private bool _targetEdited;
    private bool _loading;
    private int _replanSuspended;
    // The source is already part of the project: link it where it is, without a second input.
    private bool _sourceInWorkspace;

    public LinkSurveyViewModel(LinkSurveyEnvironment env)
    {
        _env = env;
        SourceSelector.Picker.SelectionChanged += (_, _) => { if (_sourceInWorkspace) RefreshDestinations(); else Replan(); };
        Surveys.SelectionChanged += (_, _) => NarrowStations();
        Stations.SelectionChanged += (_, _) => OnWorkspaceStationChanged();
        Sketches.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { } added)
                foreach (SketchRow row in added) row.PropertyChanged += (_, _) => Replan();
        };
    }

    public StationSelectorViewModel SourceSelector { get; } = new("Source");
    public SurveyPickerViewModel Surveys { get; } = new("Survey");
    public StationPickerViewModel Stations { get; } = new("Station");

    public SourceSurvey? Source { get; private set; }
    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _sourceStatus = string.Empty;

    public ObservableCollection<DestinationRow> Destinations { get; } = new();
    [ObservableProperty] private DestinationRow? _selectedDestination;
    [ObservableProperty] private bool _useCurrentFile;
    [ObservableProperty] private bool _canUseCurrentFile;

    [ObservableProperty] private bool _copySource;
    [ObservableProperty] private string _targetDirectory = string.Empty;
    public ObservableCollection<SketchRow> Sketches { get; } = new();

    [ObservableProperty] private bool _addComment;

    [ObservableProperty] private string _generatedText = string.Empty;
    [ObservableProperty] private IReadOnlyList<WarningRow> _warnings = Array.Empty<WarningRow>();
    [ObservableProperty] private bool _canInsert;
    [ObservableProperty] private bool _isBusy;

    public LinkPlan? Plan { get; private set; }

    /// <summary>Raised with the text to put on the clipboard.</summary>
    public event EventHandler<string>? CopyRequested;
    /// <summary>Raised to open the whole workspace (with the new survey attached) in a preview window.</summary>
    public event EventHandler? FullPreviewRequested;
    /// <summary>Raised when the lines were written; the dialog closes.</summary>
    public event EventHandler<EditOutcome>? Applied;

    /// <summary>The workspace's models plus the scopes, for the full preview window.</summary>
    public WorkspaceSemanticModel Workspace => _env.Workspace;

    /// <summary>
    /// Loads the workspace side, then the source file. With <paramref name="presetDestinationFile"/>
    /// (the editor's "Generate code" entry) the lines go into that file, in the survey at the caret.
    /// </summary>
    public async Task LoadAsync(string? sourcePath, string? presetDestinationFile = null)
    {
        _loading = true;
        IsBusy = true;
        try
        {
            try
            {
                await Task.Run(() =>
                {
                    _scopes = SurveyScopeResolver.Build(_env.Workspace, Ast);
                    _ctx = new LinkContext(_env.Workspace, _scopes, _env.ReadText, Ast, p => File.Exists(p) || Directory.Exists(p), _env.SameContent);
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SourceStatus = string.Format(Tr.Get("LinkSurvey_SourceUnreadable"), ex.Message);
                return;
            }
            Surveys.SetSurveys(LinkCatalog.Surveys(_env.Workspace, _scopes!));
            Stations.SetStations(LinkCatalog.Stations(_env.Workspace, _scopes!), showFile: true);

            if (presetDestinationFile is not null && _env.ReadText(presetDestinationFile) is { } text)
            {
                var ast = Ast(presetDestinationFile);
                var offset = _env.CurrentFile is { } current && FilePaths.Same(presetDestinationFile, current) ? _env.CurrentCaret : 0;
                _preset = new LinkDestination(presetDestinationFile, SurveyScopeResolver.SurveyPathAt(ast, Math.Min(offset, text.Length), text));
                UseCurrentFile = true;
            }
            if (!string.IsNullOrWhiteSpace(sourcePath)) await LoadSourceAsync(sourcePath);
        }
        finally
        {
            _loading = false;
            IsBusy = false;
        }
        RefreshDestinations();
    }

    /// <summary>
    /// Opens the dialog on two project stations (Suggest equates): the first file as the "source",
    /// linked where it is with an equate only.
    /// </summary>
    public async Task PresetStationsAsync(string fileA, QualifiedName localA, string fileB, QualifiedName localB)
    {
        await LoadAsync(fileA);
        SourceSelector.Picker.Select(fileA, localA);
        Stations.Select(fileB, localB);
        RefreshDestinations();
    }

    /// <summary>Loads the file named in the source box (Enter or the Load button).</summary>
    [RelayCommand]
    private Task LoadSource() => string.IsNullOrWhiteSpace(SourcePath) ? Task.CompletedTask : LoadSourceAsync(SourcePath.Trim());

    /// <summary>Parses the standalone survey file and fills the left-hand picker.</summary>
    public async Task LoadSourceAsync(string sourcePath)
    {
        SourcePath = sourcePath;
        Source = null;
        Sketches.Clear();
        _replanSuspended++;
        try
        {
            var full = Path.GetFullPath(sourcePath);
            var (source, errors, sketches) = await Task.Run(() =>
            {
                var text = _env.ReadText(full) ?? throw new FileNotFoundException(full);
                var parse = new ThParser().Parse(full, text);
                var ast = parse.Value ?? throw new InvalidDataException(full);
                var model = new SemanticBinder().Bind(ast);
                int errorCount = parse.Diagnostics.Count(d => d.Severity == Therion.Core.DiagnosticSeverity.Error);
                var survey = new SourceSurvey(full, ast, model);
                // A folder that cannot be listed just means no sketches are offered.
                IEnumerable<string> beside;
                try { beside = _env.FilesBeside(Path.GetDirectoryName(full)!).ToList(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { beside = []; }
                return (survey, errorCount, SurveyLinkPlanner.Sketches(survey, beside));
            });
            Source = source;
            SourceStatus = string.Format(Tr.Get("LinkSurvey_SourceStatus"), source.Model.Stations.Count, Enumerable.Count(source.Model.Shots), errors);
            SourceSelector.Load(LinkCatalog.Stations(source), [source.Model], showFile: false);

            // A source already in the project is linked where it is; one outside the project folder
            // is copied in by default; one inside it but not yet input is input in place.
            _sourceInWorkspace = _env.Workspace.PerFile.Keys.Any(k => FilePaths.Same(k, full));
            CopySource = !_sourceInWorkspace && !IsUnderRoot(full);
            OnPropertyChanged(nameof(CanCopySource));
            foreach (var (path, referenced) in sketches)
                Sketches.Add(new SketchRow(path, Path.GetRelativePath(Path.GetDirectoryName(full)!, path).Replace('\\', '/'), referenced));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Source = null;
            SourceStatus = string.Format(Tr.Get("LinkSurvey_SourceUnreadable"), ex.Message);
            SourceSelector.Load([], [], showFile: false);
        }
        finally { _replanSuspended--; }
        Replan();
    }

    private bool IsUnderRoot(string path) =>
        _env.WorkspaceRoot is { Length: > 0 } root
        && !Path.GetRelativePath(root, path).StartsWith("..", StringComparison.Ordinal)
        && !Path.IsPathRooted(Path.GetRelativePath(root, path));

    /// <summary>Copying makes no sense for a file the project already inputs.</summary>
    public bool CanCopySource => !_sourceInWorkspace;

    // Only survey files carry the survey blocks scopes and destinations need; sketches and
    // thconfigs are not parsed here.
    private TherionFile? Ast(string path)
    {
        if (!path.EndsWith(".th", StringComparison.OrdinalIgnoreCase)) return null;
        lock (_asts)
        {
            if (_asts.TryGetValue(path, out var cached)) return cached;
        }
        var text = _env.ReadText(path);
        var ast = text is null ? null : new ThParser().Parse(path, text).Value;
        lock (_asts) _asts[path] = ast;
        return ast;
    }

    private void NarrowStations()
    {
        if (_scopes is null) return;
        var within = Surveys.SelectedSurvey?.Effective;
        Stations.SetStations(LinkCatalog.Stations(_env.Workspace, _scopes, within), showFile: true);
    }

    private void OnWorkspaceStationChanged()
    {
        RefreshDestinations();
        Replan();
    }

    /// <summary>Rebuilds the destination list for the chosen workspace station (or the preset).</summary>
    private void RefreshDestinations()
    {
        if (_loading || _ctx is null) return;
        var previous = SelectedDestination?.Candidate is { } p ? Key(p) : null;
        _replanSuspended++;
        try { FillDestinations(previous); }
        finally { _replanSuspended--; }
        Replan();
    }

    private static string Key(DestinationCandidate c) => FilePaths.Full(c.FilePath) + "\0" + string.Join('.', c.LocalScope);

    private void FillDestinations(string? previous)
    {
        Destinations.Clear();

        var candidates = new List<DestinationCandidate>();
        if (_preset is { } preset)
            candidates.Add(new DestinationCandidate(preset.FilePath, preset.LocalScope,
                _scopes!.EffectiveScope(preset.FilePath, preset.LocalScope), DestinationReason.Block, -1));
        if (Stations.SelectedStation is { } station)
        {
            var scope = SurveyLinkPlanner.DefaultScope(station.Effective, nest: false);
            // Both stations already in the project: the lines go where both can be named from.
            if (_sourceInWorkspace && SourceSelector.Picker.SelectedStation is { } src && _scopes is not null)
            {
                var effective = _scopes.Effective(src.FilePath, src.Local);
                var srcSurvey = effective.Parts.RemoveAt(effective.Parts.Length - 1);
                int common = 0;
                while (common < scope.Length && common < srcSurvey.Length && scope[common] == srcSurvey[common]) common++;
                scope = scope.RemoveRange(common, scope.Length - common);
            }
            candidates.AddRange(SurveyLinkPlanner.Candidates(_ctx, scope));
        }

        foreach (var c in candidates.DistinctBy(c => (c.FilePath, string.Join('.', c.LocalScope))))
            Destinations.Add(new DestinationRow(c, Label(c)));

        CanUseCurrentFile = _env.CurrentFile is { } cur && Destinations.Any(d => SamePath(d.Candidate.FilePath, cur));
        if (!CanUseCurrentFile) UseCurrentFile = false;

        SelectedDestination =
            (UseCurrentFile ? Destinations.FirstOrDefault(d => SamePath(d.Candidate.FilePath, _env.CurrentFile!)) : null)
            ?? Destinations.FirstOrDefault(d => previous is not null && Key(d.Candidate) == previous)
            ?? Destinations.FirstOrDefault();
    }

    private string Label(DestinationCandidate c)
    {
        var scope = c.Scope.IsEmpty ? Tr.Get("LinkSurvey_RootScope") : string.Join('.', c.Scope);
        var reason = c.Rank < 0 ? Tr.Get("LinkSurvey_ReasonCaret") : Tr.Get("LinkSurvey_Reason" + c.Reason);
        var file = _env.WorkspaceRoot is { } root ? Path.GetRelativePath(root, c.FilePath).Replace('\\', '/') : Path.GetFileName(c.FilePath);
        return $"{file} — {scope} ({reason})";
    }

    partial void OnUseCurrentFileChanged(bool value)
    {
        if (!value || _env.CurrentFile is null) { Replan(); return; }
        if (Destinations.FirstOrDefault(d => SamePath(d.Candidate.FilePath, _env.CurrentFile)) is { } row) SelectedDestination = row;
        Replan();
    }

    partial void OnSelectedDestinationChanged(DestinationRow? value)
    {
        if (value is not null && UseCurrentFile && _env.CurrentFile is { } cur && !SamePath(value.Candidate.FilePath, cur))
            UseCurrentFile = false;
        if (!_targetEdited && value is not null && _ctx is not null)
            SetTargetDirectoryQuietly(SurveyLinkPlanner.DefaultTargetDirectory(_ctx, value.Candidate.Destination));
        Replan();
    }

    private bool _settingTarget;
    private void SetTargetDirectoryQuietly(string dir)
    {
        _settingTarget = true;
        TargetDirectory = dir;
        _settingTarget = false;
    }

    partial void OnTargetDirectoryChanged(string value)
    {
        if (!_settingTarget) _targetEdited = true;
        Replan();
    }

    partial void OnCopySourceChanged(bool value) => Replan();
    partial void OnAddCommentChanged(bool value) => Replan();

    /// <summary>Recomputes the generated lines, the edits and the warnings from the current choices.</summary>
    public void Replan()
    {
        if (_loading || _replanSuspended > 0) return;
        Plan = null;
        if (_ctx is null || Source is null || SourceSelector.Picker.SelectedStation is not { } src
            || Stations.SelectedStation is not { } dst || SelectedDestination is not { } dest)
        {
            GeneratedText = string.Empty;
            Warnings = Array.Empty<WarningRow>();
            CanInsert = false;
            return;
        }

        var comment = AddComment
            ? string.Format(Tr.Get("LinkSurvey_CommentFormat"), _env.Today.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture), Path.GetFileName(Source.FilePath))
            : null;
        var request = new LinkRequest(
            Source,
            [new LinkMember(_sourceInWorkspace ? LinkSide.Workspace : LinkSide.Source, Source.FilePath, src.Local),
             new LinkMember(LinkSide.Workspace, dst.FilePath, dst.Local)],
            dest.Candidate.Destination,
            CopySource && TargetDirectory.Length > 0
                ? new LinkCopy(true, TargetDirectory, Sketches.Where(s => s.IsChecked).Select(s => s.Path).ToList())
                : LinkCopy.InPlace,
            new LinkOptions(InsertInput: !_sourceInWorkspace, Comment: comment));

        Plan = SurveyLinkPlanner.Plan(_ctx, request);
        GeneratedText = Plan.Text;
        Warnings = Plan.Warnings.Select(w => new WarningRow(WarningText(w), w.Blocks)).ToList();
        CanInsert = Plan.CanApply && _env.Editor is not null;
    }

    private string WarningText(LinkWarning w)
    {
        var subject = w.Subject is null ? "" : _env.WorkspaceRoot is { } root && Path.IsPathRooted(w.Subject)
            ? Path.GetRelativePath(root, w.Subject).Replace('\\', '/')
            : w.Subject;
        return string.Format(Tr.Get("LinkSurvey_Warn" + w.Code), subject);
    }

    [RelayCommand]
    private void Copy()
    {
        if (GeneratedText.Length > 0) CopyRequested?.Invoke(this, GeneratedText);
    }

    [RelayCommand] private void ShowFullPreview() => FullPreviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Writes the plan. Returns the outcome; on success <see cref="Applied"/> is raised.</summary>
    public async Task<EditOutcome?> InsertAsync()
    {
        if (Plan is not { CanApply: true } plan || _env.Editor is null) return null;
        IsBusy = true;
        try
        {
            var outcome = await _env.Editor.ApplyAsync(plan.Batch);
            if (outcome.Success) Applied?.Invoke(this, outcome);
            else Warnings = Warnings.Append(new WarningRow(outcome.Describe(), true)).ToList();
            return outcome;
        }
        finally { IsBusy = false; }
    }

    /// <summary>The stations to highlight and the link to draw in the full preview (model names).</summary>
    public (IReadOnlyList<SemanticModel> Models, (QualifiedName, QualifiedName)? Link, string? SourceFile) FullPreviewScene()
    {
        var models = _env.Workspace.PerFile.Values.ToList();
        if (Source is null) return (models, null, null);
        models.Add(Source.Model);
        (QualifiedName, QualifiedName)? link = SourceSelector.Picker.SelectedStation is { } a && Stations.SelectedStation is { } b
            ? (a.Local, b.Local) : null;
        return (models, link, Source.FilePath);
    }

    private static bool SamePath(string a, string b) => FilePaths.Same(a, b);
}

/// <summary>Converters for the link dialog.</summary>
public static class LinkSurveyConverters
{
    private static readonly Avalonia.Media.IBrush Blocking = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Avalonia.Media.IBrush Advisory = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xE6, 0x51, 0x00));

    /// <summary>Red for a warning that stops the insert, orange for one the user may override.</summary>
    public static Avalonia.Data.Converters.IValueConverter WarningBrush { get; } =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(blocks => blocks ? Blocking : Advisory);
}
