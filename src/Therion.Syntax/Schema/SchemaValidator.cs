// Schema-driven validation pass (syntax-coverage effort, batch A1).
// Runs AFTER parsing, over the typed AST: looks up each command's CommandSchema and applies
// the enabled check categories. With the (A1) empty default registry this is a guaranteed
// no-op; the C batches populate the registry section by section (PLAN §6), gated on the corpus.
//
// A1 scope: tree walk + context mapping + category/section gating + generic argument checks
// for UnknownCommand nodes (raw args available). Typed AST nodes gain schema-aware argument
// accessors in the C batches. Perf budget: .claude/therion-syntax/PERF.md.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax.Schema;

/// <summary>Validates a parsed <see cref="TherionFile"/> against a <see cref="SchemaRegistry"/>.</summary>
public static class SchemaValidator
{
    /// <summary>Special values Therion accepts where a reading may be missing/unbounded
    /// (thparse.h thtt_special_val — exact spellings, case-sensitive). Deliberately a SUBSET:
    /// the source table also has <c>all</c>/<c>off</c>, which are only meaningful in specific
    /// contexts (e.g. layout <c>survey-level</c>) and are handled there, not as generic specials.</summary>
    public static readonly ImmutableHashSet<string> SpecialValues =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "-", ".", "nan", "NAN", "NaN", "Inf", "inf", "INF",
            "-Inf", "-inf", "-INF", "up", "Up", "UP", "down", "Down", "DOWN");

    public static void Validate(
        TherionFile file,
        SchemaContext rootContext,
        SchemaValidationOptions options,
        SchemaRegistry registry,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode = ParserMode.Lenient)
    {
        if (!options.Enabled) return;
        ValidateChildren(file.Children, rootContext, options, registry, diagnostics, mode);
    }

    private static void ValidateChildren(
        ImmutableArray<TherionNode> children,
        SchemaContext context,
        SchemaValidationOptions options,
        SchemaRegistry registry,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        foreach (var node in children)
        {
            // .th2 objects are not TherionCommands — they get their own rule dispatch.
            switch (node)
            {
                case PointObject point:
                    Th2PointRules.Validate(point, options, diagnostics, mode);
                    continue;
                case LineObject line:
                    Th2LineRules.ValidateLine(line, options, diagnostics, mode);
                    continue;
                case AreaObject area:
                    Th2LineRules.ValidateArea(area, options, diagnostics, mode);
                    continue;
            }

            if (node is not TherionCommand cmd) continue;

            if (registry.TryGet(context, cmd.Keyword, out var schema) &&
                options.IsSectionEnabled(schema.Section))
            {
                ValidateCommand(cmd, schema, options, diagnostics, mode);
            }

            // Typed centreline nodes carry parsed arguments — rule checks live per node type
            // (spec §5.2/§5.3), independent of the keyword registry.
            ThCentrelineRules.Validate(cmd, options, diagnostics, mode);
            TypedOptionRules.Validate(cmd, options, diagnostics, mode);

            if (node is BlockCommand block)
                ValidateChildren(block.Children, ChildContext(context, block.Keyword),
                    options, registry, diagnostics, mode);
        }
    }

    /// <summary>Context inside a block. Unknown/scoping blocks (e.g. <c>group</c>) are transparent.</summary>
    private static SchemaContext ChildContext(SchemaContext parent, string blockKeyword) =>
        blockKeyword.ToLowerInvariant() switch
        {
            "survey" => SchemaContext.Survey,
            "centreline" or "centerline" => SchemaContext.Centreline,
            "scrap" => SchemaContext.Scrap,
            "line" => SchemaContext.LineBody,
            "area" => SchemaContext.AreaBody,
            "map" => SchemaContext.MapBody,
            "layout" => SchemaContext.Layout,
            _ => parent,
        };

    private static void ValidateCommand(
        TherionCommand cmd,
        CommandSchema schema,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        // Only UnknownCommand exposes its raw argument text generically; typed nodes are
        // validated by their per-node rules (ThCentrelineRules / Th2*Rules).
        if (cmd is not UnknownCommand unknown) return;

        var (args, opts) = SplitCommandLine(unknown.RawArguments, schema);

        if (options.IsCategoryEnabled(ValidationCategories.Arity))
            CheckArity(args.Count, schema, cmd.Span, diagnostics);

        foreach (var (argIndex, param) in MapPositionals(args.Count, schema.Positional))
            CheckValue(args[argIndex], param, cmd.Span, options, diagnostics, mode);

        if (!options.IsCategoryEnabled(ValidationCategories.Options)) return;
        foreach (var (name, firstValue) in opts)
        {
            var spec = FindOption(schema, name);
            if (spec is null)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.OptionNotValidInContext,
                    mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                    $"Unknown option '-{name}' for '{schema.Keyword}'.", cmd.Span));
            }
            else if (spec.Values.Length > 0 && firstValue is not null)
            {
                CheckValue(firstValue, spec.Values[0], cmd.Span, options, diagnostics, mode);
            }
        }
    }

    /// <summary>
    /// Pairs each positional argument index with the <see cref="ParamSpec"/> it belongs to.
    /// Honours one <see cref="ParamSpec.Repeated"/> parameter: params before it map from the
    /// left, params after it map from the right (Therion parses greedy tails, e.g.
    /// <c>mark &lt;station&gt;… &lt;type&gt;</c> where the TYPE is always the LAST argument),
    /// and the repeated param absorbs the middle. When any param after the repeated one is
    /// optional the tail alignment is ambiguous (e.g. <c>units &lt;qty&gt;… [factor] unit</c>) —
    /// only the head is mapped then, so no value is ever checked against the wrong spec.
    /// </summary>
    public static IEnumerable<(int ArgIndex, ParamSpec Param)> MapPositionals(
        int argCount, ImmutableArray<ParamSpec> positional)
    {
        int rep = -1;
        for (int i = 0; i < positional.Length; i++)
            if (positional[i].Repeated) { rep = i; break; }

        if (rep < 0)
        {
            int n = Math.Min(argCount, positional.Length);
            for (int i = 0; i < n; i++) yield return (i, positional[i]);
            yield break;
        }

        int head = Math.Min(rep, argCount);
        for (int i = 0; i < head; i++) yield return (i, positional[i]);

        bool tailReliable = true;
        for (int i = rep + 1; i < positional.Length; i++)
            if (!positional[i].Required) { tailReliable = false; break; }
        if (!tailReliable) yield break;

        int tail = positional.Length - rep - 1;
        int tailStart = Math.Max(argCount - tail, head);
        for (int i = tailStart; i < argCount; i++)
            yield return (i, positional[rep + 1 + (i - tailStart)]);
        for (int i = head; i < tailStart; i++)
            yield return (i, positional[rep]);
    }

    /// <summary>Resolves an option by name/alias, searching the command's own options
    /// and its inherited option sets.</summary>
    internal static OptionSpec? FindOption(CommandSchema schema, string name)
    {
        static OptionSpec? Scan(ImmutableArray<OptionSpec> opts, string name)
        {
            foreach (var o in opts)
            {
                if (string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)) return o;
                foreach (var a in o.Aliases)
                    if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return o;
            }
            return null;
        }

        if (Scan(schema.Options, name) is { } own) return own;
        foreach (var set in schema.Inherits)
            if (Scan(set.Options, name) is { } inherited) return inherited;
        return null;
    }

    private static void CheckArity(
        int argCount, CommandSchema schema, SourceSpan span,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        if (argCount < schema.MinArgs)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.MissingRequiredArgument, DiagnosticSeverity.Error,
                $"'{schema.Keyword}' expects at least {schema.MinArgs} argument(s), found {argCount}.",
                span));
        }
        else if (schema.MaxArgs is { } max && argCount > max)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCodes.TooManyArguments, DiagnosticSeverity.Warning,
                $"'{schema.Keyword}' expects at most {max} argument(s), found {argCount}.",
                span));
        }
    }

    internal static void CheckValue(
        string token, ParamSpec param, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode)
    {
        var spec = param.Value;
        switch (spec.Kind)
        {
            case SchemaValueKind.Number or SchemaValueKind.Int or SchemaValueKind.NonNegative
                or SchemaValueKind.Angle or SchemaValueKind.Clino or SchemaValueKind.Percent
                or SchemaValueKind.Length or SchemaValueKind.Coord:
                if (!options.IsCategoryEnabled(ValidationCategories.ValueTypes)) return;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.ValueTypeMismatch,
                        mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                        $"'{token}' is not a valid numeric value for <{param.Name}>.", span));
                    return;
                }
                var range = spec.Range ?? DefaultRange(spec.Kind);
                if (range is not null && !range.Contains(value) &&
                    options.IsCategoryEnabled(ValidationCategories.Ranges))
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.ValueOutOfRange, DiagnosticSeverity.Warning,
                        $"<{param.Name}> value {value.ToString(CultureInfo.InvariantCulture)} is outside {range}.",
                        span));
                }
                return;

            case SchemaValueKind.Enum when spec.Enum is { } table:
                CheckEnum(token, table, param.Name, span, options, diagnostics, mode,
                    warnOnCaseMismatch: spec.CaseSensitive);
                return;

            case SchemaValueKind.Bool:
                CheckEnum(token, BoolValues, param.Name, span, options, diagnostics, mode);
                return;

            case SchemaValueKind.OnOffAuto:
                CheckEnum(token, OnOffAutoValues, param.Name, span, options, diagnostics, mode);
                return;

            case SchemaValueKind.SpecialOrNumber:
                if (!options.IsCategoryEnabled(ValidationCategories.SpecialValues)) return;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return;
                if (SpecialValues.Contains(token)) return;
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCodes.InvalidSpecialValue, DiagnosticSeverity.Warning,
                    $"'{token}' is neither a number nor a Therion special value (- . NaN Inf up down).",
                    span));
                return;

            case SchemaValueKind.Identifier:
                if (!options.IsCategoryEnabled(ValidationCategories.Identifiers)) return;
                if (TherionIdentifiers.FirstIllegalChar(token) is { } bad)
                {
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.IllegalIdentifier, DiagnosticSeverity.Warning,
                        $"Identifier '{token}' contains an illegal character '{bad}'.", span));
                }
                return;

            // FUTURE(C batches): Date, Person, StationRef, ObjectRef, Units, Color.
            default:
                return;
        }
    }

    private static void CheckEnum(
        string token, ImmutableHashSet<string> table, string paramName, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ParserMode mode,
        bool warnOnCaseMismatch = true)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;

        // Most of our tables are OrdinalIgnoreCase, so Contains() alone would swallow wrong-case
        // spellings that Therion's thmatch_token rejects (spec §2.1). TryGetValue hands back the
        // STORED spelling, letting us confirm the exact case behind the insensitive hit.
        if (table.TryGetValue(token, out var stored))
        {
            if (!string.Equals(stored, token, StringComparison.Ordinal))
                ReportCaseMismatch(token, stored, span, options, diagnostics, warnOnCaseMismatch);
            return;
        }

        // Ordinal tables (Bool/OnOffAuto) miss on wrong case — rescue by scanning.
        foreach (var entry in table)
        {
            if (string.Equals(entry, token, StringComparison.OrdinalIgnoreCase))
            {
                ReportCaseMismatch(token, entry, span, options, diagnostics, warnOnCaseMismatch);
                return;
            }
        }

        diagnostics.Add(Diagnostic.Create(
            DiagnosticCodes.ValueTypeMismatch,
            mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            $"'{token}' is not a valid value for <{paramName}>.", span));
    }

    private static void ReportCaseMismatch(
        string token, string stored, SourceSpan span,
        SchemaValidationOptions options,
        ImmutableArray<Diagnostic>.Builder diagnostics,
        bool warnOnCaseMismatch)
    {
        // warnOnCaseMismatch = false marks tables Therion matches with thcasematch_token
        // (genuinely case-insensitive), where a differing spelling is not a defect.
        if (!warnOnCaseMismatch || !options.IsCategoryEnabled(ValidationCategories.CaseSensitivity))
            return;
        diagnostics.Add(Diagnostic.Create(
            DiagnosticCodes.KeywordCaseMismatch, DiagnosticSeverity.Info,
            $"'{token}' should be spelled '{stored}' (Therion keywords are case-sensitive).",
            span));
    }

    private static NumericRange? DefaultRange(SchemaValueKind kind) => kind switch
    {
        SchemaValueKind.NonNegative => NumericRange.NonNegative,
        SchemaValueKind.Angle => NumericRange.Angle,
        SchemaValueKind.Clino => NumericRange.Clino,
        SchemaValueKind.Percent => NumericRange.Percent,
        _ => null,
    };

    private static readonly ImmutableHashSet<string> BoolValues =
        ImmutableHashSet.Create(StringComparer.Ordinal, "on", "off");
    private static readonly ImmutableHashSet<string> OnOffAutoValues =
        ImmutableHashSet.Create(StringComparer.Ordinal, "on", "off", "auto");

    /// <summary>
    /// Splits a raw argument string into positional tokens: whitespace-separated, honouring
    /// <c>"quoted strings"</c> and <c>[bracketed groups]</c>. Stops at the first token that
    /// names a schema option (<c>-option</c>) — negative numbers are NOT treated as options.
    /// </summary>
    public static List<string> SplitArguments(string raw, CommandSchema schema) =>
        SplitCommandLine(raw, schema).Positional;

    /// <summary>
    /// Splits a raw argument string into positional tokens plus the <c>-option value</c> tail
    /// (each option paired with its first value, if any).
    /// </summary>
    public static (List<string> Positional, List<(string Name, string? FirstValue)> Options)
        SplitCommandLine(string raw, CommandSchema schema)
    {
        var positional = new List<string>();
        var opts = new List<(string, string?)>();
        bool inOptions = false;
        string? currentOption = null;
        bool currentHasValue = false;

        int i = 0;
        while (i < raw.Length)
        {
            while (i < raw.Length && char.IsWhiteSpace(raw[i])) i++;
            if (i >= raw.Length) break;

            int start = i;
            string token;
            if (raw[i] == '"')
            {
                i++;
                while (i < raw.Length && raw[i] != '"') i++;
                if (i < raw.Length) i++;                       // consume closing quote
                token = raw[start..i];
            }
            else if (raw[i] == '[')
            {
                while (i < raw.Length && raw[i] != ']') i++;
                if (i < raw.Length) i++;                       // consume closing bracket
                token = raw[start..i];
            }
            else
            {
                while (i < raw.Length && !char.IsWhiteSpace(raw[i])) i++;
                token = raw[start..i];
            }

            // Raw argument text may keep a trailing `# comment` (round-trip fidelity) —
            // everything from an unquoted '#' on is not arguments.
            if (token[0] == '#') break;

            // A dash-word is an option name (Therion lexing): declared or not. Negative
            // numbers and special values (`-Inf`) stay positional; identifiers can't
            // legally start with '-' (spec §2.2), so this doesn't eat positionals.
            bool isOptionName =
                token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]) && token[1] != '.' &&
                !SpecialValues.Contains(token) &&
                (IsDeclaredOption(schema, token.AsSpan(1)) || LooksLikeOption(token));

            if (isOptionName)
            {
                if (currentOption is not null && !currentHasValue) opts.Add((currentOption, null));
                currentOption = token[1..];
                currentHasValue = false;
                inOptions = true;
            }
            else if (inOptions)
            {
                if (currentOption is not null && !currentHasValue)
                {
                    opts.Add((currentOption, token));
                    currentHasValue = true;
                }
                // further values of the same option: kept out of scope for now
            }
            else
            {
                positional.Add(token);
            }
        }
        if (currentOption is not null && !currentHasValue) opts.Add((currentOption, null));
        return (positional, opts);

        static bool IsDeclaredOption(CommandSchema schema, ReadOnlySpan<char> name)
        {
            foreach (var opt in schema.Options)
            {
                if (name.Equals(opt.Name, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var alias in opt.Aliases)
                    if (name.Equals(alias, StringComparison.OrdinalIgnoreCase)) return true;
            }
            foreach (var set in schema.Inherits)
                foreach (var opt in set.Options)
                {
                    if (name.Equals(opt.Name, StringComparison.OrdinalIgnoreCase)) return true;
                    foreach (var alias in opt.Aliases)
                        if (name.Equals(alias, StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        static bool LooksLikeOption(string token)
        {
            // Heuristic when the schema declares no/unknown options: `-title` style tokens
            // (letters only after the dash) are treated as options, not positionals.
            for (int k = 1; k < token.Length; k++)
                if (!char.IsLetter(token[k]) && token[k] != '-') return false;
            return true;
        }
    }
}
