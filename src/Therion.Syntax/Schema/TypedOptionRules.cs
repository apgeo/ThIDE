// C5.2 / C2-leftovers — option validation for typed nodes that keep their option tail as
// raw text (SelectCommand, ExportCommand, ScrapBlock, JoinCommand): the OptionsRaw string
// is split with the shared SchemaValidator.SplitCommandLine against a mini-schema, option
// names are checked (TH0066) and single-value options validated against their ValueSpec.
// The mini-schemas live in TypedCommandSchemas (shared with describe_command).

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
                CheckOptions(select.OptionsRaw, TypedCommandSchemas.Select, select.Span, options, diagnostics, mode);
                break;
            case ExportCommand export when options.IsSectionEnabled("export"):
                CheckOptions(export.OptionsRaw, TypedCommandSchemas.ExportSchemaFor(export.ExportType), export.Span,
                    options, diagnostics, mode,
                    allowLayoutPrefix: true);   // xtherion writes -layout-<key> inline overrides
                break;
            case ScrapBlock scrap when options.IsSectionEnabled("scrap"):
                CheckOptions(scrap.OptionsRaw, TypedCommandSchemas.Scrap, scrap.Span, options, diagnostics, mode);
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

}
