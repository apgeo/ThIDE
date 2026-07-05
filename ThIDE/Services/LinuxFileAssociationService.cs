// Linux IFileAssociationService: freedesktop xdg-mime associations (per-user, no root). On first
// use it writes a desktop entry (~/.local/share/applications/thide.desktop) plus a custom
// shared-mime-info package declaring one MIME type per Therion extension, refreshes the caches, then
// sets the app as the default handler with `xdg-mime default`. Requires xdg-utils to be installed;
// everything is best-effort and guarded, so a missing tool degrades to "not supported" rather than
// throwing. Instantiated solely by the factory on Linux.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace ThIDE.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxFileAssociationService : IFileAssociationService
{
    private const string DesktopFile = "thide.desktop";
    private readonly string _exePath;
    private readonly string _appsDir;
    private readonly string _mimeDir;
    private readonly bool _supported;

    public LinuxFileAssociationService()
    {
        _exePath = FileAssociationCatalog.ExecutablePath();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
            ? x : Path.Combine(home, ".local", "share");
        _appsDir = Path.Combine(dataHome, "applications");
        _mimeDir = Path.Combine(dataHome, "mime");
        _supported = !string.IsNullOrEmpty(_exePath) && Which("xdg-mime");
    }

    public bool IsSupported => _supported;

    public IReadOnlyList<string> SupportedExtensions => FileAssociationCatalog.Extensions();

    private static string Mime(string ext) => "application/x-therion-" + ext.TrimStart('.');

    public FileAssociationInfo GetInfo(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        var desc = FileAssociationCatalog.DescriptionFor(ext);
        if (!_supported) return new(ext, desc, FileAssociationState.Unknown, null);
        try
        {
            var def = Run("xdg-mime", "query", "default", Mime(ext))?.Trim();
            if (string.IsNullOrEmpty(def)) return new(ext, desc, FileAssociationState.NotAssociated, null);
            if (string.Equals(def, DesktopFile, StringComparison.OrdinalIgnoreCase))
                return new(ext, desc, FileAssociationState.Associated, "ThIDE");
            return new(ext, desc, FileAssociationState.AssociatedWithOther, def);
        }
        catch { return new(ext, desc, FileAssociationState.Unknown, null); }
    }

    public bool Associate(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        if (!_supported) return false;
        try
        {
            EnsureDesktopAndMime();
            RunExit("xdg-mime", "default", DesktopFile, Mime(ext));
            return GetInfo(ext).State == FileAssociationState.Associated;
        }
        catch { return false; }
    }

    public bool Unassociate(string extension)
    {
        var ext = FileAssociationCatalog.Normalize(extension);
        if (!_supported) return false;
        try
        {
            // xdg has no "unset default"; drop our line from ~/.config/mimeapps.list.
            string cfgHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c
                ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            string list = Path.Combine(cfgHome, "mimeapps.list");
            if (File.Exists(list))
            {
                var kept = new List<string>();
                foreach (var ln in File.ReadAllLines(list))
                    if (!ln.TrimStart().StartsWith(Mime(ext) + "=", StringComparison.Ordinal)) kept.Add(ln);
                File.WriteAllLines(list, kept);
            }
            return GetInfo(ext).State != FileAssociationState.Associated;
        }
        catch { return false; }
    }

    // Writes the desktop entry + a custom MIME package covering every supported extension, then
    // refreshes the freedesktop caches so `xdg-mime default` resolves the types.
    private void EnsureDesktopAndMime()
    {
        Directory.CreateDirectory(_appsDir);
        Directory.CreateDirectory(Path.Combine(_mimeDir, "packages"));

        var mime = new StringBuilder();
        mime.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        mime.AppendLine("<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">");
        var mimeTypes = new List<string>();
        foreach (var ext in SupportedExtensions)
        {
            mimeTypes.Add(Mime(ext));
            mime.AppendLine($"  <mime-type type=\"{Mime(ext)}\">");
            mime.AppendLine($"    <comment>{FileAssociationCatalog.DescriptionFor(ext)}</comment>");
            mime.AppendLine($"    <glob pattern=\"*{ext}\"/>");
            mime.AppendLine("  </mime-type>");
        }
        mime.AppendLine("</mime-info>");
        File.WriteAllText(Path.Combine(_mimeDir, "packages", "thide.xml"), mime.ToString());

        var desktop = new StringBuilder();
        desktop.AppendLine("[Desktop Entry]");
        desktop.AppendLine("Type=Application");
        desktop.AppendLine("Name=ThIDE");
        desktop.AppendLine("Comment=Therion survey editor");
        desktop.AppendLine($"Exec=\"{_exePath}\" %F");
        desktop.AppendLine("Terminal=false");
        desktop.AppendLine("Categories=Science;Education;Utility;");
        desktop.AppendLine("MimeType=" + string.Join(";", mimeTypes) + ";");
        File.WriteAllText(Path.Combine(_appsDir, DesktopFile), desktop.ToString());

        RunExit("update-mime-database", _mimeDir);
        RunExit("update-desktop-database", _appsDir);
    }

    private static bool Which(string tool)
    {
        try { return RunExit("/usr/bin/env", "which", tool) == 0; } catch { return false; }
    }

    private static string? Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return null;
        string o = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(4000);
        return o;
    }

    private static int RunExit(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return -1;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(8000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
