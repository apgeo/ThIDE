// Backs the Preferences window. Holds editable copies of the common application
// settings; Apply() writes them back through IAppSettingsService (preserving the
// auto-managed session file list).

using CommunityToolkit.Mvvm.ComponentModel;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;

    [ObservableProperty] private bool _restoreSessionOnStartup;
    [ObservableProperty] private double _editorFontSize;
    [ObservableProperty] private int _indentationSize;
    [ObservableProperty] private bool _showLineNumbers;
    [ObservableProperty] private bool _highlightCurrentLine;
    [ObservableProperty] private bool _convertTabsToSpaces;
    [ObservableProperty] private bool _autoReloadExternalChanges;
    [ObservableProperty] private bool _autoReloadGraphOnExternalChange;

    public PreferencesViewModel(IAppSettingsService settings)
    {
        _settings = settings;
        var s = settings.Current;
        _restoreSessionOnStartup = s.RestoreSessionOnStartup;
        _editorFontSize = s.EditorFontSize;
        _indentationSize = s.IndentationSize;
        _showLineNumbers = s.ShowLineNumbers;
        _highlightCurrentLine = s.HighlightCurrentLine;
        _convertTabsToSpaces = s.ConvertTabsToSpaces;
        _autoReloadExternalChanges = s.AutoReloadExternalChanges;
        _autoReloadGraphOnExternalChange = s.AutoReloadGraphOnExternalChange;
    }

    public PreferencesViewModel() : this(new AppSettingsService()) { } // design-time

    /// <summary>Persists the edited values (keeps the existing session file list).</summary>
    public void Apply()
    {
        _settings.Save(_settings.Current with
        {
            RestoreSessionOnStartup = RestoreSessionOnStartup,
            EditorFontSize = EditorFontSize,
            IndentationSize = IndentationSize,
            ShowLineNumbers = ShowLineNumbers,
            HighlightCurrentLine = HighlightCurrentLine,
            ConvertTabsToSpaces = ConvertTabsToSpaces,
            AutoReloadExternalChanges = AutoReloadExternalChanges,
            AutoReloadGraphOnExternalChange = AutoReloadGraphOnExternalChange,
        });
    }
}
