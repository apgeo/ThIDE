// LANG-02 — layout option keyword registry. thbook v6.4.0 §"layout" pp.53-60.
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
    /// <summary>The recognized layout option keys (for completion + light validation).</summary>
    public static readonly ImmutableHashSet<string> All =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "scale", "base-scale", "size", "origin", "origin-label", "overlap", "rotate",
            "grid", "grid-size", "grid-origin", "grid-coords", "page-grid",
            "legend", "legend-columns", "legend-width", "colour-legend",
            "symbol-set", "symbol-hide", "symbol-show", "symbol-assign", "symbol-colour",
            "color", "colour", "color-model", "colour-model", "opacity", "transparency",
            "surface", "surface-opacity", "sketches", "layers",
            "map-comment", "map-header", "map-header-bg", "map-image",
            "min-symbol-scale", "nav-factor", "nav-size", "fonts-setup",
            "page-setup", "page-numbers", "own-pages", "exclude-pages", "title-pages",
            "copy", "cs", "north", "code", "endcode",
            "doc-author", "doc-title", "doc-subject", "doc-keywords",
            "debug", "language", "lang", "statistics", "smooth-shading", "scale-bar",
            "grid-size", "units");

    /// <summary>True if <paramref name="key"/> is a recognized layout option key.</summary>
    public static bool IsKnown(string key) => All.Contains(key);

    /// <summary>The keys (for editor completion inside a layout block).</summary>
    public static IReadOnlyCollection<string> Keys => All;
}
