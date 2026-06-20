// Implementation Plan §6.1 — sniffer guardrails (Decision #21).

namespace Therion.Workspace;

/// <summary>Static guardrails for <see cref="ThconfigSniffer"/>. Tweak in code.</summary>
public static class SnifferConstants
{
    /// <summary>Files larger than this are skipped without inspection.</summary>
    public const int SnifferMaxFileSizeBytes = 64 * 1024;

    /// <summary>First N bytes used for the binary-content probe.</summary>
    public const int SnifferBinaryProbeBytes = 4 * 1024;

    /// <summary>Maximum number of tokens fed to the sniffer.</summary>
    public const int SnifferMaxTokens = 256;
}
