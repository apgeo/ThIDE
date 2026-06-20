// Implementation Plan §9bis.1 / §9bis.5. Locator: user overrides first,
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
            var v = await SniffVersionAsync(overridePath, cancellationToken).ConfigureAwait(false);
            return new ToolInfo(toolId, overridePath, v, Source: "override");
        }

        // 2) Well-known.
        foreach (var path in GetCandidatePaths(toolId))
        {
            if (File.Exists(path))
            {
                var v = await SniffVersionAsync(path, cancellationToken).ConfigureAwait(false);
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
                        var v = await SniffVersionAsync(full, cancellationToken).ConfigureAwait(false);
                        return new ToolInfo(toolId, full, v, Source: "PATH");
                    }
                }
                catch { /* skip invalid PATH entries */ }
            }
        }

        return null;
    }

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
}
