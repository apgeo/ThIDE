// Diagnostic codes emitted by the parsing layer. Catalog: docs/diagnostics.md.
// Codes are culture-invariant strings; messages are localized in Therion.Core.Resources.

namespace Therion.Syntax;

/// <summary>Well-known diagnostic codes emitted by the parsing layer.</summary>
public static class DiagnosticCodes
{
    // -- Lexer / parser core ----------------------------------------------
    public const string UnexpectedToken          = "TH0002";
    public const string UnknownCommand           = "TH0010";
    public const string PluginHandlerFailed      = "TH0012";

    // -- .th block structure ----------------------------------------------
    public const string UnterminatedBlock        = "TH0020";
    public const string MismatchedBlockTerminator = "TH0021";
    public const string MalformedFix             = "TH0030";
    public const string MalformedEquate          = "TH0031";
    public const string MalformedData            = "TH0032";

    // -- data style / reading-order validation ------------------
    public const string UnknownDataStyle         = "TH0033";
    public const string UnknownDataReading       = "TH0034";
    public const string MalformedDataRow         = "TH0037";  // centreline shot line with <2 columns
    public const string MissingFromTo            = "TH0036";  // no from/to/station to bind shots

    // -- centreline metadata commands ------------------------
    public const string MalformedSd              = "TH0038";  // sd needs <quantity> <value> <unit>
    public const string MalformedMeasurement     = "TH0039";  // grid-angle/vthreshold need a numeric value
    public const string MalformedUnits           = "TH0040";
    public const string MalformedCalibrate       = "TH0041";
    public const string MalformedDeclination     = "TH0042";  // declination needs a numeric value / list / '-'
    public const string UnknownCoordinateSystem  = "TH0043";
    public const string InvalidInferSpec         = "TH0056";  // infer <plumbs|equates> <on|off>

    // -- identifiers / block matching -------------------------------------
    public const string IllegalIdentifier        = "TH0050";  // char outside keyword/ext-keyword
    public const string BlockIdMismatch          = "TH0051";  // endsurvey/endscrap id != opener id

    // -- centreline argument enums (flags / mark / extend / station) ------
    public const string InvalidFlag              = "TH0052";  // flags <shot flag>
    public const string InvalidMarkType          = "TH0053";  // mark <type>
    public const string InvalidExtendSpec        = "TH0054";  // extend <spec>
    public const string InvalidStationFlag       = "TH0055";  // station … <flags>

    // -- .thconfig command arguments --------------------------------------
    public const string UnknownExportType        = "TH0060";  // export <type>
    public const string UnknownExportFormat      = "TH0061";  // export … -fmt invalid for <type>
    public const string UnknownLayoutOption      = "TH0062";  // layout body option key

    // -- schema-driven validation (SchemaValidator, syntax-coverage effort) --
    public const string MissingRequiredArgument  = "TH0063";  // fewer args than schema MinArgs
    public const string TooManyArguments         = "TH0064";  // more args than schema MaxArgs
    public const string ValueTypeMismatch        = "TH0065";  // arg/option value fails its ValueSpec
    public const string OptionNotValidInContext  = "TH0066";  // option not valid for command/type
    public const string KeywordCaseMismatch      = "TH0067";  // right keyword, wrong case
    public const string InvalidSpecialValue      = "TH0068";  // not a number nor -,.,NaN,Inf,up,down
    public const string ValueOutOfRange          = "TH0069";  // numeric value outside schema range

    // -- data <style> <readings> order validation (spec §5.3, thdata.cxx set_data_data) --
    public const string InvalidReadingForStyle   = "TH0070";  // reading not valid for the data style
    public const string DuplicateReading         = "TH0071";  // reading listed twice
    public const string IncompleteDataOrder      = "TH0072";  // "not all data for given style"
    public const string InvalidNewlinePosition   = "TH0073";  // newline first/last in the order
    public const string InterleavedMix           = "TH0074";  // station mixed with from/to

    // -- XVI (Therion `set XVI*` Tcl export format) -----------------------
    public const string XviUnknownVariable       = "TH_XVI_001";  // unknown `set XVI…` variable
    public const string XviUnexpectedStatement   = "TH_XVI_002";  // non-`set` top-level content
    public const string XviUnterminatedBlock     = "TH_XVI_003";  // `{` without matching `}`
    public const string XviMalformedGrid         = "TH_XVI_004";  // XVIgrid not 8 numeric values

    // -- .th2 drawing format ----------------------------------------------
    public const string Th2MalformedPoint        = "TH2_001";
    public const string Th2MalformedLine         = "TH2_002";
    public const string Th2MalformedArea         = "TH2_003";
    public const string Th2UnknownPointType      = "TH2_004";
    public const string Th2UnknownLineType       = "TH2_005";
    public const string Th2UnknownAreaType       = "TH2_006";
    public const string Th2UnknownSubtype        = "TH2_008";  // (reserved) point/line/area subtype
    public const string Th2UnknownOption         = "TH2_009";  // unknown -option on point/line/area
    public const string Th2UnterminatedScrap     = "TH2_010";
}
