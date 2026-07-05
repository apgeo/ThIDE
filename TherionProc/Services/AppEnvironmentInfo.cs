// Collects app / OS / runtime / bundled-component / external-tool / display information for the
// About window (#1) and the Help ▸ Debug Info window (#2). Renders a Notepad++-style text report
// that the user can copy. Deliberately omits personal data: user name, machine/host name and any
// network identifiers are never gathered, and every path shown is redacted (home dir → "~").

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Therion.Build;
using Therion.Processing.Abstractions;

namespace TherionProc.Services;

/// <summary>Detection result for one external tool (Therion / Survex / Mapiah).</summary>
public sealed record ToolReport(string Id, string Name, string Url, bool Detected, string? Version, string? Path);

public static class AppEnvironmentInfo
{
    /// <summary>This project's public source repository.</summary>
    public const string RepositoryUrl = "https://github.com/apgeo/ThIDE";
    /// <summary>Homepage of the bundled 3D viewer library.</summary>
    public const string CaveViewUrl = "https://github.com/aardgoose/CaveView.js";

    /// <summary>The external tools we detect + link to (id, display name, homepage).</summary>
    public static readonly IReadOnlyList<(string Id, string Name, string Url)> KnownTools = new[]
    {
        (ExternalToolLocator.Therion, "Therion",       "https://therion.speleo.sk"),
        (ExternalToolLocator.Aven,    "Survex (aven)", "https://survex.com"),
        (ExternalToolLocator.Mapiah,  "Mapiah",        "https://github.com/rsevero/mapiah"),
    };

    // ---- versions -----------------------------------------------------------

    /// <summary>The app's SemVer display version (informational → file → assembly), build suffix friendly.</summary>
    public static string AppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) return FormatInformationalVersion(info);
        var file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (!string.IsNullOrWhiteSpace(file)) return file;
        return asm.GetName().Version?.ToString() ?? "0.0.0";
    }

    // "0.2.0-beta.1+build.250" → "0.2.0-beta.1 (build 250)"; "+build.0"/other "+meta" is dropped.
    private static string FormatInformationalVersion(string info)
    {
        int plus = info.IndexOf('+');
        if (plus < 0) return info;
        var core = info[..plus];
        var meta = info[(plus + 1)..];
        const string buildPrefix = "build.";
        if (meta.StartsWith(buildPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var n = meta[buildPrefix.Length..];
            if (n.Length > 0 && n != "0") return $"{core} (build {n})";
        }
        return core;
    }

    private static string? _caveViewVersion;

    /// <summary>Version of the vendored CaveView.js bundle, read from its banner (cached), else a known fallback.</summary>
    public static string CaveViewVersion()
    {
        if (_caveViewVersion is not null) return _caveViewVersion;
        try
        {
            var uri = new Uri("avares://TherionProc/Assets/caveview/CaveView/js/CaveView2.js");
            using var s = AssetLoader.Open(uri);
            using var reader = new StreamReader(s, Encoding.ASCII);
            var text = reader.ReadToEnd();
            var m = Regex.Match(text, @"CaveView v(\d+\.\d+(?:\.\d+)?)");
            if (m.Success) return _caveViewVersion = m.Groups[1].Value;
        }
        catch { /* fall back to the pinned bundle version */ }
        return _caveViewVersion = "2.9.0";
    }

    private static string AvaloniaVersion()
    {
        var asm = typeof(Avalonia.Application).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info)) { int plus = info.IndexOf('+'); return plus < 0 ? info : info[..plus]; }
        return asm.GetName().Version?.ToString() ?? "unknown";
    }

    // ---- external tools -----------------------------------------------------

    /// <summary>Detects the known external tools (async: some spawn a short <c>--version</c> probe).</summary>
    public static async Task<IReadOnlyList<ToolReport>> DetectToolsAsync(
        IExternalToolLocator? locator, CancellationToken ct = default)
    {
        var list = new List<ToolReport>();
        foreach (var (id, name, url) in KnownTools)
        {
            ToolInfo? info = null;
            if (locator is not null)
                try { info = await locator.FindAsync(id, ct).ConfigureAwait(false); } catch { /* treat as not found */ }

            var version = info?.Version;
            // aven is a GUI app we never spawn for --version; its companion CLI `cavern` prints the
            // Survex version safely, so probe that when aven was found without a version.
            if (version is null && info?.Path is { } avenPath
                && string.Equals(id, ExternalToolLocator.Aven, StringComparison.OrdinalIgnoreCase))
                version = await ProbeSurvexVersionAsync(avenPath, ct).ConfigureAwait(false);

            list.Add(new ToolReport(id, name, url, info is not null, version, info?.Path));
        }
        return list;
    }

    private static async Task<string?> ProbeSurvexVersionAsync(string avenPath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(avenPath);
            string cavern = "cavern"; // PATH fallback
            if (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "cavern.exe" : "cavern");
                if (File.Exists(candidate)) cavern = candidate;
            }
            return await RunVersionAsync(cavern, "--version", ct).ConfigureAwait(false);
        }
        catch { return null; }
    }

    // Runs `<exe> --version`, returns its first non-empty output line (short + informative), or null.
    private static async Task<string?> RunVersionAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe, Arguments = args,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
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
            var firstLine = text.Split('\n', 2)[0].Trim();
            return firstLine.Length == 0 ? null : firstLine;
        }
        catch { return null; }
    }

    // ---- OS / runtime -------------------------------------------------------

    /// <summary>Human-readable OS + runtime lines (label : value), free of personal identifiers.</summary>
    public static IReadOnlyList<string> SystemLines()
    {
        var v = Environment.OSVersion.Version;
        return new List<string>
        {
            $"OS Name : {RuntimeInformation.OSDescription} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})",
            $"OS Version : {v.Major}.{v.Minor}",
            $"OS Build : {v.Build}",
            $"Architecture : OS {RuntimeInformation.OSArchitecture}, process {RuntimeInformation.ProcessArchitecture}",
            $"Runtime : {RuntimeInformation.FrameworkDescription}",
            $"Avalonia : {AvaloniaVersion()}",
            $"UI culture : {CultureInfo.CurrentUICulture.Name}; formatting {CultureInfo.CurrentCulture.Name}",
            $"Current ANSI codepage : {CultureInfo.CurrentCulture.TextInfo.ANSICodePage}",
            $"Admin mode : {AdminMode()}",
        };
    }

    private static string AdminMode()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(id);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) ? "ON" : "OFF";
            }
            catch { return "unknown"; }
        }
        return "n/a";
    }

    // ---- display ------------------------------------------------------------

    /// <summary>Monitor geometry + scaling lines from the owner window's <see cref="Screens"/>.</summary>
    public static IReadOnlyList<string> DisplayLines(Screens? screens)
    {
        var lines = new List<string>();
        if (screens is null) { lines.Add("(display info unavailable)"); return lines; }
        if (screens.Primary is { } primary)
            lines.Add($"primary monitor: {primary.Bounds.Width}x{primary.Bounds.Height}, scaling {primary.Scaling * 100:0}%");
        lines.Add($"visible monitors count: {screens.ScreenCount}");
        var all = screens.All;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            lines.Add($"    [{i}] {s.Bounds.Width}x{s.Bounds.Height} @ ({s.Bounds.X},{s.Bounds.Y}) " +
                      $"scaling {s.Scaling * 100:0}%{(s.IsPrimary ? " (primary)" : string.Empty)}");
        }
        return lines;
    }

    // ---- redaction ----------------------------------------------------------

    /// <summary>Redacts personal identifiers from a path/command line: home directory → "~", user name → "&lt;user&gt;".</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var result = text;
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home)) result = result.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }
        catch { /* ignore */ }
        try
        {
            var user = Environment.UserName;
            if (!string.IsNullOrEmpty(user)) result = result.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        }
        catch { /* ignore */ }
        return result;
    }

    // ---- full report --------------------------------------------------------

    /// <summary>Builds the copy-ready diagnostic report (About "Copy info" + Debug Info window).</summary>
    public static string BuildReport(IReadOnlyList<ToolReport> tools, Screens? screens)
    {
        var sb = new StringBuilder();
        var bitness = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        sb.AppendLine($"TherionProc v{AppVersion()}   ({bitness})");
        sb.AppendLine($"Repository : {RepositoryUrl}");
        sb.AppendLine();

        sb.AppendLine("Bundled components");
        sb.AppendLine($"    CaveView.js : v{CaveViewVersion()}   ({CaveViewUrl})");
        sb.AppendLine();

        sb.AppendLine("External tools");
        foreach (var t in tools)
        {
            var status = t.Version is { Length: > 0 } v ? v : (t.Detected ? "detected (version unknown)" : "not detected");
            sb.AppendLine($"    {t.Name,-14}: {status}   ({t.Url})");
            if (t.Detected && !string.IsNullOrEmpty(t.Path))
                sb.AppendLine($"    {string.Empty,-14}  {Redact(t.Path)}");
        }
        sb.AppendLine();

        sb.AppendLine("System");
        foreach (var line in SystemLines()) sb.AppendLine($"    {line}");
        sb.AppendLine();

        sb.AppendLine("Display");
        foreach (var line in DisplayLines(screens)) sb.AppendLine($"    {line}");
        sb.AppendLine();

        sb.AppendLine("Process");
        sb.AppendLine($"    Path : {Redact(Environment.ProcessPath)}");
        sb.AppendLine($"    Working dir : {Redact(Environment.CurrentDirectory)}");
        sb.AppendLine($"    Command line : {Redact(Environment.CommandLine)}");
        try { sb.AppendLine($"    Started : {Process.GetCurrentProcess().StartTime:yyyy-MM-dd HH:mm:ss}"); }
        catch { /* start time may be inaccessible */ }

        return sb.ToString();
    }
}
