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

    // ---- project-level analysis diagnostics (DIAG-02..06) ----
    /// <summary>A naming collision: the same survey/map name is declared in more than one file (DIAG-05).</summary>
    public const string DuplicateDeclaration = "TH_SEM_010";
    /// <summary>A closed loop in the centreline has a misclosure beyond tolerance (DIAG-02).</summary>
    public const string LoopMisclosure       = "TH_SEM_011";
    /// <summary>A shot looks like a blunder/outlier (zero/over-long leg, self-loop, …) (DIAG-03).</summary>
    public const string ShotOutlier          = "TH_SEM_012";
    /// <summary>Foresight and backsight readings disagree beyond tolerance (DIAG-04).</summary>
    public const string ForeBackMismatch     = "TH_SEM_013";
    /// <summary>A dangling reference: an <c>input</c>/<c>source</c> target that can't be resolved (DIAG-06).</summary>
    public const string DanglingReference    = "TH_SEM_014";

    // XVI semantic code: a `-sketch` target referenced from a .th2 scrap doesn't exist on disk.
    // (Syntax-layer XVI codes live in Therion.Syntax.DiagnosticCodes as TH_XVI_001..004.)
    public const string XviFileMissing          = "TH_XVI_050";
}
