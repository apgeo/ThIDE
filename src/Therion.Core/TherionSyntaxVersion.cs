// Therion source-of-truth reference: see TherionVersion.json (pinned tag).
// Implementation Plan §1 (Therion.Core layer), §2 (TherionSyntaxVersion).

namespace Therion.Core;

/// <summary>
/// Identifies a specific version of the Therion grammar / commands.
/// Attached to every produced model and diagnostic for traceability.
/// </summary>
/// <param name="Major">Major Therion version (e.g., 6).</param>
/// <param name="Minor">Minor Therion version (e.g., 4).</param>
/// <param name="Patch">Patch Therion version (e.g., 0).</param>
/// <param name="Source">
/// Provenance of the version info, e.g.
/// <c>"therion-src@&lt;git-sha&gt;"</c> or <c>"thbook@6.4.0"</c>.
/// </param>
public sealed record TherionSyntaxVersion(int Major, int Minor, int Patch, string Source)
{
    /// <summary>Default baseline used when no other version is supplied.</summary>
    public static TherionSyntaxVersion Default { get; } =
        new(6, 4, 0, "thbook@6.4.0");

    public override string ToString() => $"{Major}.{Minor}.{Patch} ({Source})";
}
