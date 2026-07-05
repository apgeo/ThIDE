// Implementation Plan �9bis.5 � Settings ? External Tools surface.
// Detects Therion / Loch / Aven binaries via IExternalToolLocator, allows
// user-edited override paths (persisted via IExternalToolPathOverrides),
// and offers a per-row Test button that re-runs detection.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace ThIDE.ViewModels;

public sealed partial class ExternalToolRow : ObservableObject
{
    private readonly IExternalToolPathOverrides? _overrides;
    private readonly IExternalToolLocator? _locator;
    private bool _suppress;

    public string ToolId { get; }

    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _overridePath = string.Empty;
    [ObservableProperty] private string _testResult = string.Empty;

    public ExternalToolRow(string toolId) { ToolId = toolId; }

    public ExternalToolRow(
        string toolId,
        IExternalToolPathOverrides overrides,
        IExternalToolLocator locator)
    {
        ToolId = toolId;
        _overrides = overrides;
        _locator = locator;
        if (overrides.Overrides.TryGetValue(toolId, out var p)) _overridePath = p;
    }

    public void UpdateDetected(string path, string version, string source)
    {
        _suppress = true;
        Path = path;
        Version = version;
        Source = source;
        _suppress = false;
    }

    partial void OnOverridePathChanged(string value)
    {
        if (_suppress || _overrides is null) return;
        _overrides.Set(ToolId, value);
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (_locator is null) { TestResult = "(no locator)"; return; }
        TestResult = "Testing�";
        var info = await _locator.FindAsync(ToolId).ConfigureAwait(true);
        if (info is null) { TestResult = "Not found"; return; }
        UpdateDetected(info.Path, info.Version ?? string.Empty, info.Source);
        TestResult = string.IsNullOrEmpty(info.Version) ? "OK" : $"OK ({info.Version})";
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IExternalToolLocator _locator;
    private readonly IExternalToolPathOverrides? _overrides;

    [ObservableProperty] private IReadOnlyList<ExternalToolRow> _tools = Array.Empty<ExternalToolRow>();
    [ObservableProperty] private string _status = string.Empty;

    public SettingsViewModel() : this(new ExternalToolLocator(), overrides: null) { }

    public SettingsViewModel(IExternalToolLocator locator)
        : this(locator, overrides: null) { }

    public SettingsViewModel(IExternalToolLocator locator, IExternalToolPathOverrides? overrides)
    {
        _locator = locator;
        _overrides = overrides;
        BuildRows();
        _ = RefreshAsync();
    }

    private void BuildRows()
    {
        var rows = new List<ExternalToolRow>();
        foreach (var id in new[] { ExternalToolLocator.Therion, ExternalToolLocator.Loch, ExternalToolLocator.Aven, ExternalToolLocator.Mapiah })
        {
            rows.Add(_overrides is null
                ? new ExternalToolRow(id)
                : new ExternalToolRow(id, _overrides, _locator));
        }
        Tools = rows;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Status = "Detecting�";
        foreach (var row in Tools)
        {
            var info = await _locator.FindAsync(row.ToolId).ConfigureAwait(true);
            if (info is null) row.UpdateDetected("(not found)", string.Empty, string.Empty);
            else row.UpdateDetected(info.Path, info.Version ?? string.Empty, info.Source);
        }
        Status = "Detection complete.";
    }
}
