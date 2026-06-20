// Implementation Plan �7.1 / �7.3 / �7.6 � main shell ViewModel.
// Owns active-document state via IDocumentService and exposes Open File /
// Open Folder commands. Storage picker is injected so the VM remains testable.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Resources;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string SampleText =
        "# Sample Therion centreline \u2014 use File \u2192 Open File... or Open Folder... to load real data.\n" +
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
        "    flags splay\n" +
        "      3 4  2.0 270 -20\n" +
        "    flags not splay\n" +
        "    fix 0 100.0 200.0 -3.25\n" +
        "  endcentreline\n" +
        "endsurvey\n";

    private readonly IStringLocalizer<Strings> _l;
    private readonly ILanguageService _language;
    private readonly IDocumentService _documents;
    private readonly IModelEditService? _editService;
    private readonly ILayoutService? _layout;
    private IStoragePicker? _picker;

    /// <summary>Shell layout snapshot (Plan �7.2 / M6 #1). Bound by MainWindow.axaml splitters.</summary>
    public LayoutState Layout => _layout?.Current ?? LayoutState.Default;
    public ILayoutService? LayoutService => _layout;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _welcomeTitle = string.Empty;
    [ObservableProperty] private string _welcomeMessage = string.Empty;
    [ObservableProperty] private string _documentText = SampleText;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _currentFilePath;
    [ObservableProperty] private System.Collections.Generic.IReadOnlyList<Therion.Core.Diagnostic> _currentDiagnostics = System.Array.Empty<Therion.Core.Diagnostic>();

    /// <summary>Raised when a diagnostic / compiler-output row is clicked so the View can scroll the editor.</summary>
    public event System.EventHandler<Therion.Core.SourceSpan>? NavigateToSpanRequested;

    // Localized menu labels.
    public string MenuFile           => L("Menu_File",                   "_File");
    public string MenuFileOpenFile   => L("Menu_File_OpenFile",          "Open _File...");
    public string MenuFileOpenFolder => L("Menu_File_OpenFolder",        "Open F_older...");
    public string MenuFileExit       => L("Menu_File_Exit",              "E_xit");
    public string MenuView           => L("Menu_View",                   "_View");
    public string MenuViewLanguage   => L("Menu_View_Language",          "_Language");
    public string MenuViewLanguageEn => L("Menu_View_Language_English",  "English");
    public string MenuViewLanguageRo => L("Menu_View_Language_Romanian", "Rom\u00e2n\u0103");
    public string MenuBuild          => L("Menu_Build",                  "_Build");

    public ObjectBrowserViewModel ObjectBrowser { get; }
    public MeasurementsViewModel Measurements { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public BuildViewModel Build { get; }
    public WorkspaceExplorerViewModel WorkspaceExplorer { get; }
    public XviReferencesViewModel XviReferences { get; }
    public SettingsViewModel Settings { get; }
    public KeyboardShortcutsViewModel KeyboardShortcuts { get; }

    [ObservableProperty] private bool _strictParserMode;
    partial void OnStrictParserModeChanged(bool value)
    {
        ParserOptionsHost.Current = ParserOptionsHost.Current with
        {
            Mode = value ? ParserMode.Strict : ParserMode.Lenient,
        };
    }

    [ObservableProperty]
    private Therion.Processing.Abstractions.ISymbolNavigationService? _navigation;

    public MainWindowViewModel(
        IStringLocalizer<Strings> localizer,
        ILanguageService language,
        ObjectBrowserViewModel objectBrowser,
        MeasurementsViewModel measurements,
        IDocumentService documents,
        DiagnosticsViewModel diagnostics,
        BuildViewModel build,
        WorkspaceExplorerViewModel workspaceExplorer,
        XviReferencesViewModel xviReferences,
        SettingsViewModel settings,
        KeyboardShortcutsViewModel keyboardShortcuts,
        IModelEditService? editService = null,
        ILayoutService? layout = null)
    {
        _l = localizer;
        _language = language;
        _documents = documents;
        _editService = editService;
        _layout = layout;
        if (_layout is not null)
            _layout.LayoutChanged += (_, _) => OnPropertyChanged(nameof(Layout));
        ObjectBrowser = objectBrowser;
        Measurements = measurements;
        Diagnostics = diagnostics;
        Build = build;
        WorkspaceExplorer = workspaceExplorer;
        XviReferences = xviReferences;
        Settings = settings;
        KeyboardShortcuts = keyboardShortcuts;

        _language.LanguageChanged += (_, _) => Refresh();
        documents.DocumentChanged += (_, _) => SyncFromDocument();
        ObjectBrowser.ShotEditRequested += async (_, e) => await ApplyShotEditAsync(e).ConfigureAwait(true);
        Build.CompileCompleted += (_, diags) =>
        {
            // Merge compile diagnostics into the panel + squiggles.
            var combined = System.Collections.Immutable.ImmutableArray.CreateBuilder<Therion.Core.Diagnostic>();
            combined.AddRange(_documents.CurrentDiagnostics);
            combined.AddRange(diags);
            CurrentDiagnostics = combined.ToImmutable();
            Diagnostics.Load(CurrentDiagnostics);
        };
        Diagnostics.NavigateRequested += (_, row) => NavigateToSpanRequested?.Invoke(this, row.Span);
        Build.NavigateRequested += (_, span) => NavigateToSpanRequested?.Invoke(this, span);
        Refresh();
        LoadSampleIntoBrowser();
    }

    public MainWindowViewModel() : this(
        new NullLocalizer(),
        new LanguageService(),
        new ObjectBrowserViewModel(),
        new MeasurementsViewModel(),
        new NullDocumentService(),
        new DiagnosticsViewModel(),
        new BuildViewModel(),
        new WorkspaceExplorerViewModel(),
        new XviReferencesViewModel(),
        new SettingsViewModel(),
        new KeyboardShortcutsViewModel())
    {
        // Designer-only.
    }

    /// <summary>Wires the storage picker once the View is attached to a TopLevel.</summary>
    public void AttachStoragePicker(IStoragePicker picker) => _picker = picker;

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

    [RelayCommand]
    private void ToggleWorkspaceExplorer()
    {
        if (_layout is null) return;
        _layout.Save(_layout.Current with { LeftPaneVisible = !_layout.Current.LeftPaneVisible });
    }

    [RelayCommand]
    private void ToggleDiagnostics()
    {
        if (_layout is null) return;
        _layout.Save(_layout.Current with { BottomPaneVisible = !_layout.Current.BottomPaneVisible });
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
            StatusText = $"Shot {e.Row.From} ? {e.Row.To} updated.";
        }
        catch (Exception ex) { StatusText = ex.Message; }
    }

    private void SyncFromDocument()
    {
        // DocumentChanged is raised from IDocumentService after a ConfigureAwait(false)
        // file read, i.e. on a thread-pool thread. Updating bound UI state (notably the
        // Measurements DataGridCollectionView) must happen on the UI thread, otherwise
        // Avalonia throws "Call from invalid thread" and the open silently fails.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(SyncFromDocument);
            return;
        }

        DocumentText = string.IsNullOrEmpty(_documents.CurrentText) ? SampleText : _documents.CurrentText;
        CurrentFilePath = _documents.CurrentPath;
        CurrentDiagnostics = _documents.CurrentDiagnostics;
        Navigation = _documents.CurrentNavigation;
        Diagnostics.Load(_documents.CurrentDiagnostics);
        if (_documents.Workspace is { } workspace)
        {
            ObjectBrowser.Load(workspace);
            Measurements.Load(workspace);
        }
        else if (_documents.CurrentSemantics is { } model)
        {
            ObjectBrowser.Load(model);
            Measurements.Load(model);
        }
        XviReferences.Refresh();
        WorkspaceExplorer.Refresh();
    }

    private void LoadSampleIntoBrowser()
    {
        var parse = new ThParser().Parse("(sample)", SampleText);
        if (parse.Value is { } ast)
        {
            var model = new SemanticBinder().Bind(ast);
            ObjectBrowser.Load(model);
            Measurements.Load(model);
        }
    }

    private void Refresh()
    {
        Title = _l["AppTitle"];
        WelcomeTitle = _l["Welcome_Title"];
        WelcomeMessage = _l["Welcome_Message"];
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
