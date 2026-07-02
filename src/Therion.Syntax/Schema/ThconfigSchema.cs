// C5 — declarative schema data for .thconfig commands (spec §7; source: thconfig.cxx:79
// thtt_cfg + thtt_cfg_log/thtt_sketchwarp, thinput.cxx:56, thbook ch03). These commands
// parse as UnknownCommand in ThconfigParser (except the typed select/export/maps/layout/cs),
// so the SchemaValidator UnknownCommand path validates their arity, values and options.

using System;
using System.Collections.Immutable;

namespace Therion.Syntax.Schema;

/// <summary>The built-in .thconfig command schemas (Therion 6.4).</summary>
public static class ThconfigSchema
{
    private const string Section = "thconfig";

    private static readonly ImmutableHashSet<string> LogTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "all", "extend", "none");

    // thtt_sketchwarp (thconfig.cxx:408); the book documents line/plaquette, src adds linear/point.
    private static readonly ImmutableHashSet<string> SketchWarpAlgorithms =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "line", "linear", "plaquette", "point");

    public static readonly ImmutableArray<CommandSchema> Commands = Arr(
        // system <command> — exactly ONE argument ("single system command expected",
        // thconfig.cxx TT_SYSTEM); multi-word commands must be quoted.
        Cfg("system", P("command", ValueSpec.Free)),
        Cfg("language", P("code", ValueSpec.Free)) with { Aliases = Arr("lang") },
        Cfg("log", P("type", ValueSpec.OfEnum(LogTypes))),
        Cfg("scrap-sort", P("switch", V(SchemaValueKind.Bool))),
        Cfg("sketch-warp", P("algorithm", ValueSpec.OfEnum(SketchWarpAlgorithms))),
        Cfg("sketch-colors", P("count", V(SchemaValueKind.Int))),
        Cfg("setup3d", P("value", ValueSpec.Number)),
        Cfg("maps-offset", P("switch", V(SchemaValueKind.Bool))),
        // text <language> <original> <translation> — exactly THREE arguments ("invalid text
        // syntax -- should be: text <language> <text> <translation>", thconfig.cxx TT_TEXT).
        Cfg("text",
            P("language", ValueSpec.Free),
            P("original", ValueSpec.Free),
            P("translation", ValueSpec.Free)),
        // encoding/input/require are reader-level (thinput.cxx) but appear in thconfig files too.
        Cfg("encoding", P("name", ValueSpec.Free)));

    private static ImmutableArray<T> Arr<T>(params T[] items) => ImmutableArray.Create(items);

    private static ValueSpec V(SchemaValueKind kind) => new(kind);

    private static ParamSpec P(string name, ValueSpec v, bool required = true, bool repeated = false) =>
        new(name, v, required, repeated);

    private static CommandSchema Cfg(string keyword, params ParamSpec[] positional) =>
        new(keyword, ImmutableArray.Create(SchemaContext.Thconfig), Section,
            ImmutableArray.Create(positional),
            ImmutableArray<OptionSpec>.Empty, ImmutableArray<OptionSet>.Empty,
            ImmutableArray<string>.Empty,
            SourceRef: "thconfig.cxx:79 thtt_cfg — spec §7");
}
