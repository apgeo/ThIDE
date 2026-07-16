// Where a project's *user* state lives: the things a caver decides about a project that must never
// be written into the survey source. Lead triage (open → checked → pushed → dead) and project
// metadata (region, licence, notes) are both of that kind — editing them is instant, reversible, and
// cannot corrupt survey data.
//
// Kept in a per-root JSON sidecar under the app-data directory rather than beside the project, so a
// read-only or version-controlled project tree stays clean.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Therion.Workspace;

/// <summary>Locates the per-workspace-root sidecar files.</summary>
public static class ProjectSidecar
{
    /// <summary>
    /// A stable, filesystem-safe key for a workspace root. Hashed rather than escaped so a deeply
    /// nested project cannot produce a filename longer than the filesystem allows.
    /// </summary>
    public static string? KeyFor(string? root)
    {
        if (string.IsNullOrEmpty(root)) return null;

        string normalized;
        try { normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToLowerInvariant(); }
        catch { normalized = root.ToLowerInvariant(); }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }

    /// <summary>The app-data directory holding one kind of sidecar (<c>leads</c>, <c>metadata</c>, …).</summary>
    public static string DirectoryFor(string kind)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "ThIDE", kind);
    }
}
