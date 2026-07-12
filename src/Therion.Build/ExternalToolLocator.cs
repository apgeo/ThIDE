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
    /// <summary>Survex <c>dump3d</c> — dumps a .3d model to text.</summary>
    public const string Dump3d = "dump3d";
    /// <summary>Survex <c>extend</c> — makes an extended elevation from a .3d.</summary>
    public const string Extend = "extend";
    /// <summary>Blender (≥ 4.2), used headless by the Blender Animation module to render.</summary>
    public const string Blender = "blender";

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

        // Blender installs under "Blender Foundation\Blender <X.Y>" (installer/Steam) or as a
        // Microsoft Store package under WindowsApps — not the "Therion/Survex/<tool>" pattern below.
        if (string.Equals(toolId, Blender, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var p in BlenderCandidatePaths()) yield return p;
            yield break;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Look under both Program Files roots (a 64-bit host, or a 32-bit installer like
            // Survex, may land the exe in either), across the vendor sub-folders that bundle these
            // tools: "Therion" (therion/loch), "Survex" (aven/dump3d/extend — installs to
            // "Program Files (x86)\Survex"), and a folder named after the tool itself. Only paths
            // that actually exist are used by the caller, so listing all combinations is cheap.
            foreach (var root in ProgramFilesRoots())
                foreach (var vendor in new[] { "Therion", "Survex", toolId })
                    yield return Path.Combine(root, vendor, toolId + ".exe");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Therion.app/Contents/MacOS/" + toolId;
            yield return "/Applications/Survex.app/Contents/MacOS/" + toolId;
            yield return "/usr/local/bin/" + toolId;
            yield return "/opt/homebrew/bin/" + toolId;
        }
        else
        {
            yield return "/usr/bin/" + toolId;
            yield return "/usr/local/bin/" + toolId;
        }
    }

    /// <summary>Distinct Program Files roots on Windows (64-bit + the x86 root), most-specific first.</summary>
    private static IEnumerable<string> ProgramFilesRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(f);
            if (!string.IsNullOrEmpty(root) && seen.Add(root)) yield return root;
        }
    }

    /// <summary>Blender install locations: the "Blender Foundation" installer/Steam folders and,
    /// on Windows, the Microsoft Store package under WindowsApps (best-effort — that directory is
    /// ACL-locked, so enumeration may fail; the exact path via a user override always works).</summary>
    private static IEnumerable<string> BlenderCandidatePaths()
    {
        var found = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in ProgramFilesRoots())
            {
                // %ProgramFiles%\Blender Foundation\Blender <X.Y>\blender.exe (newest picked by the caller).
                var foundation = Path.Combine(root, "Blender Foundation");
                TryAddDirs(found, foundation, "Blender*", "blender.exe");
                // Microsoft Store: %ProgramFiles%\WindowsApps\BlenderFoundation.Blender_*\Blender\blender.exe
                TryAddDirs(found, Path.Combine(root, "WindowsApps"), "BlenderFoundation.Blender_*", Path.Combine("Blender", "blender.exe"));
            }
            // Store execution alias (on PATH-adjacent), if the package registered one.
            var localApps = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localApps))
                found.Add(Path.Combine(localApps, "Microsoft", "WindowsApps", "blender.exe"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            found.Add("/Applications/Blender.app/Contents/MacOS/Blender");
        }
        else
        {
            found.Add("/usr/bin/blender");
            found.Add("/usr/local/bin/blender");
            found.Add("/snap/bin/blender");
            found.Add("/var/lib/flatpak/exports/bin/org.blender.Blender");
        }
        return found;
    }

    /// <summary>Adds <c>&lt;dir&gt;/&lt;version-dir&gt;/&lt;leaf&gt;</c> for each subdirectory of
    /// <paramref name="parent"/> matching <paramref name="pattern"/>. Silent on ACL/IO errors.</summary>
    private static void TryAddDirs(List<string> into, string parent, string pattern, string leaf)
    {
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent, pattern))
                into.Add(Path.Combine(dir, leaf));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* WindowsApps is ACL-locked */ }
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
