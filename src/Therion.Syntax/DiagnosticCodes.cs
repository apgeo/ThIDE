// Implementation Plan �10 (Diagnostics), Decision #16.
// Catalog versioned in docs/diagnostics.md. Codes are culture-invariant strings.

namespace Therion.Syntax;

/// <summary>Well-known diagnostic codes emitted by the parsing layer.</summary>
public static class DiagnosticCodes
{
    // -- Lexer / parser core ----------------------------------------------
    public const string LexFailed                = "TH0001";
    public const string UnexpectedToken          = "TH0002";
    public const string UnexpectedEndOfFile      = "TH0003";
    public const string UnknownCommand           = "TH0010";
    public const string MissingBlockTerminator   = "TH0011";
    public const string PluginHandlerFailed      = "TH0012";

    // -- .th specific -----------------------------------------------------
    public const string UnterminatedBlock        = "TH0020";
    public const string MismatchedBlockTerminator = "TH0021";
    public const string MalformedFix             = "TH0030";
    public const string MalformedEquate          = "TH0031";
    public const string MalformedData            = "TH0032";

    // -- data style / reading-order validation (LANG-05) ------------------
    public const string UnknownDataStyle         = "TH0033";
    public const string UnknownDataReading       = "TH0034";
    public const string DataRowArityMismatch     = "TH0035";
    public const string MissingFromTo            = "TH0036";

    // -- centreline metadata commands (LANG-04/03) ------------------------
    public const string MalformedUnits           = "TH0040";
    public const string MalformedCalibrate       = "TH0041";
    public const string MalformedDeclination     = "TH0042";
    public const string UnknownCoordinateSystem  = "TH0043";
    public const string UnknownUnit              = "TH0044";

    // -- XVI --------------------------------------------------------------
    public const string XviImageMissing          = "TH_XVI_001";
    public const string XviFileMissing           = "TH_XVI_002";
    public const string XviTransformDegenerate   = "TH_XVI_003";
    public const string XviMissingVersion        = "TH_XVI_010";
    public const string XviMalformedScale        = "TH_XVI_011";
    public const string XviMalformedTransform    = "TH_XVI_012";
    public const string XviMissingImage          = "TH_XVI_013";
    public const string XviUnknownKeyword        = "TH_XVI_014";

    // -- .th2 -------------------------------------------------------------
    public const string Th2MalformedPoint        = "TH2_001";
    public const string Th2MalformedLine         = "TH2_002";
    public const string Th2MalformedArea         = "TH2_003";
    public const string Th2UnknownPointType      = "TH2_004";
    public const string Th2UnknownLineType       = "TH2_005";
    public const string Th2UnknownAreaType       = "TH2_006";
    public const string Th2UnterminatedScrap     = "TH2_010";
}
