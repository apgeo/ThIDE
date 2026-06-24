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
        var exe = await ResolvePathAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(exe))
            return new MapiahLaunchResult(MapiahLaunchStatus.NotInstalled);

        try
        {
            // Pass the .th2 as the first argument so Mapiah opens it directly. UseShellExecute
            // is false so we don't pop a console and so the wrapper/flatpak launcher gets argv.
            var psi = new ProcessStartInfo(exe, $"\"{th2Path}\"") { UseShellExecute = false };
            Process.Start(psi);
            return new MapiahLaunchResult(MapiahLaunchStatus.Launched, exe);
        }
        catch (Exception ex)
        {
            return new MapiahLaunchResult(MapiahLaunchStatus.LaunchFailed, exe, ex.Message);
        }
    }
}
