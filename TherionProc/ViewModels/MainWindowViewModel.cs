// Main shell ViewModel. Hosts the Dock.Avalonia layout (VS-classic) and owns the
// menu/toolbar commands. Documents are multi-file (MDI) via IDocumentService;
// the document-tracking tools (Object Browser, Diagnostics) follow the active doc.

using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Microsoft.Extensions.Localization;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Docking;
using TherionProc.Resources;
using TherionProc.Services;
using TherionProc.ViewModels.Docking;

namespace TherionProc.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string SampleText =
        "# Sample Therion centreline — use File → Open File... or Open Folder... to load real data.\n" +
        "survey demo -title \"Demo Cave\"\n" +
        "  centreline\n" +
        "    date 2024.01.15\n" +
        "    team \"Alice\" instruments\n" +
        "    data normal from to length compass clino\n" +
        "      # entrance series\n" +
        "      0 1 12.5 0 -5    # start at the drip\n" +
        "      1 2  8.0 90 0\n" +
        "    flags duplicate\n" +
        "      2 3  4.2 180 10  # re-survey of the squeeze\n" +
        "    flags not duplicate\n" +
        "    fix 0 100.0 200.0 -3.25\n" +
        "  endcentreline\n" +
        "endsurvey\n";

    private readonly IStringLocalizer<Strings> _l;
    private readonly ILanguageService _language;
    private readonly IDocumentService _documents;
    private readonly IModelEditService? _editService;
    private readonly ILayoutService? _layout;
    private readonly IAppSettingsService? _settings;
    private readonly DockFactory _factory;
    private IStoragePicker? _picker;

    public ILayoutService? LayoutService => _layout;

    // Dock layout bound by MainWindow.axaml.
    public IRootDock Layout { get; }
    public DockFactory Factory => _factory;

    // Tool wrappers (shown in the dock); content VMs are reached through them.
    public WorkspaceExplorerToolViewModel WorkspaceTool { get; }
    public ObjectBrowserToolViewModel ObjectBrowserTool { get; }
    public DiagnosticsToolViewModel DiagnosticsTool { get; }
    public CompilerOutputToolViewModel CompilerOutputTool { get; }
    public GeneratedFilesToolViewModel GeneratedFilesTool { get; }
    public XviToolViewModel XviTool { get; }
    public SettingsToolViewModel SettingsTool { get; }

    // Convenience accessors so menu/toolbar/keyboard bindings stay stable.
    public BuildViewModel Build => CompilerOutputTool.Build;
    public DiagnosticsViewModel Diagnostics => DiagnosticsTool.Diagnostics;
    public ObjectBrowserViewModel ObjectBrowser => ObjectBrowserTool.Browser;
    public WorkspaceExplorerViewModel WorkspaceExplorer => WorkspaceTool.Explorer;
    public XviReferencesViewModel XviReferences => XviTool.Xvi;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private bool _strictParserMode;
    partial void OnStrictParserModeChanged(bool value)
    {
        ParserOptionsHost.Current = ParserOptionsHost.Current with
        {
            Mode = value ? ParserMode.Strict : ParserMode.Lenient,
        };
    }

    // Localized menu labels.
    public string MenuFile           => L("Menu_File",                   "_File");
    public string MenuFileOpenFile   => L("Menu_File_OpenFile",          "Open _File...");
    public string MenuFileOpenFolder => L("Menu_File_OpenFolder",        "Open F_older...");
    public string MenuFileExit       => L("Menu_File_Exit",              "E_xit");
    public string MenuView           => L("Menu_View",                   "_View");
    public string MenuViewLanguage   => L("Menu_View_Language",          "_Language");
    public string MenuViewLanguageEn => L("Menu_View_Language_English",  "English");
    public string MenuViewLanguageRo => L("Menu_View_Language_Romanian", "Română");
    public string MenuBuild          => L("Menu_Build",                  "_Build");

    public MainWindowViewModel(
        IStringLocalizer<Strings> localizer,
        ILanguageService language,
        IDocumentService documents,
        DockFactory factory,
        WorkspaceExplorerToolViewModel workspaceTool,
        ObjectBrowserToolViewModel objectBrowserTool,
        DiagnosticsToolViewModel diagnosticsTool,
        CompilerOutputToolViewModel compilerOutputTool,
        GeneratedFilesToolViewModel generatedFilesTool,
        XviToolViewModel xviTool,
        SettingsToolViewModel settingsTool,
        IModelEditService? editService = null,
        ILayoutService? layout = null,
        IAppSettingsService? settings = null)
    {
        _l = localizer;
        _language = language;
        _documents = documents;
        _editService = editService;
        _layout = layout;
        _settings = settings;
        _factory = factory;

        WorkspaceTool = workspaceTool;
        ObjectBrowserTool = objectBrowserTool;
        DiagnosticsTool = diagnosticsTool;
        CompilerOutputTool = compilerOutputTool;
        GeneratedFilesTool = generatedFilesTool;
        XviTool = xviTool;
        SettingsTool = settingsTool;

        Layout = _factory.CreateLayout();
        _factory.InitLayout(Layout);

        // Bridge the document service and the dock host (kept decoupled to avoid a DI cycle).
        // OpenFileAsync resumes on a thread-pool thread (ConfigureAwait(false)), so adding
        // the document to the dock — which mutates UI-bound VisibleDockables — must be
        // marshalled to the UI thread.
        _documents.OpenInDockRequested += (_, doc) => OnUiThread(() => _factory.OpenDocument(doc));
        _factory.ActiveDockableChanged += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.SetActive(doc);
        };
        _factory.DockableClosed += (_, e) =>
        {
            if (e.Dockable is FileDocumentViewModel doc) _documents.CloseDocument(doc);
        };

        _language.LanguageChanged += (_, _) => Refresh();
        _documents.DocumentChanged += (_, _) => RefreshActiveTools();
        ObjectBrowser.ShotEditRequested += async (_, e) => await ApplyShotEditAsync(e).ConfigureAwait(true);
        WorkspaceExplorer.OpenRequested += async (_, node) => await OpenNodeAsync(node).ConfigureAwait(true);

        Build.CompileCompleted += (_, diags) =>
        {
            var combined = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
            combined.AddRange(_documents.CurrentDiagnostics);
            combined.AddRange(diags);
            Diagnostics.Load(combined.ToImmutable());
        };
        Diagnostics.NavigateRequested += (_, row) => NavigateTo(row.Span);
        Build.NavigateRequested += (_, span) => NavigateTo(span);

        Refresh();
        RestoreSessionOrSample();
    }

    public MainWindowViewModel() : this(
        new NullLocalizer(),
        new LanguageService(),
        new NullDocumentService(),
        DesignFactory(),
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new SettingsToolViewModel(new SettingsViewModel(), new KeyboardShortcutsViewModel()))
    {
        // Designer-only.
    }

    private static DockFactory DesignFactory() => new(
        new WorkspaceExplorerToolViewModel(new WorkspaceExplorerViewModel()),
        new ObjectBrowserToolViewModel(new ObjectBrowserViewModel()),
        new DiagnosticsToolViewModel(new DiagnosticsViewModel()),
        new CompilerOutputToolViewModel(new BuildViewModel()),
        new GeneratedFilesToolViewModel(new BuildViewModel()),
        new XviToolViewModel(new XviReferencesViewModel()),
        new SettingsToolViewModel(new SettingsViewModel(), new KeyboardShortcutsViewModel()));

    /// <summary>Wires the storage picker once the View is attached to a TopLevel.</summary>
    public void AttachStoragePicker(IStoragePicker picker) => _picker = picker;

    /// <summary>Reopens last session's files (when enabled), else shows the sample document.</summary>
    private void RestoreSessionOrSample()
    {
        var s = _settings?.Current;
        var files = s is { RestoreSessionOnStartup: true } ? s.LastSessionFiles : Array.Empty<string>();
        _ = RestoreSessionAsync(files);
    }

    private async Task RestoreSessionAsync(System.Collections.Generic.IReadOnlyList<string> files)
    {
        bool opened = false;
        foreach (var path in files)
        {
            if (!System.IO.File.Exists(path)) continue;
            try { await _documents.OpenFileAsync(path).ConfigureAwait(true); opened = true; }
            catch { /* skip files that fail to open */ }
        }
        if (!opened) _documents.OpenTextDocument("(sample).th", SampleText);
    }

    /// <summary>Records the currently-open (on-disk) files so they can be restored next launch.</summary>
    public void PersistSession()
    {
        if (_settings is null) return;
        var paths = new System.Collections.Generic.List<string>();
        foreach (var doc in _documents.Documents)
        {
            if (!string.IsNullOrEmpty(doc.FilePath) && System.IO.File.Exists(doc.FilePath))
                paths.Add(doc.FilePath);
        }
        try { _settings.Save(_settings.Current with { LastSessionFiles = paths }); }
        catch { /* best-effort */ }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (_picker is null) return;
        var path = await _picker.PickOpenFileAsync(MenuFileOpenFile).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            await _documents.OpenFileAsync(path).ConfigureAwait(true);
            StatusText = path;
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        if (_picker is null) return;
        var folder = await _picker.PickOpenFolderAsync(MenuFileOpenFolder).ConfigureAwait(true);
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            await _documents.OpenFolderAsync(folder).ConfigureAwait(true);
            StatusText = _documents.CurrentPath ?? folder;
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand] private void SwitchToEnglish()  => _language.SetLanguage("en");
    [RelayCommand] private void SwitchToRomanian() => _language.SetLanguage("ro");

    [RelayCommand] private void ToggleWorkspaceExplorer() => Activate(WorkspaceTool);
    [RelayCommand] private void ToggleDiagnostics()       => Activate(DiagnosticsTool);

    private void Activate(IDockable tool)
    {
        try { _factory.SetActiveDockable(tool); } catch { /* best-effort focus */ }
    }

    private async Task OpenNodeAsync(WorkspaceNode node)
    {
        try { await _documents.OpenFileAsync(node.FullPath).ConfigureAwait(true); }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private void NavigateTo(Therion.Core.SourceSpan span)
    {
        // Open the document the span lives in (if needed), activate it, then scroll/flash.
        _ = _documents.NavigateToSpanAsync(span);
    }

    private async Task ApplyShotEditAsync(ShotEditEventArgs e)
    {
        if (_editService is null || _documents.CurrentAst is null) return;
        if (e.Row.SourceRow is not { } source || e.Row.FieldDefinition is not { } fields) return;

        int idx = -1;
        for (int i = 0; i < fields.Fields.Length; i++)
        {
            if (string.Equals(fields.Fields[i], e.Field, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        }
        if (idx < 0) return;

        var values = source.Values.ToBuilder();
        while (values.Count <= idx) values.Add("-");
        values[idx] = e.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

        var replacement = source with { Values = values.ToImmutable() };
        var result = _editService.ReplaceNode(_documents.CurrentAst, source, replacement);
        if (!result.Success || result.UpdatedText is null)
        {
            StatusText = result.Diagnostics.Length > 0 ? result.Diagnostics[0].Message : "Edit failed.";
            return;
        }
        try
        {
            await _documents.WriteCurrentTextAsync(result.UpdatedText).ConfigureAwait(true);
            StatusText = $"Shot {e.Row.From} → {e.Row.To} updated.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private static void OnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private void RefreshActiveTools()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshActiveTools);
            return;
        }

        if (_documents.Workspace is { } workspace)
            ObjectBrowser.Load(workspace);
        else if (_documents.CurrentSemantics is { } model)
            ObjectBrowser.Load(model);
        else
            ObjectBrowser.Load(SemanticModel.Empty);

        Diagnostics.Load(_documents.CurrentDiagnostics);
        XviReferences.Refresh();
        WorkspaceExplorer.Refresh();
    }

    private void Refresh()
    {
        Title = _l["AppTitle"];
        OnPropertyChanged(nameof(MenuFile));
        OnPropertyChanged(nameof(MenuFileOpenFile));
        OnPropertyChanged(nameof(MenuFileOpenFolder));
        OnPropertyChanged(nameof(MenuFileExit));
        OnPropertyChanged(nameof(MenuView));
        OnPropertyChanged(nameof(MenuViewLanguage));
        OnPropertyChanged(nameof(MenuViewLanguageEn));
        OnPropertyChanged(nameof(MenuViewLanguageRo));
        OnPropertyChanged(nameof(MenuBuild));
    }

    private string L(string key, string fallback)
    {
        var v = _l[key];
        return v.ResourceNotFound ? fallback : v.Value;
    }

    private sealed class NullLocalizer : IStringLocalizer<Strings>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);
        public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);
        public System.Collections.Generic.IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            System.Array.Empty<LocalizedString>();
    }
}
