// Backs the Preferences window (#11). A Visual-Studio-2026-style options dialog: a
// searchable list of sections on the left, the selected section's settings on the
// right. Holds editable copies of the application settings; Apply() writes them back
// through IAppSettingsService (preserving the auto-managed session file list). The
// keyboard-shortcut editor (moved out of the Settings panel) lives in its own section.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One left-hand entry in the Preferences window; Keywords feed the search filter.</summary>
public sealed record PreferenceSection(string Id, string Title, string Keywords);

/// <summary>
/// One row in the "Editor Features" Preferences section — a per-feature runtime toggle (EDIT-*).
/// A feature compiled out via <see cref="EditorFeatureFlags"/> shows disabled and forced off.
/// </summary>
public sealed partial class EditorFeatureToggle : ObservableObject
{
    public EditorFeature Feature { get; }
    public string Code { get; }
    public string Title { get; }
    public string Description { get; }
    /// <summary>False when the compile-time master constant removed this feature (row is disabled).</summary>
    public bool IsCompiledIn { get; }

    [ObservableProperty] private bool _isEnabled;

    public EditorFeatureToggle(EditorFeatureInfo info, bool enabled)
    {
        Feature = info.Feature;
        Code = info.Code;
        Title = info.Title;
        Description = info.Description;
        IsCompiledIn = EditorFeatureFlags.Compiled(info.Feature);
        _isEnabled = enabled && IsCompiledIn;
    }
}

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly ILanguageService? _language;

    // ---- general ----
    [ObservableProperty] private bool _restoreSessionOnStartup;
    /// <summary>0 = English, 1 = Romanian (#9 selector lives in Preferences, #11).</summary>
    [ObservableProperty] private int _languageIndex;

    // ---- editor ----
    [ObservableProperty] private double _editorFontSize;
    [ObservableProperty] private int _indentationSize;
    [ObservableProperty] private bool _showLineNumbers;
    [ObservableProperty] private bool _highlightCurrentLine;
    [ObservableProperty] private bool _convertTabsToSpaces;
    [ObservableProperty] private bool _formatOnSave; // EDIT-04

    // ---- workspace ----
    [ObservableProperty] private bool _autoReloadExternalChanges;
    [ObservableProperty] private bool _autoReloadGraphOnExternalChange;
    /// <summary>#3: limit Ctrl+P to thconfig-connected files instead of every file in the folder.</summary>
    [ObservableProperty] private bool _quickOpenThconfigScope;

    // ---- build outputs ----
    [ObservableProperty] private bool _openLoxAfterBuild;
    [ObservableProperty] private bool _open3dAfterBuild;
    [ObservableProperty] private bool _openPdfAfterBuild;
    /// <summary>True = open every matching output; false = open just the first.</summary>
    [ObservableProperty] private bool _openAllOutputsAfterBuild;
    [ObservableProperty] private bool _compileOnSave;

    // ---- editor behaviour ----
    [ObservableProperty] private bool _showRenamePreviewBeforeApply;

    // ---- theme (#2) ----
    /// <summary>0 = System, 1 = Light, 2 = Dark.</summary>
    [ObservableProperty] private int _themeModeIndex;
    [ObservableProperty] private bool _useCustomSyntaxColors;
    [ObservableProperty] private string _syntaxKeywordColor = "";
    [ObservableProperty] private string _syntaxIdentifierColor = "";
    [ObservableProperty] private string _syntaxNumberColor = "";
    [ObservableProperty] private string _syntaxStringColor = "";
    [ObservableProperty] private string _syntaxCommentColor = "";
    [ObservableProperty] private string _syntaxOptionColor = "";
    [ObservableProperty] private string _syntaxPunctuationColor = "";

    // ---- performance / large-file guards (#10) ----
    [ObservableProperty] private int _maxHighlightLines;
    [ObservableProperty] private int _maxHighlightKB;
    [ObservableProperty] private int _maxParseLines;
    [ObservableProperty] private int _maxParseKB;
    [ObservableProperty] private int _stationSearchLimit;
    [ObservableProperty] private int _startupLoadTimeoutSeconds;

    // ---- editor features (Section B / EDIT-*) ----
    /// <summary>Per-feature runtime toggles shown in the "Editor Features" section.</summary>
    public ObservableCollection<EditorFeatureToggle> EditorFeatureRows { get; } = new();

    // ---- keyboard shortcuts (moved here from the Settings panel, #11) ----
    public KeyboardShortcutsViewModel? Keyboard { get; }

    // ---- external tools (moved here from the dockable Settings panel, #13) ----
    public SettingsViewModel? ExternalTools { get; }

    // ---- sections + search ----------------------------------------------
    private readonly List<PreferenceSection> _allSections;
    [ObservableProperty] private ObservableCollection<PreferenceSection> _sections;
    [ObservableProperty] private PreferenceSection? _selectedSection;
    [ObservableProperty] private string _searchQuery = string.Empty;

    public PreferencesViewModel(IAppSettingsService settings,
        KeyboardShortcutsViewModel? keyboard = null, ILanguageService? language = null,
        SettingsViewModel? externalTools = null)
    {
        _settings = settings;
        _language = language;
        Keyboard = keyboard;
        ExternalTools = externalTools;

        var s = settings.Current;
        _restoreSessionOnStartup = s.RestoreSessionOnStartup;
        _languageIndex = string.Equals(s.UiLanguage, "ro", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _editorFontSize = s.EditorFontSize;
        _indentationSize = s.IndentationSize;
        _showLineNumbers = s.ShowLineNumbers;
        _highlightCurrentLine = s.HighlightCurrentLine;
        _convertTabsToSpaces = s.ConvertTabsToSpaces;
        _formatOnSave = s.EditorFormatOnSave;
        _autoReloadExternalChanges = s.AutoReloadExternalChanges;
        _autoReloadGraphOnExternalChange = s.AutoReloadGraphOnExternalChange;
        _quickOpenThconfigScope = s.QuickOpenSources.HasFlag(QuickOpenSources.ThconfigConnected)
                                  && !s.QuickOpenSources.HasFlag(QuickOpenSources.Directory);
        _openLoxAfterBuild = s.OpenLoxAfterBuild;
        _open3dAfterBuild = s.Open3dAfterBuild;
        _openPdfAfterBuild = s.OpenPdfAfterBuild;
        _openAllOutputsAfterBuild = s.OpenAllOutputsAfterBuild;
        _compileOnSave = s.CompileOnSave;
        _showRenamePreviewBeforeApply = s.ShowRenamePreviewBeforeApply;
        _maxHighlightLines = s.MaxHighlightLines;
        _maxHighlightKB = s.MaxHighlightKB;
        _maxParseLines = s.MaxParseLines;
        _maxParseKB = s.MaxParseKB;
        _stationSearchLimit = s.StationSearchLimit;
        _startupLoadTimeoutSeconds = s.StartupLoadTimeoutSeconds;
        foreach (var info in EditorFeatureCatalog.All)
            EditorFeatureRows.Add(new EditorFeatureToggle(info, s.EditorFeatures.IsEnabled(info.Feature)));
        _themeModeIndex = s.ThemeMode switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _useCustomSyntaxColors = s.UseCustomSyntaxColors;
        _syntaxKeywordColor = s.SyntaxKeywordColor ?? "#0000C8";
        _syntaxIdentifierColor = s.SyntaxIdentifierColor ?? "#2472C8";
        _syntaxNumberColor = s.SyntaxNumberColor ?? "#00808C";
        _syntaxStringColor = s.SyntaxStringColor ?? "#A31515";
        _syntaxCommentColor = s.SyntaxCommentColor ?? "#008000";
        _syntaxOptionColor = s.SyntaxOptionColor ?? "#966E00";
        _syntaxPunctuationColor = s.SyntaxPunctuationColor ?? "#5A5A5A";

        // Section titles are localized (#2); the keyword lists stay English so search keeps
        // matching the same terms regardless of UI language.
        _allSections = new List<PreferenceSection>
        {
            new("general",  Resources.Tr.Get("Pref_General"),     "startup session reopen language english romanian locale"),
            new("theme",    Resources.Tr.Get("Pref_Theme"),       "theme dark light color syntax keyword identifier custom appearance"),
            new("editor",   Resources.Tr.Get("Pref_Editor"),      "font size indent line numbers highlight tabs spaces rename preview"),
            new("editorfeatures", "Editor Features",              "edit feature snippet completion signature outline minimap sticky breadcrumb peek split diff color whitespace format smart enter toggle enable disable"),
            new("performance",Resources.Tr.Get("Pref_Performance"),"large file limit highlight parse lines size kb threshold station search symbol cap startup load timeout session reopen"),
            new("workspace",Resources.Tr.Get("Pref_Workspace"),   "reload external graph disk watch"),
            new("build",    Resources.Tr.Get("Pref_Build"),       "build output open lox 3d pdf survex aven loch"),
            new("external", Resources.Tr.Get("Pref_External"),    "therion loch aven survex path detect tool executable"),
            new("keyboard", Resources.Tr.Get("Pref_Keyboard"),    "key binding gesture shortcut hotkey command"),
        };
        _sections = new ObservableCollection<PreferenceSection>(_allSections);
        _selectedSection = _sections.FirstOrDefault();
    }

    public PreferencesViewModel() : this(new AppSettingsService()) { } // design-time

    // ---- which section is shown (drives content IsVisible) --------------
    public bool IsGeneral     => SelectedSection?.Id == "general";
    public bool IsTheme       => SelectedSection?.Id == "theme";
    public bool IsEditor      => SelectedSection?.Id == "editor";
    public bool IsEditorFeatures => SelectedSection?.Id == "editorfeatures";
    public bool IsPerformance => SelectedSection?.Id == "performance";
    public bool IsWorkspace   => SelectedSection?.Id == "workspace";
    public bool IsBuild       => SelectedSection?.Id == "build";
    public bool IsExternal    => SelectedSection?.Id == "external";
    public bool IsKeyboard    => SelectedSection?.Id == "keyboard";

    partial void OnSelectedSectionChanged(PreferenceSection? value)
    {
        OnPropertyChanged(nameof(IsGeneral));
        OnPropertyChanged(nameof(IsTheme));
        OnPropertyChanged(nameof(IsEditor));
        OnPropertyChanged(nameof(IsEditorFeatures));
        OnPropertyChanged(nameof(IsPerformance));
        OnPropertyChanged(nameof(IsWorkspace));
        OnPropertyChanged(nameof(IsBuild));
        OnPropertyChanged(nameof(IsExternal));
        OnPropertyChanged(nameof(IsKeyboard));
    }

    /// <summary>Selects a section by id (e.g. to deep-link from a large-file banner, #10).</summary>
    public void SelectSectionById(string id)
    {
        var sec = _allSections.FirstOrDefault(s => s.Id == id);
        if (sec is not null) { SearchQuery = string.Empty; SelectedSection = sec; }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Filters the left section list by the search box (title + keywords, #11).</summary>
    private void ApplyFilter()
    {
        var q = SearchQuery?.Trim() ?? string.Empty;
        var matches = string.IsNullOrEmpty(q)
            ? _allSections
            : _allSections.Where(sec =>
                sec.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                sec.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        Sections = new ObservableCollection<PreferenceSection>(matches);
        if (SelectedSection is null || !Sections.Contains(SelectedSection))
            SelectedSection = Sections.FirstOrDefault();

        // When the keyboard section is shown, narrow its rows by the same query too.
        Keyboard?.SetFilter(q);
    }

    private string LanguageCode => LanguageIndex == 1 ? "ro" : "en";

    /// <summary>Persists the edited values (keeps the existing session file list) and applies language live.</summary>
    public void Apply()
    {
        var code = LanguageCode;
        var editorFeatures = _settings.Current.EditorFeatures;
        foreach (var row in EditorFeatureRows)
            editorFeatures = editorFeatures.With(row.Feature, row.IsEnabled);
        _settings.Save(_settings.Current with
        {
            EditorFeatures = editorFeatures,
            RestoreSessionOnStartup = RestoreSessionOnStartup,
            UiLanguage = code,
            EditorFontSize = EditorFontSize,
            IndentationSize = IndentationSize,
            ShowLineNumbers = ShowLineNumbers,
            HighlightCurrentLine = HighlightCurrentLine,
            ConvertTabsToSpaces = ConvertTabsToSpaces,
            EditorFormatOnSave = FormatOnSave,
            AutoReloadExternalChanges = AutoReloadExternalChanges,
            AutoReloadGraphOnExternalChange = AutoReloadGraphOnExternalChange,
            QuickOpenSources = QuickOpenThconfigScope
                ? QuickOpenSources.History | QuickOpenSources.ThconfigConnected
                : QuickOpenSources.History | QuickOpenSources.Directory,
            OpenLoxAfterBuild = OpenLoxAfterBuild,
            Open3dAfterBuild = Open3dAfterBuild,
            OpenPdfAfterBuild = OpenPdfAfterBuild,
            OpenAllOutputsAfterBuild = OpenAllOutputsAfterBuild,
            CompileOnSave = CompileOnSave,
            ShowRenamePreviewBeforeApply = ShowRenamePreviewBeforeApply,
            MaxHighlightLines = MaxHighlightLines,
            MaxHighlightKB = MaxHighlightKB,
            MaxParseLines = MaxParseLines,
            MaxParseKB = MaxParseKB,
            StationSearchLimit = StationSearchLimit,
            StartupLoadTimeoutSeconds = StartupLoadTimeoutSeconds,
            ThemeMode = ThemeModeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" },
            UseCustomSyntaxColors = UseCustomSyntaxColors,
            SyntaxKeywordColor = NullIfBlank(SyntaxKeywordColor),
            SyntaxIdentifierColor = NullIfBlank(SyntaxIdentifierColor),
            SyntaxNumberColor = NullIfBlank(SyntaxNumberColor),
            SyntaxStringColor = NullIfBlank(SyntaxStringColor),
            SyntaxCommentColor = NullIfBlank(SyntaxCommentColor),
            SyntaxOptionColor = NullIfBlank(SyntaxOptionColor),
            SyntaxPunctuationColor = NullIfBlank(SyntaxPunctuationColor),
        });
        _language?.SetLanguage(code); // reflect the new language immediately
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
