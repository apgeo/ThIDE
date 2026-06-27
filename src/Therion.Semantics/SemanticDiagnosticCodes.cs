// Implementation Plan �5.2 � semantic diagnostic codes.
// Mirrors src/Therion.Syntax/DiagnosticCodes.cs.

namespace Therion.Semantics;

/// <summary>Diagnostic codes emitted by the semantic layer.</summary>
public static class SemanticDiagnosticCodes
{
    public const string UnresolvedStation   = "TH_SEM_001";
    public const string DuplicateFix        = "TH_SEM_002";
    public const string MalformedReference  = "TH_SEM_003";
    public const string OrphanFixedStation  = "TH_SEM_004";
    /// <summary>A data row's column count doesn't match its declared reading order (LANG-05).</summary>
    public const string DataRowArity        = "TH_SEM_005";
    /// <summary>A data-row value isn't valid for its reading (e.g. a non-numeric length/compass/clino).</summary>
    public const string DataValueInvalid    = "TH_SEM_006";
    /// <summary>A data-row value parses but is outside the expected range for its reading (e.g. compass &gt; 360).</summary>
    public const string DataValueRange      = "TH_SEM_007";
    /// <summary>A user-authored naming-convention lint was violated (LANG-13).</summary>
    public const string NamingConvention    = "TH_SEM_NAMING";

    // XVI semantic codes (image / file resolution, transform validation).
    public const string XviImageMissing         = "TH_XVI_001";
    public const string XviFileMissing          = "TH_XVI_002";
    public const string XviTransformDegenerate  = "TH_XVI_003";
}
