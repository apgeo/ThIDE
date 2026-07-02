// C2 — typed-node validation rules for .th2 point objects, encoding spec §6.2/§6.3
// (.claude/therion-syntax/syntax-spec.md; source: thpoint.cxx parse_* / set switch,
// th2ddataobject.cxx). Runs in the SchemaValidator walk; section "point"; every check
// category-toggleable. Rules apply only to KNOWN built-in types — user-defined types
// (`u:`) and unknown types (already TH2_004-warned) are skipped, since user symbols
// may accept anything.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Schema rules for .th2 <c>point</c> objects (spec §6.3).</summary>
internal static class Th2PointRules
{
    private const string Section = "point";

    // Subtype matrix (thpoint.cxx parse_subtype): subtype on any other type is an error.
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> SubtypesByType =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase,
            new[]
            {
                KV("station", "temporary", "painted", "natural", "fixed"),
                KV("air-draught", "winter", "summer", "undefined"),
                KV("water-flow", "permanent", "intermittent", "paleo"),
            });

    private static readonly ImmutableHashSet<string> TextTypes =
        Set("label", "remark", "station-name", "continuation");
    private static readonly ImmutableHashSet<string> NameTypes =
        Set("station", "continuation");
    private static readonly ImmutableHashSet<string> ValueTypes =
        Set("altitude", "height", "passage-height", "dimensions", "date", "extra");
    // -clip rejection list (thpoint.cxx set, TT_2DOBJ_CLIP case). ⚠ src≠book: src allows
    // station and rejects dimensions/map-connection; the book says the opposite.
    private static readonly ImmutableHashSet<string> NoClipTypes =
        Set("station-name", "label", "remark", "date", "altitude", "height",
            "dimensions", "map-connection", "passage-height");

    public static void Validate(
        PointObject point,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        if (!options.IsSectionEnabled(Section)) return;

        var baseType = point.BaseType;
        if (Th2Symbols.IsUserType(baseType)) return;               // u:<anything> — no rules
        bool knownType = Th2Symbols.IsKnownPointType(point.PointType);

        // ---- subtype matrix (TH2_008) --------------------------------------------------
        if (knownType && point.Subtype is { } subtype &&
            options.IsCategoryEnabled(ValidationCategories.Enums))
        {
            if (!SubtypesByType.TryGetValue(baseType, out var allowed))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.Th2UnknownSubtype, Lenient(mode),
                    $"Point type '{baseType}' takes no subtype (only station, air-draught, water-flow and u: do).",
                    point.Span));
            else if (!allowed.Contains(subtype))
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.Th2UnknownSubtype, Lenient(mode),
                    $"Invalid subtype '{subtype}' for point type '{baseType}' (allowed: {string.Join(", ", allowed)}).",
                    point.Span));
        }

        // ---- per-type option validity (TH0066) ------------------------------------------
        if (knownType && options.IsCategoryEnabled(ValidationCategories.Options))
        {
            bool isStation = Eq(baseType, "station");

            if (isStation)
            {
                NotValid(point, "orientation", diagnostics, mode, "-orientation is not valid with type station");
                NotValid(point, "orient", diagnostics, mode, "-orientation is not valid with type station");
                NotValid(point, "align", diagnostics, mode, "-align is not valid with type station");
            }

            // Dead options: thpoint.cxx throws unconditionally for these in Therion 6.4.
            foreach (var dead in DeadSizeOptions)
                NotValid(point, dead, diagnostics, mode,
                    $"-{dead} is not valid on points (rejected unconditionally by Therion 6.4)");

            if (!NameTypes.Contains(baseType))
            {
                NotValid(point, "name", diagnostics, mode,
                    $"a station reference (-name) is only valid on station/continuation points, not '{baseType}'");
                NotValid(point, "station", diagnostics, mode,
                    $"a station reference (-station) is only valid on station/continuation points, not '{baseType}'");
            }
            if (!Eq(baseType, "section"))
                NotValid(point, "scrap", diagnostics, mode,
                    $"-scrap is only valid on section points, not '{baseType}'");
            if (!TextTypes.Contains(baseType))
                NotValid(point, "text", diagnostics, mode,
                    $"-text is only valid on label/remark/station-name/continuation points, not '{baseType}'");
            if (!Eq(baseType, "continuation"))
                NotValid(point, "explored", diagnostics, mode,
                    $"-explored is only valid on continuation points, not '{baseType}'");
            if (!ValueTypes.Contains(baseType))
                NotValid(point, "value", diagnostics, mode,
                    $"-value is only valid on altitude/height/passage-height/dimensions/date points, not '{baseType}'");
            if (!Eq(baseType, "extra"))
                NotValid(point, "dist", diagnostics, mode,
                    $"-dist is only valid on extra points, not '{baseType}'");
            if (NoClipTypes.Contains(baseType))
                NotValid(point, "clip", diagnostics, mode,
                    $"-clip is not valid with type '{baseType}'");
        }

        // ---- orientation range [0, 360) (TH0069) ----------------------------------------
        if (options.IsCategoryEnabled(ValidationCategories.Ranges) &&
            point.Options.Orientation is { } o && (o < 0 || o >= 360))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueOutOfRange, Lenient(mode),
                $"-orientation {o} is out of range (0 ≤ value < 360).",
                SpanOf(point, "orientation") ?? SpanOf(point, "orient") ?? point.Span));
        }
    }

    private static readonly string[] DeadSizeOptions = { "x-size", "y-size", "size" };

    private static void NotValid(
        PointObject point, string option,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode, string message)
    {
        if (SpanOf(point, option) is { } span)
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.OptionNotValidInContext, Lenient(mode), message + ".", span));
    }

    /// <summary>The span of the named option, when present.</summary>
    private static SourceSpan? SpanOf(PointObject point, string option)
    {
        foreach (var o in point.Options.Options)
            if (string.Equals(o.Name, option, StringComparison.OrdinalIgnoreCase))
                return o.Span;
        return null;
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static ImmutableHashSet<string> Set(params string[] items) =>
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, items);

    private static System.Collections.Generic.KeyValuePair<string, ImmutableHashSet<string>> KV(
        string key, params string[] values) => new(key, Set(values));

    private static DiagnosticSeverity Lenient(ParserMode mode) =>
        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
}
