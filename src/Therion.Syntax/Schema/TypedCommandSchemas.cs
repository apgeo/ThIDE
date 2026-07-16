// Option tables for the typed command nodes that keep their option tail as raw text
// (SelectCommand, ExportCommand, ScrapBlock). These are not in SchemaRegistry: the registry
// keys by (context, single keyword), and `export` splits its option set by its *type*
// argument ("export model" accepts -wall-source, "export map" accepts -proj), which that
// key cannot express.
//
// Two consumers read them: TypedOptionRules (validation) and the MCP describe_command tool
// (syntax help). One copy, so help can never document an option the validator rejects.
// Sources: thselector.cxx:62, thexp*.h option tables, thscrap.h:74 (spec §6.1/§7).

using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>Option schemas for typed command nodes (spec §6.1, §7).</summary>
public static class TypedCommandSchemas
{
    private static readonly ImmutableHashSet<string> ModelItems = Set(
        "all", "cave-centerline", "cave-centreline", "centerline", "centreline",
        "entrances", "splay-shots", "surface", "surface-centerline", "surface-centreline", "walls");
    private static readonly ImmutableHashSet<string> MapItems = Set("all", "entrances");
    private static readonly ImmutableHashSet<string> WallSources = Set(
        "all", "centerline", "centreline", "maps", "scans", "splays");

    public static readonly CommandSchema Select = Mini("select", "thconfig",
        Opt("recursive", new ValueSpec(SchemaValueKind.Bool)),
        Opt("map-level", ValueSpec.Free),           // <n> | basic
        Opt("chapter-level", ValueSpec.Free),       // <n> | - | .
        Opt("color", ValueSpec.Free, "colour"));

    public static readonly CommandSchema ExportMap = Mini("export map", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),       // value validated by TH0061 already
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("layout", ValueSpec.Free),
        Opt("proj", ValueSpec.Free, "projection"),
        Opt("enable", ValueSpec.OfEnum(MapItems)),
        Opt("disable", ValueSpec.OfEnum(MapItems)));

    public static readonly CommandSchema ExportModel = Mini("export model", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("enable", ValueSpec.OfEnum(ModelItems)),
        Opt("disable", ValueSpec.OfEnum(ModelItems)),
        Opt("wall-source", ValueSpec.OfEnum(WallSources)));

    public static readonly CommandSchema ExportTable = Mini("export list", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("attr", ValueSpec.Free, "attributes"),
        Opt("filter", ValueSpec.Free),
        Opt("location", new ValueSpec(SchemaValueKind.Bool)),
        Opt("surveys", ValueSpec.Free));

    public static readonly CommandSchema ExportDatabase = Mini("export database", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"));

    public static readonly CommandSchema Scrap = Mini("scrap", "scrap",
        Opt("projection", ValueSpec.Free, "proj"),  // type[:index] / [elevation …] — parsed elsewhere
        Opt("scale", ValueSpec.Free),
        Opt("cs", ValueSpec.Free),
        Opt("stations", ValueSpec.Free),
        Opt("sketch", ValueSpec.Free),
        Opt("walls", new ValueSpec(SchemaValueKind.OnOffAuto)),
        Opt("flip", ValueSpec.OfEnum(Set("none", "horiz", "horizontal", "vert", "vertical"))),
        Opt("station-names", ValueSpec.Free),
        // DataObjectOptions
        Opt("attr", ValueSpec.Free), Opt("author", ValueSpec.Free),
        Opt("copyright", ValueSpec.Free), Opt("id", ValueSpec.Free),
        Opt("title", ValueSpec.Free));

    /// <summary>
    /// The option schema for <c>export &lt;type&gt;</c>. An unknown type falls back to the map
    /// schema, which is reported separately (TH0060) rather than double-flagged here.
    /// </summary>
    public static CommandSchema ExportSchemaFor(string type) => (type ?? string.Empty).ToLowerInvariant() switch
    {
        "map" or "atlas" => ExportMap,
        "model" => ExportModel,
        "cave-list" or "survey-list" or "continuation-list" => ExportTable,
        "database" => ExportDatabase,
        _ => ExportMap,
    };

    // ---- helpers -----------------------------------------------------------------------

    private static CommandSchema Mini(string keyword, string section, params OptionSpec[] opts) =>
        new(keyword, ImmutableArray<SchemaContext>.Empty, section,
            ImmutableArray<ParamSpec>.Empty, ImmutableArray.CreateRange(opts),
            ImmutableArray<OptionSet>.Empty, ImmutableArray<string>.Empty);

    private static OptionSpec Opt(string name, ValueSpec value, params string[] aliases) =>
        new(name, ImmutableArray.Create(new ParamSpec(name, value)), ImmutableArray.Create(aliases));

    private static ImmutableHashSet<string> Set(params string[] items) =>
        ImmutableHashSet.Create(System.StringComparer.OrdinalIgnoreCase, items);
}
