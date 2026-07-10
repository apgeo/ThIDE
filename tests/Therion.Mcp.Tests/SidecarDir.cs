namespace Therion.Mcp.Tests;

/// <summary>
/// A throwaway directory for the sidecar stores. Without one they write to the real
/// %AppData%/ThIDE, so a test run would rewrite the developer's own lead statuses.
/// </summary>
internal static class SidecarDir
{
    public static string New()
    {
        var path = Path.Combine(Path.GetTempPath(), "thmcp_sidecar_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
