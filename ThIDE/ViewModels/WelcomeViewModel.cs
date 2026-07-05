// Welcome start page (shown as a document-well tab): quick actions (New / Open file / Open
// workspace), recent thconfig / workspace / file lists, a link to the Therion Book, and an
// external-tools status strip (Therion / Survex / Mapiah) with a shortcut into Settings ▸ External
// Tools. Picker-based actions raise events the shell wires to its existing commands; everything
// else (recents, tool detection, thbook) is self-contained via injected services.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

/// <summary>One recent-list entry: a display name, a dimmed detail (redacted parent path) and the full path.</summary>
public sealed record RecentItem(string Name, string Detail, string FullPath);

/// <summary>One external-tool status row on the welcome page.</summary>
public sealed record WelcomeToolStatus(string Name, string Url, bool Detected, string StatusText);

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;
    private readonly IAppSettingsService? _settings;
    private readonly IExternalToolLocator? _toolLocator;
    private readonly IThbookDocumentationService? _thbook;

    private const int MaxRecent = 8;

    public ObservableCollection<RecentItem> RecentThconfigs { get; } = new();
    public ObservableCollection<RecentItem> RecentWorkspaces { get; } = new();
    public ObservableCollection<RecentItem> RecentFiles { get; } = new();
    public ObservableCollection<WelcomeToolStatus> Tools { get; } = new();

    public bool HasRecentThconfigs => RecentThconfigs.Count > 0;
    public bool HasRecentWorkspaces => RecentWorkspaces.Count > 0;
    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string AppTitle => "ThIDE";
    public string AppVersion => AppEnvironmentInfo.AppVersion();

    /// <summary>Two-way bound to the "Show on startup" checkbox; persisted immediately.</summary>
    public bool ShowOnStartup
    {
        get => _settings?.Current.ShowWelcomeOnStartup ?? true;
        set
        {
            if (_settings is null || value == ShowOnStartup) return;
            _settings.Save(_settings.Current with { ShowWelcomeOnStartup = value });
            OnPropertyChanged();
        }
    }

    // Picker-/window-based actions handled by the shell (need the main window's TopLevel).
    public event EventHandler? NewFileRequested;
    public event EventHandler? OpenFileRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? ShowExternalToolsSettingsRequested;

    public WelcomeViewModel() { } // design-time

    public WelcomeViewModel(
        IDocumentService documents,
        IAppSettingsService settings,
        IExternalToolLocator? toolLocator = null,
        IThbookDocumentationService? thbook = null)
    {
        _documents = documents;
        _settings = settings;
        _toolLocator = toolLocator;
        _thbook = thbook;

        if (_settings is not null)
            _settings.Changed += (_, _) => Dispatcher.UIThread.Post(() => { RefreshRecents(); _ = DetectToolsAsync(); });

        RefreshRecents();
        _ = DetectToolsAsync();
    }

    /// <summary>Re-reads recents + re-detects tools when the welcome tab is (re)activated.</summary>
    public void OnActivated()
    {
        OnPropertyChanged(nameof(ShowOnStartup));
        RefreshRecents();
        _ = DetectToolsAsync();
    }

    // ---- actions ------------------------------------------------------------

    [RelayCommand] private void NewFile() => NewFileRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void OpenFile() => OpenFileRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void OpenFolder() => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void OpenThbook() => _thbook?.OpenAtPage(1);
    [RelayCommand] private void OpenExternalToolsSettings() => ShowExternalToolsSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task OpenRecentThconfig(RecentItem? item)
    {
        if (item is null || _documents is null) return;
        try { await _documents.ActivateThconfigAsync(item.FullPath, new ThconfigActivation(OpenInEditor: true)).ConfigureAwait(true); }
        catch { /* the shell surfaces open errors elsewhere; a stale recent is a no-op here */ }
    }

    [RelayCommand]
    private async Task OpenRecentWorkspace(RecentItem? item)
    {
        if (item is null || _documents is null) return;
        try { await _documents.OpenFolderAsync(item.FullPath).ConfigureAwait(true); }
        catch { /* ignore a stale recent directory */ }
    }

    [RelayCommand]
    private async Task OpenRecentFile(RecentItem? item)
    {
        if (item is null || _documents is null) return;
        try { await _documents.OpenFileAsync(item.FullPath).ConfigureAwait(true); }
        catch { /* ignore a stale recent file */ }
    }

    // ---- recents ------------------------------------------------------------

    private void RefreshRecents()
    {
        var s = _settings?.Current;
        var files = s?.RecentFiles ?? Array.Empty<string>();
        var dirs = s?.RecentDirectories ?? Array.Empty<string>();

        ReplaceItems(RecentThconfigs, files.Where(IsThconfig).Take(MaxRecent).Select(ToFileItem));
        ReplaceItems(RecentFiles, files.Where(p => !IsThconfig(p)).Take(MaxRecent).Select(ToFileItem));
        ReplaceItems(RecentWorkspaces, dirs.Take(MaxRecent).Select(ToDirItem));

        OnPropertyChanged(nameof(HasRecentThconfigs));
        OnPropertyChanged(nameof(HasRecentWorkspaces));
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private static void ReplaceItems(ObservableCollection<RecentItem> target, IEnumerable<RecentItem> items)
    {
        target.Clear();
        foreach (var it in items) target.Add(it);
    }

    private static RecentItem ToFileItem(string path) =>
        new(Path.GetFileName(path), AppEnvironmentInfo.Redact(SafeDir(path)), path);

    private static RecentItem ToDirItem(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) name = trimmed; // drive root
        return new RecentItem(name, AppEnvironmentInfo.Redact(SafeDir(trimmed)), path);
    }

    private static string SafeDir(string path)
    {
        try { return Path.GetDirectoryName(path) ?? string.Empty; } catch { return string.Empty; }
    }

    private static bool IsThconfig(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Length == 0
            ? string.Equals(Path.GetFileName(path), "thconfig", StringComparison.OrdinalIgnoreCase)
            : ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
    }

    // ---- external tools -----------------------------------------------------

    private async Task DetectToolsAsync()
    {
        var reports = await AppEnvironmentInfo.DetectToolsAsync(_toolLocator).ConfigureAwait(true);
        Tools.Clear();
        foreach (var r in reports)
        {
            var status = r.Version is { Length: > 0 } v ? v
                : r.Detected ? Tr.Get("About_Detected")
                : Tr.Get("About_NotDetected");
            Tools.Add(new WelcomeToolStatus(r.Name, r.Url, r.Detected, status));
        }
    }
}
