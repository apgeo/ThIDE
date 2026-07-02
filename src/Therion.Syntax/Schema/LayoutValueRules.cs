// C6 — layout option VALUE validation (spec §8; source: thlayout.h enum tables, B6-verified).
// The option KEY check (TH0062) lives in LayoutBodyParser; this adds per-key value checks for
// the closed enum tables + simple ranges. Gated by section "layout" + Enums/Ranges categories.

using System;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Value rules for <c>layout</c> body options (thlayout.h token tables).</summary>
internal static class LayoutValueRules
{
    private const string Section = "layout";

    // key → allowed FIRST-argument values (thlayout.h:126–478, thlayout.cxx:411, thlayoutclr.h:43,
    // thlocale.cxx:32, thsymbolset.cxx:132).
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> FirstArgEnums =
        BuildFirstArgEnums();

    private static ImmutableDictionary<string, ImmutableHashSet<string>> BuildFirstArgEnums()
    {
        var b = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var grid = Set("bottom", "off", "top");
        b["grid"] = grid;
        b["page-grid"] = Set("off", "on");                       // book: on/off
        b["grid-coords"] = Set("all", "border", "off");
        b["legend"] = Set("all", "off", "on");
        var colorLegend = Set("discrete", "off", "on", "smooth");
        b["color-legend"] = colorLegend;
        b["colour-legend"] = colorLegend;
        b["debug"] = Set("all", "first", "off", "on", "scrap-names", "second", "station-names");
        b["surface"] = Set("bottom", "off", "top");
        b["smooth-shading"] = Set("off", "quick");
        b["north"] = Set("grid", "true");
        var colorModel = Set("cmyk", "grayscale", "rgb");
        b["color-model"] = colorModel;
        b["colour-model"] = colorModel;
        b["units"] = Set("imperial", "metric");
        b["statistics"] = Set("carto", "carto-count", "copyright", "copyright-count",
            "explo", "explo-length", "topo", "topo-length");
        var colorItems = Set("labels", "map-bg", "map-fg", "preview-above", "preview-below");
        b["color"] = colorItems;
        b["colour"] = colorItems;
        var symbolClass = Set("point", "line", "area", "group", "special");
        b["symbol-hide"] = symbolClass;
        b["symbol-show"] = symbolClass;
        b["symbol-assign"] = symbolClass;
        b["symbol-color"] = symbolClass;
        b["symbol-colour"] = symbolClass;
        return b.ToImmutable();
    }

    // map-header <x> <y> <align> — the LAST argument is the align enum (thlayout.h:323).
    private static readonly ImmutableHashSet<string> MapHeaderAligns =
        Set("c", "center", "e", "n", "ne", "nw", "off", "s", "se", "sw", "w");

    /// <summary>Checks one decoded layout option's value(s).</summary>
    public static void Check(
        LayoutOption option,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserOptions? parserOptions)
    {
        var v = (parserOptions ?? ParserOptions.Default).EffectiveValidation;
        if (!v.Enabled || !v.IsSectionEnabled(Section)) return;
        var mode = (parserOptions ?? ParserOptions.Default).Mode;

        if (string.IsNullOrWhiteSpace(option.Value)) return;    // arity is not this pass's job
        var args = option.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (v.IsCategoryEnabled(ValidationCategories.Enums))
        {
            if (FirstArgEnums.TryGetValue(option.Key, out var allowed) && !allowed.Contains(args[0]))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                    $"'{args[0]}' is not a valid value for layout '{option.Key}' (allowed: {string.Join(", ", allowed)}).",
                    option.Span));
            }
            else if (Eq(option.Key, "map-header") && !MapHeaderAligns.Contains(args[^1]))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.ValueTypeMismatch, Lenient(mode),
                    $"'{args[^1]}' is not a valid map-header position (n/s/e/w/ne/nw/se/sw/center/off).",
                    option.Span));
            }
        }

        // opacity / surface-opacity: 0–100 (thbook ch05).
        if (v.IsCategoryEnabled(ValidationCategories.Ranges) &&
            (Eq(option.Key, "opacity") || Eq(option.Key, "surface-opacity")) &&
            double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) &&
            (pct < 0 || pct > 100))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.ValueOutOfRange, Lenient(mode),
                $"'{option.Key}' {args[0]} is outside the 0–100 range.",
                option.Span));
        }
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static ImmutableHashSet<string> Set(params string[] items) =>
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, items);

    private static DiagnosticSeverity Lenient(ParserMode mode) =>
        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
}
