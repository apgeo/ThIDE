// Implementation Plan §5.2 — semantic diagnostic codes.
// Mirrors src/Therion.Syntax/DiagnosticCodes.cs.

namespace Therion.Semantics;

/// <summary>Diagnostic codes emitted by the semantic layer.</summary>
public static class SemanticDiagnosticCodes
{
    public const string UnresolvedStation   = "TH_SEM_001";
    public const string DuplicateFix        = "TH_SEM_002";
    public const string MalformedReference  = "TH_SEM_003";
    public const string OrphanFixedStation  = "TH_SEM_004";

    // XVI semantic codes (image / file resolution, transform validation).
    public const string XviImageMissing         = "TH_XVI_001";
    public const string XviFileMissing          = "TH_XVI_002";
    public const string XviTransformDegenerate  = "TH_XVI_003";
}
