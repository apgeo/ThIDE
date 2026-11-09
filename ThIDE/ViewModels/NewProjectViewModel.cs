using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Templates;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

/// <summary>One field of the template's form.</summary>
public sealed partial class TemplateFieldRow(TemplateVariable variable, string value, Action changed, bool builtIn = false) : ObservableObject
{
    public TemplateVariable Variable { get; } = variable;
    /// <summary>A built-in template's prompt in the UI language; an imported template's as its author wrote it.</summary>
    public string Prompt => PromptOf(Variable, builtIn);

    internal static string PromptOf(TemplateVariable v, bool builtIn) =>
        builtIn && Tr.Find("ProjTpl_Var_" + v.Id) is { Length: > 0 } text ? text : v.Prompt;
    public bool IsBool => Variable.Type == TemplateVariableType.Bool;
    public bool IsChoice => Variable.Type == TemplateVariableType.Choice;
    public bool IsText => !IsBool && !IsChoice;
    public IReadOnlyList<string> Choices => Variable.Choices ?? [];
    public string AutomationId => "NewProject.Field." + Variable.Id;

    [ObservableProperty] private string _value = value;
    partial void OnValueChanged(string value) => changed();

    public bool BoolValue
    {
        get => Value == "true";
        set => Value = value ? "true" : "false";
    }
}

public sealed record ProjectTemplateRow(ProjectTemplate Template, string Title, bool IsImported)
{
    public string Description => Template.TitleKey is { } key ? Tr.Get(key + "Desc") : Template.Description;
    public override string ToString() => Title;
}

/// <summary>File ▸ New from Template…: a template, its fields and a target folder.</summary>
public sealed partial class NewProjectViewModel : ObservableObject
{
    private readonly string _library;
    private readonly Func<DateTime> _now;

    /// <param name="library">Folder holding imported templates (one sub-folder each).</param>
    public NewProjectViewModel(string library, string defaultFolder, Func<DateTime>? now = null)
    {
        _library = library;
        _now = now ?? (() => DateTime.Now);
        _targetFolder = defaultFolder;
        ReloadTemplates();
    }

    public ObservableCollection<ProjectTemplateRow> Templates { get; } = new();
    public ObservableCollection<TemplateFieldRow> Fields { get; } = new();

    [ObservableProperty] private ProjectTemplateRow? _selectedTemplate;
    [ObservableProperty] private string _targetFolder;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _canCreate;

    /// <summary>The thconfig to open after <see cref="Create"/> succeeded.</summary>
    public string? CreatedThconfig { get; private set; }

    public void ReloadTemplates(string? select = null)
    {
        Templates.Clear();
        foreach (var t in ProjectTemplateEngine.BuiltIns)
            Templates.Add(new ProjectTemplateRow(t, t.TitleKey is { } k ? Tr.Get(k) : t.Title, false));
        foreach (var t in ProjectTemplateEngine.LoadLibrary(_library))
            Templates.Add(new ProjectTemplateRow(t, t.Title, true));
        SelectedTemplate = Templates.FirstOrDefault(t => t.Template.Id == select) ?? Templates.FirstOrDefault();
    }

    partial void OnSelectedTemplateChanged(ProjectTemplateRow? value)
    {
        Fields.Clear();
        if (value is not null)
        {
            var defaults = ProjectTemplateEngine.Defaults(value.Template, _now());
            bool builtIn = !value.IsImported;
            foreach (var v in value.Template.Variables)
            {
                // A built-in's sample title ("My cave") in the UI language; it is only a starting value.
                var start = builtIn && v.Id == "title" && Tr.Find("ProjTpl_DefaultTitle") is { Length: > 0 } t ? t : defaults[v.Id];
                Fields.Add(new TemplateFieldRow(v, start, Refresh, builtIn));
            }
        }
        Refresh();
    }

    partial void OnTargetFolderChanged(string value) => Refresh();

    private Dictionary<string, string> Values() => Fields.ToDictionary(f => f.Variable.Id, f => f.Value, StringComparer.Ordinal);

    // The value problems in the UI language; the engine's other (path) errors stay as it words them.
    private IEnumerable<string> Describe(IReadOnlyList<string> errors)
    {
        var problems = ProjectTemplateEngine.ValueProblems(SelectedTemplate!.Template, Values());
        if (problems.Count == 0) return errors;
        bool builtIn = !SelectedTemplate.IsImported;
        return problems.Select(p => p.Value.Length == 0
            ? string.Format(Tr.Get("NewProject_ValueNeeded"), TemplateFieldRow.PromptOf(p.Variable, builtIn))
            : string.Format(Tr.Get("NewProject_ValueInvalid"), TemplateFieldRow.PromptOf(p.Variable, builtIn), p.Value));
    }

    private void Refresh()
    {
        CanCreate = false;
        if (SelectedTemplate is null) return;
        var (plan, errors) = ProjectTemplateEngine.Plan(SelectedTemplate.Template, Values(), _now());
        if (plan is null) { Message = string.Join("\n", Describe(errors)); return; }
        if (string.IsNullOrWhiteSpace(TargetFolder) || !Path.IsPathRooted(TargetFolder)) { Message = Tr.Get("NewProject_PickFolder"); return; }
        var taken = plan.Files.Select(f => f.RelativePath).FirstOrDefault(p => File.Exists(Path.Combine(TargetFolder, p)));
        Message = taken is null ? string.Empty : string.Format(Tr.Get("NewProject_Taken"), taken);
        CanCreate = taken is null;
    }

    /// <summary>Writes the project. Null on success (see <see cref="CreatedThconfig"/>), else the reason.</summary>
    public string? Create()
    {
        if (SelectedTemplate is null) return Tr.Get("NewProject_PickFolder");
        var (plan, errors) = ProjectTemplateEngine.Plan(SelectedTemplate.Template, Values(), _now());
        if (plan is null) return string.Join("\n", errors);
        try
        {
            Directory.CreateDirectory(TargetFolder);
            if (ProjectTemplateEngine.Write(plan, SelectedTemplate.Template.Folder, TargetFolder) is { } problem) return problem;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        CreatedThconfig = plan.ThconfigRelativePath.Length > 0 ? Path.Combine(TargetFolder, plan.ThconfigRelativePath) : null;
        return null;
    }

    /// <summary>Imports a template folder or zip into the library and selects it.</summary>
    public string? Import(string source)
    {
        try { Directory.CreateDirectory(_library); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        var (template, error) = ProjectTemplateEngine.Import(source, _library);
        if (template is null) return error;
        ReloadTemplates(template.Id);
        return null;
    }

    /// <summary>Removes an imported template from the library.</summary>
    public void RemoveSelected()
    {
        if (SelectedTemplate is not { IsImported: true, Template.Folder: { } folder }) return;
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message = ex.Message; return; }
        ReloadTemplates();
    }
}
