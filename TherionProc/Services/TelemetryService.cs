// opt-in, privacy-respecting crash reporting & anonymous usage telemetry.
//
// Default OFF. When the user opts in (Preferences ▸ General), usage events and crash reports are
// written to LOCAL files only — under %AppData%/TherionProc/telemetry — and never sent anywhere.
// Events carry no personal data or file contents: just an event name, a UTC timestamp, and
// non-identifying environment info (OS + app version). This gives the user a local diagnostic
// trail without any network egress, which a future opt-in uploader could build on.

using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TherionProc.Services;

public interface ITelemetryService
{
    /// <summary>True when the user has opted in (otherwise every call is a no-op).</summary>
    bool IsEnabled { get; }
    /// <summary>Records an anonymous usage event (no-op unless opted in).</summary>
    void TrackEvent(string name, string? detail = null);
    /// <summary>Writes a local crash report for an exception (no-op unless opted in).</summary>
    void ReportException(Exception ex, string context);
}

public sealed class LocalTelemetryService : ITelemetryService
{
    private readonly IAppSettingsService? _settings;
    private readonly ILogger? _logger;
    private readonly string _dir;
    private readonly object _gate = new();

    public LocalTelemetryService(IAppSettingsService? settings = null, ILogger<LocalTelemetryService>? logger = null)
        : this(DefaultDir(), settings, logger) { }

    public LocalTelemetryService(string dir, IAppSettingsService? settings, ILogger? logger)
    {
        _dir = dir;
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled => _settings?.Current.TelemetryEnabled ?? false;

    public void TrackEvent(string name, string? detail = null)
    {
        if (!IsEnabled || string.IsNullOrEmpty(name)) return;
        try
        {
            Directory.CreateDirectory(_dir);
            var line = string.Join('\t',
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                name,
                detail ?? string.Empty);
            lock (_gate) File.AppendAllText(Path.Combine(_dir, "usage.log"), line + Environment.NewLine);
        }
        catch (Exception logEx) { _logger?.LogDebug(logEx, "Telemetry event not recorded."); }
    }

    public void ReportException(Exception ex, string context)
    {
        if (!IsEnabled || ex is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.txt");
            var report =
                $"UTC: {DateTimeOffset.UtcNow:o}{Environment.NewLine}" +
                $"Context: {context}{Environment.NewLine}" +
                $"OS: {Environment.OSVersion}{Environment.NewLine}" +
                $"App: {TherionAppVersion()}{Environment.NewLine}" +
                $"Exception: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
            File.WriteAllText(file, report);
        }
        catch (Exception logEx) { _logger?.LogDebug(logEx, "Crash report not written."); }
    }

    private static string TherionAppVersion()
    {
        try { return typeof(LocalTelemetryService).Assembly.GetName().Version?.ToString() ?? "unknown"; }
        catch { return "unknown"; }
    }

    private static string DefaultDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "TherionProc", "telemetry");
    }
}
