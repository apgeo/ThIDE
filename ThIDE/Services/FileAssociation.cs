// User-triggered file-type association (Task 5). Lets the user register the syntax-supported Therion
// file types (.th / .th2 / .thconfig / …) so double-clicking one opens it in ThIDE. This is
// NEVER applied automatically — the Preferences ▸ File Associations tab exposes it on demand and
// shows the current status per type. Implemented per-user (no elevation) where the OS allows it:
// Windows (HKCU registry) and Linux (freedesktop xdg-mime); other OSes report unsupported.

using System;
using System.Collections.Generic;

namespace ThIDE.Services;

/// <summary>Whether a Therion extension currently opens in this app, another app, or nothing.</summary>
public enum FileAssociationState { NotAssociated, Associated, AssociatedWithOther, Unknown }

/// <summary>Current association status for one extension (one row of the File Associations list).</summary>
public sealed record FileAssociationInfo(
    string Extension,
    string Description,
    FileAssociationState State,
    string? CurrentHandler);

/// <summary>
/// Registers / queries / removes the OS "open with" association between the syntax-supported Therion
/// file types and this application. Always user-triggered.
/// </summary>
public interface IFileAssociationService
{
    /// <summary>True when this OS supports managing associations from here (per-user, no admin rights).</summary>
    bool IsSupported { get; }
    /// <summary>The syntax-supported extensions the app can claim (each begins with a dot, lower-case).</summary>
    IReadOnlyList<string> SupportedExtensions { get; }
    /// <summary>Current status for one extension.</summary>
    FileAssociationInfo GetInfo(string extension);
    /// <summary>Make this app the default opener for <paramref name="extension"/>. Returns success.</summary>
    bool Associate(string extension);
    /// <summary>Remove this app's association for <paramref name="extension"/>. Returns success.</summary>
    bool Unassociate(string extension);
}

/// <summary>Shared metadata + platform selection for <see cref="IFileAssociationService"/>.</summary>
public static class FileAssociationCatalog
{
    /// <summary>Syntax-supported Therion file types (extension → human description).</summary>
    public static IReadOnlyList<(string Ext, string Description)> Types { get; } = new[]
    {
        (".th",       "Therion survey source"),
        (".th2",      "Therion 2D map / scrap"),
        (".thconfig", "Therion configuration"),
        (".thc",      "Therion configuration"),
        (".thl",      "Therion library"),
        (".xvi",      "Therion XVI scan"),
    };

    public static string DescriptionFor(string ext)
    {
        foreach (var (e, d) in Types)
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) return d;
        return "Therion file";
    }

    public static string[] Extensions()
    {
        var a = new string[Types.Count];
        for (int i = 0; i < Types.Count; i++) a[i] = Types[i].Ext;
        return a;
    }

    /// <summary>Normalizes user/CLI extension text to ".ext" lower-case form.</summary>
    public static string Normalize(string ext)
    {
        ext = (ext ?? string.Empty).Trim().ToLowerInvariant();
        if (ext.Length > 0 && ext[0] != '.') ext = "." + ext;
        return ext;
    }

    /// <summary>Full path of the running executable (used as the open command / desktop Exec).</summary>
    public static string ExecutablePath()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath)) return Environment.ProcessPath!;
            return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}

/// <summary>Selects the <see cref="IFileAssociationService"/> implementation for the host OS.</summary>
public static class FileAssociationServiceFactory
{
    public static IFileAssociationService Create()
        => OperatingSystem.IsWindows() ? new WindowsFileAssociationService()
         : OperatingSystem.IsLinux()   ? new LinuxFileAssociationService()
         : new UnsupportedFileAssociationService();
}

/// <summary>Fallback where associations can't be managed without elevation/bundling (e.g. macOS).</summary>
public sealed class UnsupportedFileAssociationService : IFileAssociationService
{
    public bool IsSupported => false;
    public IReadOnlyList<string> SupportedExtensions => FileAssociationCatalog.Extensions();
    public FileAssociationInfo GetInfo(string extension) =>
        new(FileAssociationCatalog.Normalize(extension),
            FileAssociationCatalog.DescriptionFor(FileAssociationCatalog.Normalize(extension)),
            FileAssociationState.Unknown, null);
    public bool Associate(string extension) => false;
    public bool Unassociate(string extension) => false;
}
