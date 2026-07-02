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
    /// (thparse.h thtt_special_val — exact spellings, case-sensitive).</summary>
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
            if (node is PointObject point)
            {
                Th2PointRules.Validate(point, options, diagnostics, mode);
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
        // A1: only UnknownCommand exposes its raw argument text generically.
        // FUTURE(C batches): typed nodes provide schema-aware argument accessors.
        if (cmd is not UnknownCommand unknown) return;

        var args = SplitArguments(unknown.RawArguments, schema);

        if (options.IsCategoryEnabled(ValidationCategories.Arity))
            CheckArity(args.Count, schema, cmd.Span, diagnostics);

        int n = Math.Min(args.Count, schema.Positional.Length);
        for (int i = 0; i < n; i++)
            CheckValue(args[i], schema.Positional[i], cmd.Span, options, diagnostics, mode);
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

    private static void CheckValue(
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
                        $"<{param.Name}> value {value} is outside [{range.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"}, {range.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}].",
                        span));
                }
                return;

            case SchemaValueKind.Enum when spec.Enum is { } table:
                CheckEnum(token, table, param.Name, span, options, diagnostics, mode);
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
        ParserMode mode)
    {
        if (!options.IsCategoryEnabled(ValidationCategories.Enums)) return;
        if (table.Contains(token)) return; // exact (tables are built ordinal — Therion matches case-sensitively)

        // Case-insensitive rescue: right word, wrong case → the lighter case diagnostic.
        foreach (var entry in table)
        {
            if (string.Equals(entry, token, StringComparison.OrdinalIgnoreCase))
            {
                if (options.IsCategoryEnabled(ValidationCategories.CaseSensitivity))
                    diagnostics.Add(Diagnostic.Create(
                        DiagnosticCodes.KeywordCaseMismatch, DiagnosticSeverity.Info,
                        $"'{token}' should be spelled '{entry}' (Therion keywords are case-sensitive).",
                        span));
                return;
            }
        }

        diagnostics.Add(Diagnostic.Create(
            DiagnosticCodes.ValueTypeMismatch,
            mode == ParserMode.Strict ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            $"'{token}' is not a valid value for <{paramName}>.", span));
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
    public static List<string> SplitArguments(string raw, CommandSchema schema)
    {
        var result = new List<string>();
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

            // An option name ends the positional list (the option tail is validated by the
            // Options category — C batches). `-12.5` and special values (`-Inf`) stay positional.
            if (token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]) && token[1] != '.' &&
                !SpecialValues.Contains(token) &&
                (IsDeclaredOption(schema, token.AsSpan(1)) ||
                 (schema.Options.Length == 0 && LooksLikeOption(token))))
                break;

            result.Add(token);
        }
        return result;

        static bool IsDeclaredOption(CommandSchema schema, ReadOnlySpan<char> name)
        {
            foreach (var opt in schema.Options)
            {
                if (name.Equals(opt.Name, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var alias in opt.Aliases)
                    if (name.Equals(alias, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool LooksLikeOption(string token)
        {
            // Heuristic for schemas with no options declared yet: `-title` style tokens
            // (letters only after the dash) are treated as options, not positionals.
            for (int k = 1; k < token.Length; k++)
                if (!char.IsLetter(token[k]) && token[k] != '-') return false;
            return true;
        }
    }
}
