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

    // ---- workspace ----
    [ObservableProperty] private bool _autoReloadExternalChanges;
    [ObservableProperty] private bool _autoReloadGraphOnExternalChange;

    // ---- build outputs ----
    [ObservableProperty] private bool _openLoxAfterBuild;
    [ObservableProperty] private bool _open3dAfterBuild;
    [ObservableProperty] private bool _openPdfAfterBuild;
    /// <summary>True = open every matching output; false = open just the first.</summary>
    [ObservableProperty] private bool _openAllOutputsAfterBuild;

    // ---- editor behaviour ----
    [ObservableProperty] private bool _showRenamePreviewBeforeApply;

    // ---- keyboard shortcuts (moved here from the Settings panel, #11) ----
    public KeyboardShortcutsViewModel? Keyboard { get; }

    // ---- sections + search ----------------------------------------------
    private readonly List<PreferenceSection> _allSections;
    [ObservableProperty] private ObservableCollection<PreferenceSection> _sections;
    [ObservableProperty] private PreferenceSection? _selectedSection;
    [ObservableProperty] private string _searchQuery = string.Empty;

    public PreferencesViewModel(IAppSettingsService settings,
        KeyboardShortcutsViewModel? keyboard = null, ILanguageService? language = null)
    {
        _settings = settings;
        _language = language;
        Keyboard = keyboard;

        var s = settings.Current;
        _restoreSessionOnStartup = s.RestoreSessionOnStartup;
        _languageIndex = string.Equals(s.UiLanguage, "ro", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _editorFontSize = s.EditorFontSize;
        _indentationSize = s.IndentationSize;
        _showLineNumbers = s.ShowLineNumbers;
        _highlightCurrentLine = s.HighlightCurrentLine;
        _convertTabsToSpaces = s.ConvertTabsToSpaces;
        _autoReloadExternalChanges = s.AutoReloadExternalChanges;
        _autoReloadGraphOnExternalChange = s.AutoReloadGraphOnExternalChange;
        _openLoxAfterBuild = s.OpenLoxAfterBuild;
        _open3dAfterBuild = s.Open3dAfterBuild;
        _openPdfAfterBuild = s.OpenPdfAfterBuild;
        _openAllOutputsAfterBuild = s.OpenAllOutputsAfterBuild;
        _showRenamePreviewBeforeApply = s.ShowRenamePreviewBeforeApply;

        _allSections = new List<PreferenceSection>
        {
            new("general",  "General",             "startup session reopen language english romanian locale"),
            new("editor",   "Editor",              "font size indent line numbers highlight tabs spaces rename preview"),
            new("workspace","Workspace",           "reload external graph disk watch"),
            new("build",    "Build & Output",      "build output open lox 3d pdf survex aven loch"),
            new("keyboard", "Keyboard Shortcuts",  "key binding gesture shortcut hotkey command"),
        };
        _sections = new ObservableCollection<PreferenceSection>(_allSections);
        _selectedSection = _sections.FirstOrDefault();
    }

    public PreferencesViewModel() : this(new AppSettingsService()) { } // design-time

    // ---- which section is shown (drives content IsVisible) --------------
    public bool IsGeneral   => SelectedSection?.Id == "general";
    public bool IsEditor    => SelectedSection?.Id == "editor";
    public bool IsWorkspace => SelectedSection?.Id == "workspace";
    public bool IsBuild     => SelectedSection?.Id == "build";
    public bool IsKeyboard  => SelectedSection?.Id == "keyboard";

    partial void OnSelectedSectionChanged(PreferenceSection? value)
    {
        OnPropertyChanged(nameof(IsGeneral));
        OnPropertyChanged(nameof(IsEditor));
        OnPropertyChanged(nameof(IsWorkspace));
        OnPropertyChanged(nameof(IsBuild));
        OnPropertyChanged(nameof(IsKeyboard));
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
        _settings.Save(_settings.Current with
        {
            RestoreSessionOnStartup = RestoreSessionOnStartup,
            UiLanguage = code,
            EditorFontSize = EditorFontSize,
            IndentationSize = IndentationSize,
            ShowLineNumbers = ShowLineNumbers,
            HighlightCurrentLine = HighlightCurrentLine,
            ConvertTabsToSpaces = ConvertTabsToSpaces,
            AutoReloadExternalChanges = AutoReloadExternalChanges,
            AutoReloadGraphOnExternalChange = AutoReloadGraphOnExternalChange,
            OpenLoxAfterBuild = OpenLoxAfterBuild,
            Open3dAfterBuild = Open3dAfterBuild,
            OpenPdfAfterBuild = OpenPdfAfterBuild,
            OpenAllOutputsAfterBuild = OpenAllOutputsAfterBuild,
            ShowRenamePreviewBeforeApply = ShowRenamePreviewBeforeApply,
        });
        _language?.SetLanguage(code); // reflect the new language immediately
    }
}
