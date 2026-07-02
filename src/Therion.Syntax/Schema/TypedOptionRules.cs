// C5.2 / C2-leftovers — option validation for typed nodes that keep their option tail as
// raw text (SelectCommand, ExportCommand, ScrapBlock, JoinCommand): the OptionsRaw string
// is split with the shared SchemaValidator.SplitCommandLine against a mini-schema, option
// names are checked (TH0066) and single-value options validated against their ValueSpec.
// Sources: thselector.cxx:62, thexp*.h option tables, thscrap.h:74 (spec §6.1/§7).

using System;
using System.Collections.Immutable;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Option-tail validation for typed command nodes (spec §6.1, §7).</summary>
internal static class TypedOptionRules
{
    public static void Validate(
        TherionCommand cmd,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        switch (cmd)
        {
            case SelectCommand select when options.IsSectionEnabled("thconfig"):
                CheckOptions(select.OptionsRaw, SelectSchema, select.Span, options, diagnostics, mode);
                break;
            case ExportCommand export when options.IsSectionEnabled("export"):
                CheckOptions(export.OptionsRaw, ExportSchemaFor(export.ExportType), export.Span,
                    options, diagnostics, mode,
                    allowLayoutPrefix: true);   // xtherion writes -layout-<key> inline overrides
                break;
            case ScrapBlock scrap when options.IsSectionEnabled("scrap"):
                CheckOptions(scrap.OptionsRaw, ScrapSchema, scrap.Span, options, diagnostics, mode);
                break;
            case JoinCommand join when options.IsSectionEnabled("scrap"):
                if (options.IsCategoryEnabled(ValidationCategories.Arity) && join.Targets.Length < 2)
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.MissingRequiredArgument,
                        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        "'join' needs at least two objects to connect.", join.Span));
                break;
        }
    }

    private static void CheckOptions(
        string raw, CommandSchema schema, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode,
        bool allowLayoutPrefix = false)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        if (!options.IsCategoryEnabled(ValidationCategories.Options)) return;

        var (_, opts) = SchemaValidator.SplitCommandLine(raw, schema);
        foreach (var (name, firstValue) in opts)
        {
            // xtherion inline layout overrides: `-layout-<key> …` (validate <key> as a layout key).
            if (allowLayoutPrefix && name.StartsWith("layout-", StringComparison.OrdinalIgnoreCase))
            {
                var layoutKey = name["layout-".Length..];
                if (!LayoutKeywords.IsKnown(layoutKey))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.OptionNotValidInContext,
                        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        $"'-layout-{layoutKey}': '{layoutKey}' is not a known layout option.", span));
                continue;
            }

            var spec = SchemaValidator.FindOption(schema, name);
            if (spec is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.OptionNotValidInContext,
                    mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    $"Unknown option '-{name}' for '{schema.Keyword}'.", span));
            }
            else if (spec.Values.Length > 0 && firstValue is not null)
            {
                SchemaValidator.CheckValue(firstValue, spec.Values[0], span, options, diagnostics, mode);
            }
        }
    }

    // ---- mini-schemas (data only; SourceRef in spec) --------------------------------------

    private static readonly CommandSchema SelectSchema = Mini("select", "thconfig",
        Opt("recursive", new ValueSpec(SchemaValueKind.Bool)),
        Opt("map-level", ValueSpec.Free),           // <n> | basic
        Opt("chapter-level", ValueSpec.Free),       // <n> | - | .
        Opt("color", ValueSpec.Free, "colour"));

    private static readonly ImmutableHashSet<string> ModelItems = Set(
        "all", "cave-centerline", "cave-centreline", "centerline", "centreline",
        "entrances", "splay-shots", "surface", "surface-centerline", "surface-centreline", "walls");
    private static readonly ImmutableHashSet<string> MapItems = Set("all", "entrances");
    private static readonly ImmutableHashSet<string> WallSources = Set(
        "all", "centerline", "centreline", "maps", "scans", "splays");

    private static readonly CommandSchema ExportMapSchema = Mini("export map", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),       // value validated by TH0061 already
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("layout", ValueSpec.Free),
        Opt("proj", ValueSpec.Free, "projection"),
        Opt("enable", ValueSpec.OfEnum(MapItems)),
        Opt("disable", ValueSpec.OfEnum(MapItems)));

    private static readonly CommandSchema ExportModelSchema = Mini("export model", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("enable", ValueSpec.OfEnum(ModelItems)),
        Opt("disable", ValueSpec.OfEnum(ModelItems)),
        Opt("wall-source", ValueSpec.OfEnum(WallSources)));

    private static readonly CommandSchema ExportTableSchema = Mini("export list", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"),
        Opt("attr", ValueSpec.Free, "attributes"),
        Opt("filter", ValueSpec.Free),
        Opt("location", new ValueSpec(SchemaValueKind.Bool)),
        Opt("surveys", ValueSpec.Free));

    private static readonly CommandSchema ExportDbSchema = Mini("export database", "export",
        Opt("cs", ValueSpec.Free), Opt("o", ValueSpec.Free, "output"),
        Opt("fmt", ValueSpec.Free, "format"),
        Opt("enc", ValueSpec.Free, "encoding"));

    private static CommandSchema ExportSchemaFor(string type) => type.ToLowerInvariant() switch
    {
        "map" or "atlas" => ExportMapSchema,
        "model" => ExportModelSchema,
        "cave-list" or "survey-list" or "continuation-list" => ExportTableSchema,
        "database" => ExportDbSchema,
        _ => ExportMapSchema,   // unknown type is reported separately (TH0060)
    };

    private static readonly CommandSchema ScrapSchema = Mini("scrap", "scrap",
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

    // ---- helpers -----------------------------------------------------------------------

    private static CommandSchema Mini(string keyword, string section, params OptionSpec[] opts) =>
        new(keyword, ImmutableArray<SchemaContext>.Empty, section,
            ImmutableArray<ParamSpec>.Empty, ImmutableArray.CreateRange(opts),
            ImmutableArray<OptionSet>.Empty, ImmutableArray<string>.Empty);

    private static OptionSpec Opt(string name, ValueSpec value, params string[] aliases) =>
        new(name, ImmutableArray.Create(new ParamSpec(name, value)), ImmutableArray.Create(aliases));

    private static ImmutableHashSet<string> Set(params string[] items) =>
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, items);
}
