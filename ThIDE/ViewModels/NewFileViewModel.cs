using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Editing;
using Therion.Workspace.Templates;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>What File ▸ New File… needs from the app, captured when it opens.</summary>
/// <param name="SurveyFiles">Workspace <c>.th</c> files the new file can be input from.</param>
/// <param name="InputParents">Of those, the ones that already input other files (preferred as the default).</param>
/// <param name="Variables">Template variables, resolved against the file open in the editor.</param>
/// <param name="Open">Opens the created file with the caret at an offset.</param>
/// <param name="Activate">Makes a created thconfig the active one.</param>
public sealed record NewFileEnvironment(
    IReadOnlyList<FileTemplate> Templates,
    string Folder,
    string? WorkspaceRoot,
    IReadOnlyList<string> SurveyFiles,
    IReadOnlyCollection<string> InputParents,
    Func<string, string?> ReadText,
    Func<string, bool> Exists,
    Func<string, string?> Variables,
    IWorkspaceEditService? Editor,
    Func<string, int, Task> Open,
    Func<string, Task>? Activate);

public sealed record TemplateRow(FileTemplate Template, string Title, string Description)
{
    public override string ToString() => Title;
}

public sealed record InputTargetRow(string? Path, string Label)
{
    public override string ToString() => Label;
}

/// <summary>File ▸ New File…: a template, a name and folder, and optionally an input into a survey file.</summary>
public sealed partial class NewFileViewModel : ObservableObject
{
    private readonly NewFileEnvironment _env;
    private bool _nameEdited;
    private bool _settingName;
    private bool _inputPicked;
    private bool _settingInput;

    public NewFileViewModel(NewFileEnvironment env)
    {
        _env = env;
        foreach (var t in env.Templates)
            Templates.Add(new TemplateRow(t, t.TitleKey is { } k ? Tr.Get(k) : t.Title, t.DescriptionKey is { } d ? Tr.Get(d) : t.Description));
        InputTargets.Add(new InputTargetRow(null, Tr.Get("NewFile_NoInput")));
        foreach (var f in env.SurveyFiles)
            InputTargets.Add(new InputTargetRow(f, env.WorkspaceRoot is { } root ? Path.GetRelativePath(root, f).Replace('\\', '/') : Path.GetFileName(f)));
        _folder = env.Folder;
        SelectedTemplate = Templates.FirstOrDefault();
        _settingInput = true;
        SelectedInputTarget = DefaultInputTarget();
        _settingInput = false;
    }

    public ObservableCollection<TemplateRow> Templates { get; } = new();
    public ObservableCollection<InputTargetRow> InputTargets { get; } = new();

    [ObservableProperty] private TemplateRow? _selectedTemplate;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private string _folder;
    [ObservableProperty] private InputTargetRow? _selectedInputTarget;
    [ObservableProperty] private string _preview = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _canCreate;

    /// <summary>
    /// The file to create: the typed name, with the template's extension unless it already ends in one
    /// of the templates' extensions. Any other dot is part of the name (<c>upper.level</c> → <c>upper.level.th</c>).
    /// </summary>
    public string TargetPath => Path.Combine(Folder,
        SelectedTemplate is { } t && KnownExtension(FileName) is null ? FileName.TrimEnd('.') + t.Template.Extension : FileName);

    private string? KnownExtension(string name) => Templates.Select(r => r.Template.Extension)
        .FirstOrDefault(e => e.Length > 0 && name.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    partial void OnSelectedTemplateChanged(TemplateRow? value)
    {
        if (value is not null)
        {
            if (!_nameEdited) SetNameQuietly(NewFilePlanner.FreeName(Folder, value.Template.DefaultName, value.Template.Extension, _env.Exists));
            // A typed name keeps its stem but takes the template's extension.
            else SetNameQuietly((KnownExtension(FileName) is { } e ? FileName[..^e.Length] : FileName.TrimEnd('.')) + value.Template.Extension);
        }
        Refresh();
    }

    partial void OnFileNameChanged(string value)
    {
        if (!_settingName) _nameEdited = true;
        Refresh();
    }

    partial void OnFolderChanged(string value)
    {
        if (!_nameEdited && SelectedTemplate is { } t) SetNameQuietly(NewFilePlanner.FreeName(value, t.Template.DefaultName, t.Template.Extension, _env.Exists));
        if (!_inputPicked)
        {
            _settingInput = true;
            SelectedInputTarget = DefaultInputTarget();
            _settingInput = false;
        }
        Refresh();
    }

    partial void OnSelectedInputTargetChanged(InputTargetRow? value)
    {
        if (!_settingInput) _inputPicked = true;
        Refresh();
    }

    private void SetNameQuietly(string name)
    {
        _settingName = true;
        FileName = name;
        _settingName = false;
    }

    // A survey file in the same folder that already inputs others is where a new one usually goes.
    private InputTargetRow DefaultInputTarget() =>
        InputTargets.Skip(1).FirstOrDefault(r => r.Path is { } p && _env.InputParents.Contains(p)
            && FilePaths.Same(Path.GetDirectoryName(p)!, Folder))
        ?? InputTargets[0];

    /// <summary>The plan for the current choices, or null when they are incomplete.</summary>
    public NewFilePlan? Plan { get; private set; }

    private void Refresh()
    {
        Plan = null;
        CanCreate = false;
        Message = string.Empty;
        if (SelectedTemplate is null || string.IsNullOrWhiteSpace(FileName) || string.IsNullOrWhiteSpace(Folder))
        {
            Preview = string.Empty;
            return;
        }
        if (FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Message = Tr.Get("NewFile_BadName");
            Preview = string.Empty;
            return;
        }
        var input = SelectedTemplate.Template.IsSurveyFile ? SelectedInputTarget?.Path : null;
        Plan = NewFilePlanner.Plan(SelectedTemplate.Template, TargetPath, _env.Variables, input, _env.ReadText);
        Preview = Plan.Content;
        Message = _env.Exists(TargetPath) ? Tr.Get("NewFile_Exists")
                : Plan.InputExists ? Tr.Get("NewFile_InputExists")
                : string.Empty;
        CanCreate = !_env.Exists(TargetPath) && _env.Editor is not null;
    }

    /// <summary>Creates the file (and the input), opens it, and activates a thconfig template. Null on success, else why not.</summary>
    public async Task<string?> CreateAsync()
    {
        if (Plan is not { } plan || _env.Editor is null || SelectedTemplate is null) return Tr.Get("NewFile_CannotCreate");
        var outcome = await _env.Editor.ApplyAsync(plan.Batch);
        if (!outcome.Success)
            return outcome.Describe();
        await _env.Open(plan.FilePath, plan.Caret);
        if (SelectedTemplate.Template.ActivateThconfig && _env.Activate is { } activate) await activate(plan.FilePath);
        return null;
    }
}
