// "Edit with Mapiah" support — launches the user-installed Mapiah .th2 sketch editor
// (https://github.com/rsevero/mapiah) on a .th2 file. We do not embed Mapiah; we detect
// its executable (well-known install paths + user override in Settings) and shell it out.
// When Mapiah saves the file, the workspace file watcher (plus a per-document watcher set
// up by the editor view) reloads the updated text automatically.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace TherionProc.Services;

/// <summary>Where the user can download Mapiah when it isn't installed/detected.</summary>
public static class MapiahLinks
{
    public const string Releases = "https://github.com/rsevero/mapiah/releases";
}

public enum MapiahLaunchStatus { Launched, NotInstalled, LaunchFailed }

public sealed record MapiahLaunchResult(MapiahLaunchStatus Status, string? ExePath = null, string? Error = null);

public interface IMapiahService
{
    /// <summary>Resolves the Mapiah executable path (override → well-known → PATH), or null.</summary>
    ValueTask<string?> ResolvePathAsync(CancellationToken ct = default);

    /// <summary>Launches Mapiah on <paramref name="th2Path"/>. Detects the executable first.</summary>
    ValueTask<MapiahLaunchResult> EditAsync(string th2Path, CancellationToken ct = default);
}

public sealed class MapiahService : IMapiahService
{
    private readonly IExternalToolLocator _locator;

    public MapiahService(IExternalToolLocator locator) => _locator = locator;

    public async ValueTask<string?> ResolvePathAsync(CancellationToken ct = default)
    {
        var info = await _locator.FindAsync(ExternalToolLocator.Mapiah, ct).ConfigureAwait(false);
        return info?.Path;
    }

    public async ValueTask<MapiahLaunchResult> EditAsync(string th2Path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(th2Path) || !System.IO.File.Exists(th2Path))
            return new MapiahLaunchResult(MapiahLaunchStatus.LaunchFailed, null, $"File not found: {th2Path}");

        var exe = await ResolvePathAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(exe))
            return new MapiahLaunchResult(MapiahLaunchStatus.NotInstalled);

        // Always pass an absolute path so a sandboxed (flatpak) Mapiah can resolve it.
        var file = System.IO.Path.GetFullPath(th2Path);

        // Pass the .th2 as the first argument so Mapiah opens it directly. UseShellExecute is false
        // so we don't pop a console and so the wrapper/flatpak launcher receives argv.
        if (TryStart(new ProcessStartInfo(exe, $"\"{file}\"") { UseShellExecute = false }, out var err))
            return new MapiahLaunchResult(MapiahLaunchStatus.Launched, exe);

        // Fallback 1: a flatpak wrapper whose filename is the app id → `flatpak run <id> <file>`.
        if (FlatpakAppId(exe) is { } appId &&
            TryStart(new ProcessStartInfo("flatpak", $"run {appId} \"{file}\"") { UseShellExecute = false }, out _))
            return new MapiahLaunchResult(MapiahLaunchStatus.Launched, exe);

        // Fallback 2: launch Mapiah without the file so the user can open it manually.
        if (TryStart(new ProcessStartInfo(exe) { UseShellExecute = false }, out _))
            return new MapiahLaunchResult(MapiahLaunchStatus.Launched, exe,
                "Opened Mapiah, but could not pass the file — open it manually.");

        return new MapiahLaunchResult(MapiahLaunchStatus.LaunchFailed, exe, err);
    }

    private static bool TryStart(ProcessStartInfo psi, out string? error)
    {
        try { Process.Start(psi); error = null; return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // A flatpak exports/bin wrapper is named after the app id (e.g. io.github.rsevero.mapiah).
    private static string? FlatpakAppId(string exePath)
    {
        if (!exePath.Replace('\\', '/').Contains("/flatpak/exports/bin/")) return null;
        var name = System.IO.Path.GetFileName(exePath);
        return name.Contains('.') ? name : null;
    }
}
