// layout option keyword registry. thbook v6.4.0 §"layout" pp.53-60.
// Therion source-of-truth: therion/src/thlayout.cxx.
//
// Used for editor completion inside a `layout … endlayout` body and to recognize the
// structural sub-keys (copy / cs / code) the typed LayoutCommand decodes specially.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Therion.Syntax;

/// <summary>Known <c>layout</c> body option keywords.</summary>
public static class LayoutKeywords
{
    /// <summary>
    /// The recognized layout option keys — exactly thlayout.h:478 `thtt_layout_opt[]`
    /// (B6-verified; the previous list missed survey-level/geospatial/color-profile and had
    /// phantom gradient/lang) + the structural `cs` sub-key our LayoutCommand decodes.
    /// </summary>
    public static readonly ImmutableHashSet<string> All =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "base-scale", "code", "endcode",
            "color", "colour", "color-legend", "colour-legend",
            "color-model", "colour-model", "color-profile", "colour-profile",
            "copy", "debug",
            "doc-author", "doc-keywords", "doc-subject", "doc-title",
            "exclude-pages", "fonts-setup", "geospatial",
            "grid", "grid-coords", "grid-origin", "grid-size",
            "language", "layers",
            "legend", "legend-columns", "legend-width",
            "map-comment", "map-header", "map-header-bg", "map-image",
            "min-symbol-scale", "nav-factor", "nav-size", "north",
            "opacity", "origin", "origin-label", "overlap", "own-pages",
            "page-grid", "page-numbers", "page-setup",
            "rotate", "scale", "scale-bar", "size", "sketches", "smooth-shading",
            "statistics", "surface", "surface-opacity", "survey-level",
            "symbol-assign", "symbol-color", "symbol-colour", "symbol-hide",
            "symbol-set", "symbol-show",
            "title-pages", "transparency", "units",
            // structural sub-key decoded by LayoutCommand (cs inside layout, book ch03 §cs)
            "cs");

    // FUTURE (deep per-option value validation — deliberately out of scope, see
    // docs/layout-and-embedded-code.md): today an option's *key* is validated (IsKnown) but its
    // *arguments* are not. A future pass could validate each option's value shape, e.g.:
    //   • scale          → one or two positive numbers (e.g. `1 500`)
    //   • base-scale     → one or two positive numbers
    //   • legend         → on | off
    //   • color-model    → cmyk | rgb | grey | hsv
    //   • symbol-hide/-show → <point|line|area|group|special|all> <symbol>  (see SymbolSets.Kinds)
    //   • symbol-set     → a known standard (see SymbolSets.Standards)
    //   • north          → true | grid
    //   • units          → a unit keyword + factor
    //   • grid / page-grid → on | off | bottom | top | …
    //   • size/origin/grid-size/grid-origin → numbers (+ optional unit)
    // This was intentionally deferred to keep the surface small and avoid brittleness across
    // Therion versions; the option-key registry + UnknownLayoutOption diagnostic is the current
    // validation level (LayoutBodyParser).

    /// <summary>True if <paramref name="key"/> is a recognized layout option key.</summary>
    public static bool IsKnown(string key) => All.Contains(key);

    /// <summary>The keys (for editor completion inside a layout block).</summary>
    public static IReadOnlyCollection<string> Keys => All;
}
