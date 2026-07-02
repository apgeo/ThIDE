// C1 — typed-node validation rules for .th survey/centreline commands, encoding the
// verified grammar of spec §5.2/§5.3 (.claude/therion-syntax/syntax-spec.md; source:
// thdata.cxx set_data_* methods). Invoked from SchemaValidator's walk for typed AST
// nodes; every rule is gated by the section ("centreline") and a check category, so
// each can be disabled via SchemaValidationOptions (PERF.md §2).
//
// Severity convention: rules Therion rejects with thexception are Warning in lenient
// mode and Error in strict (consistent with the existing "lenient" diagnostic rows).

using System;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Schema rules for typed centreline command nodes (spec §5).</summary>
internal static class ThCentrelineRules
{
    private const string Section = "centreline";

    /// <summary>Applies the centreline rules to one typed command node.</summary>
    public static void Validate(
        TherionCommand cmd,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        // map is its own toggle section (spec §6.7); everything below is "centreline".
        if (cmd is MapCommand map)
        {
            if (options.IsSectionEnabled("map") &&
                options.IsCategoryEnabled(ValidationCategories.Enums) &&
                map.Projection is { Length: > 0 } proj &&
                proj[0] != '[')   // bracketed form `[elevation <angle> [deg]]` — content not modeled here
            {
                // `type[:index]` — the base must be a known projection (thdb2dprj.h:55).
                int colon = proj.IndexOf(':');
                var baseProj = colon < 0 ? proj : proj[..colon];
                if (!ThSchema.ProjectionTypes.Contains(baseProj))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                        $"'{proj}' is not a valid projection (plan, elevation, extended, none).",
                        map.Span));
            }
            return;
        }

        if (!options.IsSectionEnabled(Section)) return;

        switch (cmd)
        {
            case DataCommand data:
                ValidateDataOrder(data, options, diagnostics, mode);
                break;
            case StationFix fix:
                ValidateFix(fix, options, diagnostics, mode);
                break;
            case TeamCommand team:
                ValidateTeamRoles(team, options, diagnostics, mode);
                break;
            case InstrumentCommand instr:
                ValidateInstrument(instr, options, diagnostics, mode);
                break;
            case ExtendCommand ext:
                if (options.IsCategoryEnabled(ValidationCategories.Arity) && ext.Stations.Length > 3)
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.TooManyArguments, Lenient(mode),
                        $"'extend' takes at most 3 station arguments, found {ext.Stations.Length}.",
                        ext.Span));
                break;
            case DeclinationCommand decl:
                // src: a numeric declination REQUIRES angular units ("missing angular units").
                if (options.IsCategoryEnabled(ValidationCategories.ValueTypes) &&
                    decl.SingleValue is not null && decl.Unit is null && !decl.IsReset)
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.MalformedDeclination, Lenient(mode),
                        "A numeric declination requires angular units (e.g. 'declination 2.5 degrees').",
                        decl.Span));
                break;
            case VThresholdCommand vt:
                // src: 0–90° after unit transform; only check the degree/no-unit forms.
                if (options.IsCategoryEnabled(ValidationCategories.Ranges) &&
                    vt.Value is { } v && IsDegreesOrUnitless(vt.Unit) && (v < 0 || v > 90))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.ValueOutOfRange, Lenient(mode),
                        $"vthreshold {v.ToString(CultureInfo.InvariantCulture)} is outside the 0–90° range.",
                        vt.Span));
                break;
            case StationCommand st:
                ValidateStationFlags(st, options, diagnostics, mode);
                break;
            case DateCommand date:
                CheckDate(date.Value, "date", date.Span, options, diagnostics, mode);
                break;
            case ExploDateCommand exploDate:
                CheckDate(exploDate.Value, "explo-date", exploDate.Span, options, diagnostics, mode);
                break;
            case UnitsCommand units:
                CheckQuantityClasses(units.Quantities, unitsSet: true, "units", units.Span,
                    options, diagnostics, mode);
                break;
            case SdCommand sd:
                CheckQuantityClasses(sd.Quantities, unitsSet: false, "sd", sd.Span,
                    options, diagnostics, mode);
                break;
        }
    }

    private static void ValidateDataOrder(
        DataCommand data, SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        var style = DataStyles.ParseStyle(data.Style);
        if (style == DataStyle.Unknown) return; // TH0033 already reported

        foreach (var p in DataStyles.ValidateOrder(style, data.Fields))
        {
            var category = p.Kind is DataStyles.OrderProblemKind.Incomplete
                    or DataStyles.OrderProblemKind.TooManyReadings
                ? ValidationCategories.Arity
                : ValidationCategories.Enums;
            if (!options.IsCategoryEnabled(category)) continue;

            var code = p.Kind switch
            {
                DataStyles.OrderProblemKind.Duplicate => DiagnosticCodes.DuplicateReading,
                DataStyles.OrderProblemKind.InterleavedMix => DiagnosticCodes.InterleavedMix,
                DataStyles.OrderProblemKind.NewlinePosition => DiagnosticCodes.InvalidNewlinePosition,
                DataStyles.OrderProblemKind.Incomplete => DiagnosticCodes.IncompleteDataOrder,
                DataStyles.OrderProblemKind.TooManyReadings => DiagnosticCodes.TooManyArguments,
                _ => DiagnosticCodes.InvalidReadingForStyle,
            };
            diagnostics.Add(Diagnostic.Create(code, Lenient(mode), p.Message, data.Span));
        }
    }

    private static void ValidateFix(
        StationFix fix, SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        // fix <station> <x> <y> <z> [<sd>] | [<sdxy> <sdz>] | [<sdx> <sdy> <sdz>] — 4–7 args total
        // (src set_data_fix: switch(nargs) 4..7); std deviations must be positive numbers.
        var sds = fix.OptionsRaw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (options.IsCategoryEnabled(ValidationCategories.Arity) && sds.Length > 3)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.TooManyArguments, Lenient(mode),
                $"'fix' takes at most 7 arguments (station, x, y, z, up to 3 std deviations); found {4 + sds.Length}.",
                fix.Span));
            return;
        }
        foreach (var sd in sds)
        {
            if (!double.TryParse(sd, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                if (options.IsCategoryEnabled(ValidationCategories.ValueTypes))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                        $"'fix' standard deviation '{sd}' is not a number.", fix.Span));
            }
            else if (val <= 0 && options.IsCategoryEnabled(ValidationCategories.Ranges))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.ValueOutOfRange, Lenient(mode),
                    $"'fix' standard deviation must be positive, found {sd}.", fix.Span));
            }
        }
    }

    private static void ValidateTeamRoles(
        TeamCommand team, SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;
        foreach (var role in team.OptionsRaw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (CommandVocabulary.TeamRoles.Contains(role)) continue;
            var hint = string.Equals(role, "explorer", StringComparison.OrdinalIgnoreCase)
                ? "The thbook documents 'explorer', but the Therion compiler rejects it."
                : null;
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueTypeMismatch, DiagnosticSeverity.Warning,
                $"Unknown team role '{role}'.", team.Span, hint));
        }
    }

    private static void ValidateInstrument(
        InstrumentCommand instr, SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;
        foreach (var q in instr.Quantities)
        {
            if (!CommandVocabulary.InstrumentQuantities.Contains(q))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                    $"Invalid instrument quantity '{q}'.", instr.Span));
        }
    }

    private static void ValidateStationFlags(
        StationCommand st, SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;
        bool continuation = false;
        for (int i = 0; i < st.Flags.Length; i++)
        {
            var flag = st.Flags[i];
            bool negated = i > 0 && string.Equals(st.Flags[i - 1], "not", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(flag, "continuation", StringComparison.OrdinalIgnoreCase) && !negated)
                continuation = true;

            // src: "you can not set fixed station flag directly - fix command needs to be used".
            if (string.Equals(flag, "fixed", StringComparison.OrdinalIgnoreCase) && !negated)
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.InvalidStationFlag, Lenient(mode),
                    "The 'fixed' station flag cannot be set directly — use the 'fix' command ('not fixed' is allowed).",
                    st.Span));

            // src: "missing continuation flag before explored length".
            if (string.Equals(flag, "explored", StringComparison.OrdinalIgnoreCase) && !continuation)
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.InvalidStationFlag, Lenient(mode),
                    "'explored' requires the 'continuation' flag to be set first.",
                    st.Span));
        }
    }

    private static void CheckDate(
        string value, string command, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.ValueTypes)) return;
        if (TherionDates.Check(value) is { } error)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                $"Invalid '{command}' value: {error} (expected YYYY[.MM[.DD[@HH[:MM[:SS]]]]] or an interval).",
                span));
    }

    private static void CheckQuantityClasses(
        ImmutableArray<string> quantities, bool unitsSet, string command, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;
        if (CommandVocabulary.FindIncompatibleQuantity(quantities, unitsSet) is { } bad)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                $"Incompatible quantity '{bad}' in '{command}': length-class and angle-class quantities cannot be mixed.",
                span));
    }

    private static bool IsDegreesOrUnitless(string? unit) =>
        unit is null || unit.Equals("deg", StringComparison.OrdinalIgnoreCase)
                     || unit.Equals("degree", StringComparison.OrdinalIgnoreCase)
                     || unit.Equals("degrees", StringComparison.OrdinalIgnoreCase);

    private static DiagnosticSeverity Lenient(ParserMode mode) =>
        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
}
