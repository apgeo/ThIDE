// "explain this error". Maps a diagnostic code to a short plain-language explanation,
// an example fix, and a thbook term to open the relevant manual page. Keyed by the stable code
// strings from Therion.Syntax.DiagnosticCodes / Therion.Semantics.SemanticDiagnosticCodes.
//
// Lives here, not in the app, because `explain_diagnostic` must answer with no UI loaded
// (.claude/mcp-integration/DECISIONS.md D-009). The table is pure data: it holds no UI types and
// no state, and coverage of the code catalogs is deliberately partial — an unexplained code is a
// clean miss, not an error.

using System.Collections.Generic;

namespace Therion.Semantics;

/// <summary>Explanation + example fix + thbook term for a diagnostic code.</summary>
public sealed record DiagnosticExplanation(string Summary, string? Example = null, string? DocTerm = null);

public static class DiagnosticExplanations
{
    private static readonly Dictionary<string, DiagnosticExplanation> Map = new()
    {
        // Parser core
        ["TH0001"] = new("The lexer could not tokenize this position — usually a stray or unbalanced quote/brace.", null, null),
        ["TH0002"] = new("An unexpected token was found where it isn't allowed in this command.", null, null),
        ["TH0003"] = new("The file ends in the middle of a block — a closing keyword (e.g. endsurvey) is missing.", null, null),
        ["TH0010"] = new("This top-level keyword isn't a recognized Therion command. Check spelling or context.", null, null),
        ["TH0011"] = new("A block is missing its terminator.", "survey foo\n  …\nendsurvey", "survey"),
        ["TH0032"] = new("A 'data' command is malformed — it needs a style and a reading order.", "data normal from to length compass clino", "data"),
        ["TH0033"] = new("Unknown data style. Use one of: normal, diving, cartesian, cylpolar, dimensions, nosurvey, topofil.", "data normal from to length compass clino", "data"),
        ["TH0034"] = new("Unknown data reading keyword in the reading order.", "data normal from to length compass clino", "data"),
        ["TH0040"] = new("A 'units' command is malformed (unknown quantity or missing unit).", "units length metres", "units"),
        ["TH0041"] = new("A 'calibrate' command is missing its zero-error value.", "calibrate compass 0.5", "calibrate"),
        ["TH0043"] = new("Unknown coordinate system. Use an EPSG code or a known cs name.", "cs EPSG:32633", "cs"),

        // Semantics
        ["TH_SEM_001"] = new("This station/reference can't be resolved to any declaration. Check the name and survey path.", null, "equate"),
        ["TH_SEM_002"] = new("This station is fixed more than once — keep a single 'fix' per station.", null, "fix"),
        ["TH_SEM_003"] = new("The reference is malformed (bad @-qualified name).", null, null),
        ["TH_SEM_004"] = new("A fixed station is never used by any shot or equate — likely a typo or leftover.", null, "fix"),
        ["TH_SEM_005"] = new("The data row's value count doesn't match the active reading order.", "data normal from to length compass clino\n0 1 12.5 0 -5", "data"),
        ["TH_SEM_006"] = new("A value isn't a valid number for its reading (e.g. a typo in a length/compass/clino).", null, "data"),
        ["TH_SEM_007"] = new("A value is outside the expected range for its reading (e.g. compass > 360°).", null, "data"),
        ["TH_SEM_010"] = new("The same survey/map name is declared in more than one file. Names must be unique across the project (station names, however, are unique only per survey).", null, "survey"),
        ["TH_SEM_011"] = new("A closed loop doesn't close: the misclosure is large relative to the loop length. Look for a transposed digit or a missed backsight in the loop.", null, "centerline"),
        ["TH_SEM_012"] = new("This leg looks implausible — zero length between distinct stations, a self-loop, or an unusually long shot.", null, "data"),
        ["TH_SEM_013"] = new("The foresight and backsight disagree by more than the tolerance. Compass back ≈ fore ± 180°, clino back ≈ −fore.", "data normal from to length compass backcompass clino backclino", "data"),
        ["TH_SEM_014"] = new("An included file (input/source target) was not found on disk.", "input ../survey/part.th", "input"),
        ["TH_SEM_015"] = new("This piece of survey is disconnected from the rest of the cave and isn't georeferenced. Join it to a neighbouring station with an 'equate', or anchor it in absolute coordinates with a 'fix' under a 'cs'.", "equate 0@thispart 14@mainpassage", "equate"),

        // XVI
        ["TH_XVI_001"] = new("The image file referenced by this .xvi was not found.", null, "xvi"),
        ["TH_XVI_002"] = new("The referenced .xvi file was not found.", null, "xvi"),
        ["TH_XVI_003"] = new("The .xvi affine transform is degenerate (non-invertible).", null, "xvi"),

        // Workspace
        ["TH_WS_001"] = new("A referenced path was not found.", null, null),
        ["TH_WS_002"] = new("No Therion configuration (thconfig) file was found in this folder.", null, "thconfig"),
    };

    public static DiagnosticExplanation? For(string? code) =>
        !string.IsNullOrEmpty(code) && Map.TryGetValue(code, out var e) ? e : null;
}
