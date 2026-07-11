// Blender discovery (BA-B10, doc 04). Order: explicit override → PATH → per-OS conventional
// locations. Every candidate is probed with `--version` (cached); the newest install ≥ the
// minimum wins. If the only installs found are too old, that is reported distinctly so the
// UI can say "update Blender" rather than "not found" (NFR-05, Mapiah-not-found pattern).

using System.Diagnostics;

namespace Therion.Blender.Execution;

/// <summary>Finds a usable Blender executable.</summary>
public sealed class BlenderLocator
{
    /// <summary>Generated scripts target Blender ≥ 4.2 LTS (D-02).</summary>
    public static readonly BlenderVersion MinimumVersion = new(4, 2, 0);

    private readonly IBlenderProbe _probe;
    private readonly Func<IEnumerable<string>> _candidateProvider;
    private readonly Dictionary<string, BlenderVersion?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="probe">Version prober (real runs the exe; fake for tests).</param>
    /// <param name="candidateProvider">Conventional candidate paths; defaults to the per-OS
    /// set so tests can inject a fixed list.</param>
    public BlenderLocator(IBlenderProbe probe, Func<IEnumerable<string>>? candidateProvider = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        _candidateProvider = candidateProvider ?? DefaultCandidates;
    }

    /// <summary>
    /// Locates Blender. <paramref name="overridePath"/> (Preferences ▸ External tools) is
    /// tried first; then PATH and the conventional locations. Returns the newest usable
    /// install, or a NotFound/TooOld result with a human detail.
    /// </summary>
    public BlenderLocateResult Locate(string? overridePath = null)
    {
        BlenderInstallation? newestUsable = null;
        BlenderInstallation? newestTooOld = null;

        foreach (var path in EnumerateCandidates(overridePath))
        {
            var version = ProbeCached(path);
            if (version is not { } v) continue;

            var install = new BlenderInstallation(path, v);
            if (v >= MinimumVersion)
            {
                if (newestUsable is null || v > newestUsable.Version) newestUsable = install;
            }
            else if (newestTooOld is null || v > newestTooOld.Version)
            {
                newestTooOld = install;
            }
        }

        if (newestUsable is not null)
            return new BlenderLocateResult(BlenderLocateStatus.Found, newestUsable);
        if (newestTooOld is not null)
            return new BlenderLocateResult(
                BlenderLocateStatus.TooOld, newestTooOld,
                $"Found Blender {newestTooOld.Version} at {newestTooOld.Path}, but {MinimumVersion} or newer is required.");
        return new BlenderLocateResult(
            BlenderLocateStatus.NotFound, null, "No Blender executable was found. Set its path in Preferences.");
    }

    private BlenderVersion? ProbeCached(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var version = _probe.Probe(path);
        _cache[path] = version;
        return version;
    }

    private IEnumerable<string> EnumerateCandidates(string? overridePath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(overridePath) && seen.Add(overridePath))
            yield return overridePath;
        foreach (var path in _candidateProvider())
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                yield return path;
    }

    /// <summary>The per-OS conventional candidate paths plus PATH entries. Only paths that
    /// exist on disk are returned (probing a missing path is wasted work).</summary>
    public static IEnumerable<string> DefaultCandidates()
    {
        foreach (var path in PathCandidates())
            if (File.Exists(path)) yield return path;
        foreach (var path in ConventionalCandidates())
            if (File.Exists(path)) yield return path;
    }

    private static IEnumerable<string> PathCandidates()
    {
        string exe = OperatingSystem.IsWindows() ? "blender.exe" : "blender";
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) yield break;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir, exe); }
            catch (ArgumentException) { continue; } // bad chars in a PATH segment
            yield return candidate;
        }
    }

    private static IEnumerable<string> ConventionalCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetEnvironmentVariable("ProgramW6432"),
                         Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var foundation = Path.Combine(root, "Blender Foundation");
                if (!Directory.Exists(foundation)) continue;
                IEnumerable<string> versionDirs;
                try { versionDirs = Directory.EnumerateDirectories(foundation, "Blender*"); }
                catch (IOException) { continue; }
                foreach (var dir in versionDirs)
                    yield return Path.Combine(dir, "blender.exe");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Blender.app/Contents/MacOS/Blender";
        }
        else // Linux and other Unix
        {
            yield return "/usr/bin/blender";
            yield return "/usr/local/bin/blender";
            yield return "/snap/bin/blender";
            yield return "/var/lib/flatpak/exports/bin/org.blender.Blender";
        }
    }
}

/// <summary>Real <see cref="IBlenderProbe"/>: runs <c>&lt;path&gt; --version</c> and parses stdout.</summary>
public sealed class ProcessBlenderProbe : IBlenderProbe
{
    private readonly int _timeoutMs;

    public ProcessBlenderProbe(int timeoutMs = 15_000) => _timeoutMs = timeoutMs;

    public BlenderVersion? Probe(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        try
        {
            var psi = new ProcessStartInfo(executablePath, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(_timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
            return BlenderVersion.TryParse(output, out var version) ? version : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null; // not executable / not present
        }
    }
}
