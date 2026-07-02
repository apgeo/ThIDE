// C3 — typed-node validation rules for .th2 line and area objects, encoding spec
// §6.4/§6.5 (source: thline.cxx parse_* / set switch, tharea). Runs in the
// SchemaValidator walk; sections "line" / "area"; category-toggleable. Rules apply
// only to KNOWN built-in types — `u:` and unknown types are skipped.

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Schema rules for .th2 <c>line</c> / <c>area</c> objects (spec §6.4–6.5).</summary>
internal static class Th2LineRules
{
    // Subtype matrix (thline.cxx parse_subtype): subtype on any other type is
    // "invalid line type - subtype combination".
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> SubtypesByType =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase,
            new[]
            {
                KV("wall", "invisible", "bedrock", "sand", "clay", "pebbles", "debris", "blocks",
                    "ice", "underlying", "unsurveyed", "presumed", "overlying", "pit", "moonmilk",
                    "flowstone"),
                KV("border", "invisible", "temporary", "visible", "presumed"),
                KV("survey", "cave", "surface"),
                KV("water-flow", "permanent", "intermittent", "conjectural"),
            });

    private static readonly ImmutableHashSet<string> GradientValues =
        Set("none", "center", "point");
    private static readonly ImmutableHashSet<string> DirectionValues =
        Set("none", "begin", "end", "both", "point");
    private static readonly ImmutableHashSet<string> OutlineValues =
        Set("in", "out", "none");

    public static void ValidateLine(
        LineObject line,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        if (!options.IsSectionEnabled("line")) return;

        var baseType = line.BaseType;
        if (Th2Symbols.IsUserType(baseType)) return;
        if (!Th2Symbols.IsKnownLineType(line.LineType)) return;   // TH2_005 already reported

        bool isPit = Eq(baseType, "pit") || Eq(baseType, "pitch");
        // wall:pit may be declared on the header OR switched mid-line by a body `subtype pit`
        // (Therion applies options sequentially, so either position legalizes -height).
        bool isWallPit = Eq(baseType, "wall") &&
            (Eq(line.Subtype ?? "", "pit") || AnyVertexSubtype(line, "pit"));

        // ---- subtype matrix (TH2_008) --------------------------------------------------
        if (options.IsCategoryEnabled(ValidationCategories.Enums))
        {
            ValidateSubtype(baseType, line.Subtype, line.Span, diagnostics, mode);
            foreach (var v in line.Vertices)
                ValidateSubtype(baseType, v.Options.Subtype, v.Span, diagnostics, mode);
        }

        // ---- per-type option validity (TH0066) + enum values ------------------------------
        if (options.IsCategoryEnabled(ValidationCategories.Options))
        {
            // The same option may appear on the line header or any in-body point line.
            foreach (var (opt, span) in AllOptions(line))
            {
                switch (opt.Name.ToLowerInvariant())
                {
                    case "orientation" or "orient" when !Eq(baseType, "slope"):
                        Flag(diagnostics, mode, span, $"-orientation is only valid on slope lines, not '{baseType}'.");
                        break;
                    case "anchors" or "rebelays" when !Eq(baseType, "rope"):
                        Flag(diagnostics, mode, span, $"-{opt.Name} is only valid on rope lines, not '{baseType}'.");
                        break;
                    case "border" when !Eq(baseType, "slope"):
                        Flag(diagnostics, mode, span, $"-border is only valid on slope lines, not '{baseType}'.");
                        break;
                    case "gradient" when !Eq(baseType, "contour"):
                        Flag(diagnostics, mode, span, $"-gradient is only valid on contour lines, not '{baseType}'.");
                        break;
                    case "gradient":
                        EnumValue(diagnostics, mode, span, opt, GradientValues);
                        break;
                    case "direction" when !Eq(baseType, "section"):
                        Flag(diagnostics, mode, span, $"-direction is only valid on section lines, not '{baseType}'.");
                        break;
                    case "direction":
                        EnumValue(diagnostics, mode, span, opt, DirectionValues);
                        break;
                    case "head" when !Eq(baseType, "arrow"):
                        Flag(diagnostics, mode, span, $"-head is only valid on arrow lines, not '{baseType}'.");
                        break;
                    case "size" or "l-size" when !Eq(baseType, "slope"):
                        Flag(diagnostics, mode, span, $"-{opt.Name} is only valid on slope lines, not '{baseType}'.");
                        break;
                    case "r-size":
                        Flag(diagnostics, mode, span, "-r-size is not valid on lines (rejected unconditionally by Therion 6.4).");
                        break;
                    case "altitude" when !Eq(baseType, "wall"):
                        Flag(diagnostics, mode, span, $"-altitude is only valid on wall lines, not '{baseType}'.");
                        break;
                    case "text" when !Eq(baseType, "label"):
                        Flag(diagnostics, mode, span, $"-text is only valid on label lines, not '{baseType}'.");
                        break;
                    case "height" when !(isPit || isWallPit):
                        Flag(diagnostics, mode, span, $"-height is only valid on pit lines (or wall:pit), not '{baseType}'.");
                        break;
                    case "outline":
                        EnumValue(diagnostics, mode, span, opt, OutlineValues);
                        break;
                }
            }
        }
    }

    public static void ValidateArea(
        AreaObject area,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        if (!options.IsSectionEnabled("area")) return;
        if (!options.IsCategoryEnabled(ValidationCategories.Options)) return;

        // Areas take only the inherited 2dobj options, and -scale is explicitly rejected
        // ("scale specification for area not allowed" — th2ddataobject.cxx).
        foreach (var o in area.Options.Options)
            if (string.Equals(o.Name, "scale", StringComparison.OrdinalIgnoreCase))
                Flag(diagnostics, mode, o.Span, "-scale is not allowed on area objects.");
    }

    private static void ValidateSubtype(
        string baseType, string? subtype, SourceSpan span,
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode)
    {
        if (subtype is null) return;
        if (!SubtypesByType.TryGetValue(baseType, out var allowed))
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.Th2UnknownSubtype, Lenient(mode),
                $"Line type '{baseType}' takes no subtype (only wall, border, survey, water-flow and u: do).",
                span));
        else if (!allowed.Contains(subtype))
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.Th2UnknownSubtype, Lenient(mode),
                $"Invalid subtype '{subtype}' for line type '{baseType}' (allowed: {string.Join(", ", allowed)}).",
                span));
    }

    private static System.Collections.Generic.IEnumerable<(Th2Option Option, SourceSpan Span)> AllOptions(LineObject line)
    {
        foreach (var o in line.Options.Options) yield return (o, o.Span);
        foreach (var v in line.Vertices)
            foreach (var o in v.Options.Options) yield return (o, o.Span);
    }

    private static bool AnyVertexSubtype(LineObject line, string subtype)
    {
        foreach (var v in line.Vertices)
            if (Eq(v.Options.Subtype ?? "", subtype)) return true;
        return false;
    }

    private static void EnumValue(
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode, SourceSpan span,
        Th2Option opt, ImmutableHashSet<string> allowed)
    {
        if (!string.IsNullOrEmpty(opt.Value) && !allowed.Contains(opt.Value))
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                $"'{opt.Value}' is not a valid -{opt.Name} value (allowed: {string.Join(", ", allowed)}).",
                span));
    }

    private static void Flag(
        ImmutableArray<Diagnostic>.Builder diagnostics, ParserMode mode, SourceSpan span, string message) =>
        diagnostics.Add(Diagnostic.Create(
            DiagnosticCodes.OptionNotValidInContext, Lenient(mode), message, span));

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static ImmutableHashSet<string> Set(params string[] items) =>
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, items);

    private static System.Collections.Generic.KeyValuePair<string, ImmutableHashSet<string>> KV(
        string key, params string[] values) => new(key, Set(values));

    private static DiagnosticSeverity Lenient(ParserMode mode) =>
        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
}
