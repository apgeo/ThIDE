// Implementation Plan �9bis.1 / �9bis.5. Locator: user overrides first,
// then well-known install locations per OS, then PATH. Version sniffing
// spawns `<exe> --version` with a short timeout.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Therion.Processing.Abstractions;

namespace Therion.Build;

public sealed class ExternalToolLocator : IExternalToolLocator
{
    public const string Therion = "therion";
    public const string Loch = "loch";
    public const string Aven = "aven";
    /// <summary>The Mapiah .th2 sketch editor (a Flutter desktop app the user installs separately).</summary>
    public const string Mapiah = "mapiah";
    /// <summary>Survex <c>dump3d</c> — dumps a .3d model to text (BUILD-06).</summary>
    public const string Dump3d = "dump3d";
    /// <summary>Survex <c>extend</c> — makes an extended elevation from a .3d (BUILD-06).</summary>
    public const string Extend = "extend";

    // GUI viewers/editors: invoking `<exe> --version` actually pops their window open, so
    // we detect them by path only and never spawn them for version sniffing.
    private static readonly HashSet<string> GuiTools =
        new(StringComparer.OrdinalIgnoreCase) { Loch, Aven, Mapiah };

    private readonly IExternalToolPathOverrides? _overrides;

    public ExternalToolLocator() { }

    public ExternalToolLocator(IExternalToolPathOverrides overrides)
    {
        _overrides = overrides;
    }

    public async ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default)
    {
        // 1) User override.
        if (_overrides is not null
            && _overrides.Overrides.TryGetValue(toolId, out var overridePath)
            && !string.IsNullOrWhiteSpace(overridePath)
            && File.Exists(overridePath))
        {
            var v = await ResolveVersionAsync(toolId, overridePath, cancellationToken).ConfigureAwait(false);
            return new ToolInfo(toolId, overridePath, v, Source: "override");
        }

        // 2) Well-known.
        foreach (var path in GetCandidatePaths(toolId))
        {
            if (File.Exists(path))
            {
                var v = await ResolveVersionAsync(toolId, path, cancellationToken).ConfigureAwait(false);
                return new ToolInfo(toolId, path, v, Source: "well-known");
            }
        }

        // 3) PATH fallback.
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (envPath is not null)
        {
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? toolId + ".exe" : toolId;
            foreach (var dir in envPath.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var full = Path.Combine(dir, exeName);
                    if (File.Exists(full))
                    {
                        var v = await ResolveVersionAsync(toolId, full, cancellationToken).ConfigureAwait(false);
                        return new ToolInfo(toolId, full, v, Source: "PATH");
                    }
                }
                catch { /* skip invalid PATH entries */ }
            }
        }

        return null;
    }

    // Sniff the version unless the tool is a GUI viewer (which we must not spawn).
    private static async Task<string?> ResolveVersionAsync(string toolId, string exe, CancellationToken ct)
        => GuiTools.Contains(toolId) ? null : await SniffVersionAsync(exe, ct).ConfigureAwait(false);

    private static async Task<string?> SniffVersionAsync(string exe, CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            if (!p.Start()) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            try { await p.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return null; }
            var stdout = await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var stderr = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
            var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            if (string.IsNullOrWhiteSpace(text)) return null;
            // First non-empty line, trimmed; pull a version-like token if present.
            var firstLine = text.Split('\n', 2)[0].Trim();
            var m = Regex.Match(firstLine, @"\d+(\.\d+){1,3}");
            return m.Success ? m.Value : firstLine;
        }
        catch { return null; }
    }

    private static IEnumerable<string> GetCandidatePaths(string toolId)
    {
        // Mapiah ships as a stand-alone Flutter app, not part of the Therion bundle,
        // so its install locations differ from therion/loch/aven.
        if (string.Equals(toolId, Mapiah, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var p in MapiahCandidatePaths()) yield return p;
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(pf, "Therion", toolId + ".exe");
            yield return Path.Combine(pf, toolId, toolId + ".exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Therion.app/Contents/MacOS/" + toolId;
            yield return "/usr/local/bin/" + toolId;
            yield return "/opt/homebrew/bin/" + toolId;
        }
        else
        {
            yield return "/usr/bin/" + toolId;
            yield return "/usr/local/bin/" + toolId;
        }
    }

    private static IEnumerable<string> MapiahCandidatePaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Default Mapiah installer location on this dev machine is Program Files (x86).
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(pfx86)) yield return Path.Combine(pfx86, "Mapiah", "mapiah.exe");
            if (!string.IsNullOrEmpty(pf))    yield return Path.Combine(pf, "Mapiah", "mapiah.exe");
            if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, "Mapiah", "mapiah.exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Mapiah.app/Contents/MacOS/mapiah";
        }
        else
        {
            // Flatpak installs a launcher wrapper that forwards arguments into the sandboxed app.
            // The app id has been seen as both "io.github.rsevero.mapiah" (flathub) and
            // "org.mapiah.mapiah" — try both, system- and user-wide, plus the native locations.
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var id in new[] { "io.github.rsevero.mapiah", "org.mapiah.mapiah" })
            {
                yield return "/var/lib/flatpak/exports/bin/" + id;
                if (!string.IsNullOrEmpty(home))
                    yield return Path.Combine(home, ".local/share/flatpak/exports/bin/" + id);
            }
            yield return "/usr/bin/mapiah";
            yield return "/usr/local/bin/mapiah";
        }
    }
}
