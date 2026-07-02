// C1.2 — declarative schema data for .th survey/centreline commands (spec §5,
// .claude/therion-syntax/syntax-spec.md). This is the machine-usable grammar model that
// future features (completion, code generation, refactoring) consume; runtime validation
// of these commands is enforced by ThCentrelineRules on the typed AST (the parser
// produces typed nodes for all of them, so the registry's UnknownCommand argument checks
// do not fire for .th — the entries here are descriptive data with SourceRef citations).

using System;
using System.Collections.Immutable;
using System.Linq;

namespace Therion.Syntax.Schema;

/// <summary>The built-in .th command schemas (survey + centreline, Therion 6.4).</summary>
public static class ThSchema
{
    private const string Survey = "survey";
    private const string Centreline = "centreline";

    /// <summary>Generic options every data object inherits (thdataobject.h:104 thtt_dataobject_opt).</summary>
    public static readonly OptionSet DataObjectOptions = new(
        "dataobject",
        Arr(
            Opt("attr", P("name", ValueSpec.Id(IdentifierCharset.AttrName)), P("value", ValueSpec.String)),
            Opt("author", P("date", V(SchemaValueKind.Date)), P("person", V(SchemaValueKind.Person))),
            Opt("copyright", P("date", V(SchemaValueKind.Date)), P("string", ValueSpec.String)),
            Opt("cs", P("name", ValueSpec.Id(IdentifierCharset.Keyword))),
            Opt("id", P("id", ValueSpec.Id())),
            Opt("station-names", P("prefix", ValueSpec.Id()), P("suffix", ValueSpec.Id())),
            Opt("title", P("title", ValueSpec.String))));

    private static readonly ImmutableHashSet<string> InferWhat =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "plumbs", "equates");

    private static readonly ImmutableHashSet<string> CalibrateQuantities =
        CommandVocabulary.UnitsLengthQuantities.Union(CommandVocabulary.SdAngleQuantities);

    private static readonly ImmutableHashSet<string> DataStyleNames =
        ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, DataStyles.StyleNames);

    private static readonly ImmutableHashSet<string> ReadingNames =
        ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, DataStyles.ReadingKeywords);

    // NOTE: every table must be declared BEFORE Commands (static field initialization order).
    /// <summary>Map/scrap projection base types (thdb2dprj.h:55 thtt_2dproj).</summary>
    public static readonly ImmutableHashSet<string> ProjectionTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "plan", "elevation", "extended", "none");

    private static readonly ImmutableHashSet<string> ImportFormats =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "3d", "plt", "xyz");
    private static readonly ImmutableHashSet<string> ImportSurveys =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "create", "use", "ignore");

    /// <summary>All .th command schemas, keyed for <see cref="SchemaRegistry"/>.</summary>
    public static readonly ImmutableArray<CommandSchema> Commands = Arr(
        // ---- blocks ---------------------------------------------------------------
        Cmd("survey", Survey, Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey),
                P("id", ValueSpec.Id(IdentifierCharset.Keyword)))
            with
            {
                Terminator = "endsurvey",
                Options = Arr(
                    Opt("namespace", P("switch", V(SchemaValueKind.Bool))),
                    Opt("declination", P("spec", ValueSpec.Free)),   // [<date> <value> … <units>]
                    Opt("person-rename", P("old", ValueSpec.String), P("new", ValueSpec.String)),
                    Opt("entrance", P("station", V(SchemaValueKind.StationRef))),
                    Opt("title", P("title", ValueSpec.String))),
                Inherits = Arr(DataObjectOptions),
                SourceRef = "thsurvey.h:57 thtt_survey_opt",
            },
        Cmd("centreline", Centreline, Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey))
            with
            {
                Terminator = "endcentreline",
                Aliases = Arr("centerline"),
                Inherits = Arr(DataObjectOptions),
                SourceRef = "thdata.h:80 thtt_data_opt",
            },
        Cmd("group", Centreline, Ctx(SchemaContext.Centreline))
            with { Terminator = "endgroup", SourceRef = "thdata.h thtt_data_opt" },

        // ---- attribution / metadata -------------------------------------------------
        CL("date", P("date", V(SchemaValueKind.Date), repeated: true)),
        CL("explo-date", P("date", V(SchemaValueKind.Date), repeated: true)),
        CL("team",
            P("person", ValueSpec.String),
            P("role", Enum(CommandVocabulary.TeamRoles), required: false, repeated: true)),
        CL("explo-team", P("person", ValueSpec.String)),
        CL("instrument",
            P("quantity", Enum(CommandVocabulary.InstrumentQuantities), repeated: true),
            P("description", ValueSpec.String)),

        // ---- instrument corrections / units ----------------------------------------
        CL("calibrate",
            P("quantity", Enum(CalibrateQuantities), repeated: true),
            P("zero-error", ValueSpec.Number),
            P("scale", ValueSpec.Number, required: false)),
        CL("units",
            P("quantity", Enum(CalibrateQuantities), repeated: true),
            P("factor", ValueSpec.Number, required: false),
            P("unit", V(SchemaValueKind.Units))),
        CL("sd",
            P("quantity", Enum(CommandVocabulary.SdLengthQuantities.Union(CommandVocabulary.SdAngleQuantities)), repeated: true),
            P("value", V(SchemaValueKind.SpecialOrNumber)),
            P("unit", V(SchemaValueKind.Units), required: false)),
        CL("grade", P("grade", V(SchemaValueKind.ObjectRef), repeated: true)),
        CL("declination",
            P("value", V(SchemaValueKind.SpecialOrNumber)),
            P("unit", V(SchemaValueKind.Units), required: false)),   // required when value is numeric
        CL("grid-angle",
            P("value", ValueSpec.Number),
            P("unit", V(SchemaValueKind.Units), required: false)),
        CL("infer", P("what", Enum(InferWhat)), P("switch", V(SchemaValueKind.Bool))),

        // ---- stations & shots --------------------------------------------------------
        CL("mark",
            P("station", V(SchemaValueKind.StationRef), required: false, repeated: true),
            P("type", Enum(CommandVocabulary.MarkTypes))),           // type is LAST
        CL("flags", P("flag", Enum(CommandVocabulary.ShotFlags.Add("not")), repeated: true)),
        CL("station",
            P("station", V(SchemaValueKind.StationRef)),
            P("comment", ValueSpec.String),
            P("flag", Enum(CommandVocabulary.StationFlags.Union(new[] { "not", "attr", "explored" })),
                required: false, repeated: true)),
        CL("cs", P("name", ValueSpec.Id(IdentifierCharset.Keyword))),
        CL("fix",
            P("station", V(SchemaValueKind.StationRef)),
            P("x", V(SchemaValueKind.Coord)), P("y", V(SchemaValueKind.Coord)),
            P("z", ValueSpec.Number),
            P("sd-1", new ValueSpec(SchemaValueKind.Number, Range: new NumericRange(0, null)), required: false),
            P("sd-2", new ValueSpec(SchemaValueKind.Number, Range: new NumericRange(0, null)), required: false),
            P("sd-3", new ValueSpec(SchemaValueKind.Number, Range: new NumericRange(0, null)), required: false)),
        CL("equate",
            P("station", V(SchemaValueKind.StationRef)),
            P("station-2", V(SchemaValueKind.StationRef)),
            P("station-n", V(SchemaValueKind.StationRef), required: false, repeated: true)),
        CL("data",
            P("style", Enum(DataStyleNames)),
            P("reading", Enum(ReadingNames), repeated: true)),
        CL("break"),
        CL("walls", P("switch", V(SchemaValueKind.OnOffAuto))),
        CL("vthreshold",
            P("value", new ValueSpec(SchemaValueKind.Number, Range: new NumericRange(0, 90))),
            P("unit", V(SchemaValueKind.Units), required: false)),
        CL("extend",
            P("spec", ValueSpec.Free),                               // keyword or 0–200 ratio
            P("from", V(SchemaValueKind.StationRef), required: false),
            P("to", V(SchemaValueKind.StationRef), required: false),
            P("before", V(SchemaValueKind.StationRef), required: false)),
        CL("station-names", P("prefix", ValueSpec.Id()), P("suffix", ValueSpec.Id())),

        // ---- shared .th blocks / commands (B4, spec §6.7) ---------------------------
        Cmd("map", "map", Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey),
                P("id", ValueSpec.Id(IdentifierCharset.Keyword)))
            with
            {
                Terminator = "endmap",
                Options = Arr(
                    Opt("projection", P("type", ValueSpec.Free)) with { Aliases = Arr("proj") },
                    Opt("survey", P("id", V(SchemaValueKind.ObjectRef))),
                    Opt("title", P("title", ValueSpec.String))),
                Inherits = Arr(DataObjectOptions),
                SourceRef = "thmap.h:56 thtt_map_opt + thdb2dprj.h:55",
            },
        Cmd("import", "import", Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey, SchemaContext.Centreline),
                P("file", ValueSpec.Free))
            with
            {
                Options = Arr(
                    Opt("fmt", P("format", Enum(ImportFormats))) with { Aliases = Arr("format") },
                    Opt("surveys", P("mode", Enum(ImportSurveys))),
                    Opt("filter", P("spec", ValueSpec.Free)),
                    Opt("calibrate", P("spec", ValueSpec.Free))),
                SourceRef = "thimport.h:52,74,94",
            },
        Cmd("require", "shared", Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey, SchemaContext.Thconfig),
                P("version", ValueSpec.Free))
            with { SourceRef = "book ch02 §require" },
        Cmd("revise", "shared", Ctx(SchemaContext.ThTopLevel, SchemaContext.Survey),
                P("id", V(SchemaValueKind.ObjectRef)))
            with { SourceRef = "book ch02 §revise", Notes = "block form endrevise unmodeled (no corpus usage)" });

    // ---- helpers -------------------------------------------------------------------

    private static ImmutableArray<T> Arr<T>(params T[] items) => ImmutableArray.Create(items);

    private static ImmutableArray<SchemaContext> Ctx(params SchemaContext[] c) => ImmutableArray.Create(c);

    private static ValueSpec V(SchemaValueKind kind) => new(kind);

    private static ValueSpec Enum(ImmutableHashSet<string> table) => ValueSpec.OfEnum(table);

    private static ParamSpec P(string name, ValueSpec v, bool required = true, bool repeated = false) =>
        new(name, v, required, repeated);

    private static OptionSpec Opt(string name, params ParamSpec[] values) =>
        new(name, ImmutableArray.Create(values), ImmutableArray<string>.Empty);

    private static CommandSchema Cmd(string keyword, string section,
        ImmutableArray<SchemaContext> contexts, params ParamSpec[] positional) =>
        new(keyword, contexts, section, ImmutableArray.Create(positional),
            ImmutableArray<OptionSpec>.Empty, ImmutableArray<OptionSet>.Empty,
            ImmutableArray<string>.Empty);

    /// <summary>A centreline-context line command (SourceRef: thdata.cxx set_data_*).</summary>
    private static CommandSchema CL(string keyword, params ParamSpec[] positional) =>
        Cmd(keyword, Centreline, Ctx(SchemaContext.Centreline), positional)
            with { SourceRef = $"thdata.cxx set_data — spec §5.2 '{keyword}'" };
}
