// .th2 point/line/area symbol-type registry. thbook v6.4.0 §"point"/"line"/"area".
// Therion source-of-truth: therion/thsymbolsets.cxx + the *.cxx type tables.
//
// Used to (a) validate object types (lenient — unknown built-in types warn) and (b) drive editor
// completion. A type may carry an inline subtype after ':' (e.g. wall:blocks, station:fixed) and
// the user-defined type "u"/"u:foo" is always accepted.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Therion.Syntax;

/// <summary>Known Therion symbol types for <c>point</c>, <c>line</c> and <c>area</c> objects.</summary>
public static class Th2Symbols
{
    /// <summary>The user-defined-type sentinel (with optional <c>u:name</c> subtype form).</summary>
    public const string UserType = "u";

    public static readonly ImmutableHashSet<string> PointTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            // special / labels
            "dimensions", "section", "station", "altitude", "date", "height", "label",
            "passage-height", "remark", "station-name",
            // symbolic passage fills
            "bedrock", "blocks", "clay", "debris", "guano", "ice", "mudcrack", "mud",
            "pebbles", "raft", "sand", "snow", "water",
            // speleothems
            "anastomosis", "aragonite", "cave-pearl", "clay-tree", "crystal", "curtains", "curtain",
            "disc-pillar", "disc-stalactite", "disc-stalagmite", "disc-pillars", "disc-stalactites",
            "disc-stalagmites", "disk", "flowstone", "flute", "gypsum-flower", "gypsum",
            "helictites", "helictite", "karren", "moonmilk", "pendant", "pillar-with-curtains",
            "pillars-with-curtains", "pillar", "pillars", "popcorn", "raft-cone", "rimstone-dam", "rimstone-pool",
            "scallop", "soda-straw", "stalactite-stalagmite", "stalactites-stalagmites", "stalactite",
            "stalactites", "stalagmite", "stalagmites", "volcano", "wall-calcite",
            // equipment
            "anchor", "bridge", "camp", "fixed-ladder", "gate", "handrail", "masonry", "nameplate",
            "no-equipment", "no-wheelchair", "rope-ladder", "rope", "steps", "traverse",
            "via-ferrata", "walkway", "wheelchair",
            // passage ends
            "breakdown-choke", "clay-choke", "continuation", "entrance", "flowstone-choke",
            "low-end", "narrow-end",
            // others
            "air-draught", "altar", "archeo-excavation", "archeo-material", "audio", "bat", "bones",
            "borehole", "danger", "dig", "electric-light", "ex-voto", "extra", "gradient",
            "human-bones", "ice-pillar", "ice-stalactite", "ice-stalagmite", "map-connection",
            "paleo-material", "photo", "root", "seed-germination", "sink", "spring", "tree-trunk",
            "vegetable-debris", "water-drip", "water-flow");

    // Exactly thline.h:212 thtt_line_types (B3-verified; `pitch` ≡ pit alias). The previous list
    // missed 11 real types — the corpus's TH2_005 warnings were our gap, not user symbols.
    public static readonly ImmutableHashSet<string> LineTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "abyss-entrance", "arrow", "border", "ceiling-meander", "ceiling-step", "chimney",
            "contour", "dripline", "fault", "fixed-ladder", "floor-meander", "floor-step",
            "flowstone", "gradient", "handrail", "joint", "label", "low-ceiling", "map-connection",
            "moonmilk", "overhang", "pit", "pit-chimney", "pitch", "rimstone-dam", "rimstone-pool",
            "rock-border", "rock-edge", "rope", "rope-ladder", "section", "slope", "steps",
            "survey", "via-ferrata", "walkway", "wall", "water-flow",
            // kept for corpus tolerance (not in the 6.4 table; older/aliased usage)
            "water", "wall-calcite");

    // Exactly tharea.h:91 thtt_area_types (B3-verified). `pillars`/`pillars-with-curtains` are
    // point-only types and were removed; `dimensions` was missing.
    public static readonly ImmutableHashSet<string> AreaTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "bedrock", "blocks", "clay", "debris", "dimensions", "flowstone", "ice", "moonmilk",
            "mudcrack", "pebbles", "pillar", "pillar-with-curtains", "sand", "snow", "stalactite",
            "stalactite-stalagmite", "stalagmite", "sump", "water");

    /// <summary>Splits a type into its base type and inline subtype (<c>wall:blocks</c> → wall / blocks).</summary>
    public static (string Base, string? Subtype) SplitType(string type)
    {
        int colon = type.IndexOf(':');
        return colon < 0 ? (type, null) : (type[..colon], type[(colon + 1)..]);
    }

    /// <summary>True if a base type is the user-defined sentinel (always allowed).</summary>
    public static bool IsUserType(string baseType) =>
        string.Equals(baseType, UserType, StringComparison.OrdinalIgnoreCase);

    public static bool IsKnownPointType(string type) => IsKnown(type, PointTypes);
    public static bool IsKnownLineType(string type) => IsKnown(type, LineTypes);
    public static bool IsKnownAreaType(string type) => IsKnown(type, AreaTypes);

    private static bool IsKnown(string type, ImmutableHashSet<string> set)
    {
        if (string.IsNullOrEmpty(type)) return false;
        var (baseType, _) = SplitType(type);
        return IsUserType(baseType) || set.Contains(baseType);
    }

    /// <summary>All point types (for editor completion).</summary>
    public static IReadOnlyCollection<string> PointTypeNames => PointTypes;
    public static IReadOnlyCollection<string> LineTypeNames => LineTypes;
    public static IReadOnlyCollection<string> AreaTypeNames => AreaTypes;

    /// <summary>
    /// Option names (dash stripped) accepted on <c>point</c> / <c>line</c> / <c>area</c> objects and
    /// their [LINE DATA] points (thbook §point/line/area pp.25-32). A union across object kinds — a
    /// type-specific option used on the wrong object is not flagged, only genuinely unknown options.
    /// </summary>
    public static readonly ImmutableHashSet<string> ObjectOptions =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            // common (th2ddataobject + point)
            "id", "subtype", "orientation", "orient", "scale", "place", "clip", "visibility",
            "visible", "context", "align", "text", "value", "name", "from", "scrap", "explored",
            "dist", "attr",
            // line / line-data (thline.h:90 thtt_line_opt — B3-verified; the previous list had
            // phantom `rebound`/`clip-radius` and missed anchors/rebelays/r-size/height)
            "adjust", "altitude", "anchors", "border", "close", "direction", "gradient", "head",
            "height", "l-size", "mark", "outline", "r-size", "rebelays", "reverse", "size", "smooth",
            // misc accepted forms
            "station", "extend");

    /// <summary>True if <paramref name="name"/> (dash stripped) is a known object option.</summary>
    public static bool IsKnownOption(string name) => ObjectOptions.Contains(name);
}
