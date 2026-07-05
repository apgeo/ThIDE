// Application preferences + session state, persisted as JSON under
// %AppData%/TherionProc/settings.json (XDG fallback on POSIX). Mirrors the
// JsonLayoutService pattern. Holds both user-editable preferences (shown in the
// Preferences window) and auto-managed session state (the files that were open
// at last shutdown, restored on next launch when enabled).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TherionProc.Services;

/// <summary>Workspace-panel presentation: relational object graph vs. plain file explorer.</summary>
public enum WorkspaceViewMode
{
    Relational = 0,
    FileExplorer = 1,
}

/// <summary>
/// Which file categories the Go-to-File palette (Ctrl+P) searches. A [Flags] enum so categories
/// can be independently enabled/disabled and new sources added later without breaking callers.
/// </summary>
[Flags]
public enum QuickOpenSources
{
    None = 0,
    History = 1,              // recently-opened + currently-open files (always useful)
    Directory = 2,            // every Therion file under the active workspace root
    ThconfigConnected = 4,    // only files that participate in the active thconfig's object graph
}

/// <summary>when the editor auto-saves dirty files.</summary>
public enum AutoSaveMode
{
    Off = 0,
    AfterDelay = 1,    // periodically while a file stays dirty
    OnFocusLoss = 2,   // when the app window loses focus
}

/// <summary>File-explorer sort key (Windows-Explorer-style), applied within each folder (#15).</summary>
public enum WorkspaceSortMode
{
    Name = 0,
    Modified = 1,
    Size = 2,
    Type = 3,
    Created = 4,
}

public sealed record AppSettings
{
    // ---- session ----
    /// <summary>Reopen the files that were open when the app last closed.</summary>
    public bool RestoreSessionOnStartup { get; init; } = true;
    /// <summary>Absolute paths of files open at last shutdown (most-recent active last).</summary>
    public IReadOnlyList<string> LastSessionFiles { get; init; } = Array.Empty<string>();
    /// <summary>last caret offset per open file, restored when the tab reopens.</summary>
    public IReadOnlyDictionary<string, int> SessionCaretOffsets { get; init; } = new Dictionary<string, int>();
    /// <summary>Recently-opened files, most-recent first; grouped per type in the menu (#8).</summary>
    public IReadOnlyList<string> RecentFiles { get; init; } = Array.Empty<string>();
    /// <summary>Pinned recent files: shown in their own group and never cleared by "Clear Recent".</summary>
    public IReadOnlyList<string> PinnedRecentFiles { get; init; } = Array.Empty<string>();
    /// <summary>Recently-opened working directories (workspace roots), most-recent first; listed in
    /// the File ▸ Recent Directories submenu.</summary>
    public IReadOnlyList<string> RecentDirectories { get; init; } = Array.Empty<string>();

    // ---- workspace session (single root + single active thconfig) ----
    /// <summary>Root directory of the workspace at last shutdown (restored on launch, #9).</summary>
    public string? LastWorkspaceRoot { get; init; }
    /// <summary>Active thconfig path at last shutdown (restored on launch, #9).</summary>
    public string? LastActiveThconfig { get; init; }
    /// <summary>
    /// Last active thconfig chosen per workspace-root directory (full root path → full
    /// thconfig path). When a root is reopened, its remembered thconfig is reactivated
    /// instead of auto-picking one (task 1).
    /// </summary>
    public IReadOnlyDictionary<string, string> LastThconfigByRoot { get; init; } =
        new Dictionary<string, string>();
    /// <summary>Reload an open editor when its file changes on disk (clean files only; #6).</summary>
    public bool AutoReloadExternalChanges { get; init; } = true;
    /// <summary>Rebuild the object graph when a tracked file changes on disk (#5b).</summary>
    public bool AutoReloadGraphOnExternalChange { get; init; } = true;

    // ---- scripting / macro hooks ----
    /// <summary>Run the configured hook commands on open/save/build. Adds processing time, so it
    /// can be turned off for big projects (default on, but hooks only fire when commands are set).</summary>
    public bool EnableScriptHooks { get; init; } = true;
    /// <summary>Shell command run when a file is opened (<c>{file}</c> is substituted), or null.</summary>
    public string? HookOnOpen { get; init; }
    /// <summary>Shell command run when a file is saved (<c>{file}</c> is substituted), or null.</summary>
    public string? HookOnSave { get; init; }
    /// <summary>Shell command run when a build starts (<c>{file}</c> = active thconfig), or null.</summary>
    public string? HookOnBuild { get; init; }

    // ---- plugins ----
    /// <summary>Load external plugin assemblies (custom semantic rules) from the plugins folder.
    /// Plugin rules run during analysis, so this can be disabled for performance (default on).</summary>
    public bool EnablePlugins { get; init; } = true;

    // ---- telemetry ----
    /// <summary>Opt-in: record anonymous usage events + crash reports to LOCAL files only. Off by default.</summary>
    public bool TelemetryEnabled { get; init; }

    // ---- rename symbol ----
    /// <summary>Show a preview window before applying a symbol rename (#1).</summary>
    public bool ShowRenamePreviewBeforeApply { get; init; } = true;

    // ---- measurements column visibility ----
    public bool MColShotsSurvey { get; init; } = true;
    public bool MColShotsFrom { get; init; } = true;
    public bool MColShotsTo { get; init; } = true;
    public bool MColShotsLength { get; init; } = true;
    public bool MColShotsCompass { get; init; } = true;
    public bool MColShotsClino { get; init; } = true;
    public bool MColShotsSurface { get; init; } = true;
    public bool MColShotsDuplicate { get; init; } = true;
    public bool MColShotsSplay { get; init; } = true;
    public bool MColShotsApproximate { get; init; } = true;
    public bool MColShotsComment { get; init; } = true;
    public bool MColShotsLine { get; init; } = true;
    public bool MColStationsSurvey { get; init; } = true;
    public bool MColStationsKind { get; init; } = true;
    public bool MColStationsLine { get; init; } = true;

    // ---- build outputs (auto-open after a successful build) ----
    /// <summary>Open generated .lox files after a successful build.</summary>
    public bool OpenLoxAfterBuild { get; init; }
    /// <summary>Open generated Survex .3d files after a successful build.</summary>
    public bool Open3dAfterBuild { get; init; }
    /// <summary>Open generated .pdf files after a successful build.</summary>
    public bool OpenPdfAfterBuild { get; init; }
    /// <summary>Open every matching output (true) instead of just the first one (false).</summary>
    public bool OpenAllOutputsAfterBuild { get; init; }
    /// <summary>
    /// Per-output auto-open overrides (#7): full output path → true (always open) / false (never).
    /// Absent ⇒ use the general per-type setting above. Persisted across builds/sessions.
    /// </summary>
    public IReadOnlyDictionary<string, bool> AutoOpenOverrides { get; init; } =
        new Dictionary<string, bool>();
    /// <summary>automatically (re)build the active project a short moment after each save. Off by default.</summary>
    public bool CompileOnSave { get; init; }
    /// <summary>Before compiling, ensure each export's output directory exists, creating it
    /// recursively when missing (Therion otherwise fails on a non-existent output folder). On by default.</summary>
    public bool EnsureOutputDirectories { get; init; } = true;
    /// <summary>Before compiling, save any unsaved project files automatically instead of prompting.
    /// Off by default — when off, the build prompts to save the involved unsaved files first.</summary>
    public bool AutoSaveBeforeCompile { get; init; }

    // ---- auto-save ----
    /// <summary>When the editor auto-saves dirty files (off / after a delay / on focus loss).</summary>
    public AutoSaveMode AutoSave { get; init; } = AutoSaveMode.Off;
    /// <summary>Interval (seconds) for the AfterDelay auto-save mode.</summary>
    public int AutoSaveDelaySeconds { get; init; } = 30;

    // ---- visualization features ----
    /// <summary>the live centreline preview (plan/elevation sketch from our model).</summary>
    public bool EnableLivePreview { get; init; } = true;
    /// <summary>auto-load the latest rendered map output into the viewer after a build.</summary>
    public bool EnableMapAutoPreview { get; init; } = true;
    /// <summary>the in-app PNG/SVG/PDF map viewer.</summary>
    public bool EnableInAppViewer { get; init; } = true;
    /// <summary>Open a clicked PDF output in the in-app map viewer instead of the external app. On by default.</summary>
    public bool OpenPdfInInternalViewer { get; init; } = true;
    /// <summary>the embedded 3D model viewer (CaveView.js in a NativeWebView). On by default.</summary>
    public bool EnableModel3DViewer { get; init; } = true;
    /// <summary>auto-load the newest .lox/.3d into the 3D viewer after a build.</summary>
    public bool EnableModel3DAutoPreview { get; init; } = true;
    /// <summary>last-used 3D color-by shading mode (height / survey / length / inclination / single).</summary>
    public string Model3DShadingMode { get; init; } = "height";
    /// <summary>the structural-geology module (plane strike/dip calculator). On by default.</summary>
    public bool EnableStructuralGeology { get; init; } = true;
    /// <summary>persisted panel state (detection/declination options, columns, plot prefs) as JSON.</summary>
    public string StructuralGeologySettings { get; init; } = "";

    // ---- survey-domain analytics — disable on huge projects for performance ----
    /// <summary>compute the project statistics / charts / team / entrances /
    /// data-quality analytics. Adds processing time on every graph rebuild.</summary>
    public bool EnableProjectAnalytics { get; init; } = true;
    /// <summary>populate the extra Object Browser entity tabs (surveys, fixes, equates,
    /// scraps, maps, points, lines, areas). Walks the whole model on each load.</summary>
    public bool EnableObjectBrowserEntities { get; init; } = true;
    /// <summary>scan every project file's comments for TODO/FIXME/QM tags. Reads all files
    /// on each graph rebuild, so it can be disabled for big projects (default on).</summary>
    public bool EnableTodoScan { get; init; } = true;
    /// <summary>populate the Media manager (referenced .xvi scans + an on-disk orphan
    /// scan). Walks the project folder, so it can be disabled for big projects (default on).</summary>
    public bool EnableMediaScan { get; init; } = true;
    /// <summary>Recompute the exploration-leads register live from the unsaved editor buffers as you
    /// type (debounced), not only on save. On by default; turn off on very large projects — it
    /// re-analyses the project on each typing pause. The Leads panel's Rescan button always works.</summary>
    public bool AutoRecalcLeads { get; init; } = true;

    // ---- diagnostics behaviour ----
    /// <summary>
    /// When true, a bare <c>fix</c> (no <c>cs</c>) counts as grounding a disconnected survey piece,
    /// suppressing its "disconnected survey" warning (TH_SEM_015). Off by default: by default only a
    /// georeferenced fix exempts a piece, so a piece anchored solely by a local <c>fix 0 0 0</c> is
    /// still flagged as floating.
    /// </summary>
    public bool LocalFixGroundsDisconnected { get; init; }

    // ---- large-file guards (#10) ----
    /// <summary>Skip syntax highlighting + hover features above this line count.</summary>
    public int MaxHighlightLines { get; init; } = 25000;
    /// <summary>Skip syntax highlighting + hover features above this size in KB.</summary>
    public int MaxHighlightKB { get; init; } = 1024;
    /// <summary>Skip parsing/object-graph above this line count.</summary>
    public int MaxParseLines { get; init; } = 20000;
    /// <summary>Skip parsing/object-graph above this size in KB.</summary>
    public int MaxParseKB { get; init; } = 512;
    /// <summary>
    /// Maximum number of stations listed by the "Go to Symbol" / station search (Ctrl+Shift+P).
    /// Caps the symbol list so huge caves stay responsive while the fuzzy matcher narrows it.
    /// </summary>
    public int StationSearchLimit { get; init; } = 4000;

    /// <summary>
    /// Time budget (seconds) for reopening last-session files at startup. Once exceeded, the
    /// remaining files are skipped (a warning is logged) so a big project stays launchable. 0 = no
    /// limit. A future background-load mode will supersede this stop-gap.
    /// </summary>
    public int StartupLoadTimeoutSeconds { get; init; } = 20;

    // ---- theme (#2) ----
    /// <summary>App theme mode: "System", "Light", or "Dark".</summary>
    public string ThemeMode { get; init; } = "System";
    /// <summary>When true, the editor uses the custom syntax colors below instead of theme defaults.</summary>
    public bool UseCustomSyntaxColors { get; init; }
    // Custom syntax token colors as #RRGGBB hex (null ⇒ use the theme default for that token).
    public string? SyntaxKeywordColor { get; init; }
    public string? SyntaxIdentifierColor { get; init; }
    public string? SyntaxNumberColor { get; init; }
    public string? SyntaxStringColor { get; init; }
    public string? SyntaxCommentColor { get; init; }
    public string? SyntaxOptionColor { get; init; }
    public string? SyntaxPunctuationColor { get; init; }

    // ---- search history (#10) ----
    /// <summary>Recent Find-in-Files queries (most-recent first), capped at 50.</summary>
    public IReadOnlyList<string> RecentSearches { get; init; } = new List<string>();

    // ---- language ----
    /// <summary>UI language culture name (e.g. "en", "ro"); applied at startup (#9).</summary>
    public string UiLanguage { get; init; } = "en";

    // ---- editor preferences ----
    public double EditorFontSize { get; init; } = 13;
    public bool ShowLineNumbers { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;
    public bool ConvertTabsToSpaces { get; init; } = true;
    public int IndentationSize { get; init; } = 2;
    public bool EditorWordWrap { get; init; }
    /// <summary>
    /// When true (default), navigating to an identifier's declaration requires a double-click; a
    /// single click just places the caret (normal text-editing). When false, a single click on a
    /// navigable token jumps to its declaration like a hyperlink. Ctrl+click always navigates, and
    /// navigable identifiers are underlined on hover in either mode.
    /// </summary>
    public bool RequireDoubleClickToNavigate { get; init; } = true;

    // ---- editor features ----
    /// <summary>Per-feature runtime toggles for the editor features (default: all on).</summary>
    public EditorFeatureSettings EditorFeatures { get; init; } = new();

    // ---- whitespace rendering (View-menu toggles, gated by the feature) ----
    /// <summary>Render spaces (·) and tabs (→) in the editor.</summary>
    public bool EditorShowWhitespace { get; init; }
    /// <summary>Render an end-of-line marker (¶) at each line break.</summary>
    public bool EditorShowEndOfLine { get; init; }
    /// <summary>Draw vertical indentation guide lines.</summary>
    public bool EditorShowIndentGuides { get; init; }

    /// <summary>Run "Format Document" automatically on save.</summary>
    public bool EditorFormatOnSave { get; init; }

    /// <summary>
    /// Re-validate the whole project object graph from the unsaved editor buffers as you type
    /// (debounced), instead of only on save. Off by default — it re-analyses the project on every
    /// typing pause, which can be heavy on large projects.
    /// </summary>
    public bool ValidateOnType { get; init; }

    /// <summary>Show the code minimap (View-menu toggle, gated by the feature).</summary>
    public bool EditorShowMinimap { get; init; }

    // ---- quick open (Ctrl+P go-to-file) ----
    /// <summary>Which categories the Go-to-File palette searches (besides history). Default: history + directory.</summary>
    public QuickOpenSources QuickOpenSources { get; init; } = QuickOpenSources.History | QuickOpenSources.Directory;

    // ---- workspace panel ----
    public bool WorkspaceShowObjectModel { get; init; } = true;
    public bool WorkspaceRevealOnHover { get; init; }
    public bool WorkspaceRevealOnTabSwitch { get; init; }
    /// <summary>Relational (object graph) vs. file-explorer view (#5).</summary>
    public WorkspaceViewMode WorkspaceViewMode { get; init; } = WorkspaceViewMode.Relational;
    /// <summary>Show file nodes in the relational view; when off, only logical objects (#5a).</summary>
    public bool WorkspaceShowFilesInModel { get; init; } = true;
    /// <summary>File-explorer sort key (#15).</summary>
    public WorkspaceSortMode WorkspaceSortMode { get; init; } = WorkspaceSortMode.Name;
    /// <summary>File-explorer sort direction; true = ascending (#15).</summary>
    public bool WorkspaceSortAscending { get; init; } = true;

    public static AppSettings Default { get; } = new();
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    void Save(AppSettings settings);
    event EventHandler? Changed;
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string _path;
    private readonly ILogger? _logger;
    private AppSettings _state;

    public AppSettingsService(ILogger<AppSettingsService>? logger = null) : this(DefaultPath(), logger) { }

    public AppSettingsService(string path, ILogger<AppSettingsService>? logger = null)
    {
        _path = path;
        _logger = logger;
        _state = TryLoad() ?? AppSettings.Default;
    }

    public AppSettings Current => _state;

    public event EventHandler? Changed;

    public void Save(AppSettings settings)
    {
        _state = settings;
        TryWrite();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AppSettings? TryLoad()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load settings from {Path}; using defaults.", _path);
            return null;
        }
    }

    private void TryWrite()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path,
                JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist settings to {Path}; changes may be lost.", _path);
        }
    }

    private static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "TherionProc", "settings.json");
    }
}
