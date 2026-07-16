using System.Collections.Immutable;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Therion.Semantics.Thbook;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Mcp.Tools;

/// <param name="Name">The parameter name, as it reads in the syntax line.</param>
/// <param name="Kind">The value kind, e.g. "Identifier", "Date", "Enum", "StationRef".</param>
/// <param name="Values">The closed set of accepted words, when the kind is an enum; otherwise null.</param>
/// <param name="Range">A numeric interval like "[0, 360)", when the value is range-checked.</param>
public sealed record CommandParamDoc(
    string Name,
    string Kind,
    bool Required,
    bool Repeated,
    IReadOnlyList<string>? Values,
    string? Range);

/// <param name="Aliases">Other spellings Therion accepts for this option, e.g. "format" for "fmt".</param>
/// <param name="Values">What the option takes; empty for a flag.</param>
public sealed record CommandOptionDoc(
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<CommandParamDoc> Values,
    bool Deprecated);

/// <param name="Keyword">The canonical command keyword.</param>
/// <param name="Syntax">A ready-to-adapt usage line, e.g. "export model [-fmt &lt;format&gt;] …".</param>
/// <param name="Contexts">The blocks this command is legal in; empty for a thconfig-level typed command.</param>
/// <param name="Terminator">The closing keyword for a block command ("endsurvey"); null for a line command.</param>
/// <param name="Variants">
/// Sibling forms to ask about when one command's options depend on an argument — <c>export</c>'s types.
/// </param>
/// <param name="SourceRef">Where in the Therion source this grammar came from — traceability, not user text.</param>
/// <param name="ThbookCitation">The Therion Book page covering the command, when it is indexed.</param>
public sealed record CommandDoc(
    string Keyword,
    string Syntax,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Contexts,
    string Section,
    bool IsBlock,
    string? Terminator,
    IReadOnlyList<CommandParamDoc> Positional,
    IReadOnlyList<CommandOptionDoc> Options,
    IReadOnlyList<string> Variants,
    string? SourceRef,
    string? Notes,
    string? ThbookCitation);

/// <summary>
/// Ring R1 — syntax help derived from the grammar model the parser and validator already use
/// (<see cref="SchemaRegistry"/> + <see cref="TypedCommandSchemas"/>), so help can never document
/// an option the validator would reject. No workspace needed: this is the language, not a project.
/// Pairs with <c>search_thbook</c>, which gives the book page but not the syntax.
/// </summary>
[McpServerToolType]
public sealed class SyntaxTools
{
    /// <summary>
    /// Commands whose option set depends on their first argument, so the registry's (context, keyword)
    /// key cannot hold them. Keyed by the compound name a caller may ask for ("export model").
    /// </summary>
    private static readonly ImmutableDictionary<string, CommandSchema> TypedForms =
        new Dictionary<string, CommandSchema>(StringComparer.OrdinalIgnoreCase)
        {
            ["select"] = TypedCommandSchemas.Select,
            ["scrap"] = TypedCommandSchemas.Scrap,
            ["export map"] = TypedCommandSchemas.ExportMap,
            ["export atlas"] = TypedCommandSchemas.ExportMap,
            ["export model"] = TypedCommandSchemas.ExportModel,
            ["export cave-list"] = TypedCommandSchemas.ExportTable,
            ["export survey-list"] = TypedCommandSchemas.ExportTable,
            ["export continuation-list"] = TypedCommandSchemas.ExportTable,
            ["export database"] = TypedCommandSchemas.ExportDatabase,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    [McpServerTool(Name = "describe_command", Title = "Describe a Therion command",
        ReadOnly = true, Idempotent = true)]
    [Description("Gives the real syntax of a Therion command — positional arguments, options, accepted "
               + "values and enums — from the same grammar model the validator uses, so what it documents "
               + "is what the compiler accepts. Use it before writing or editing Therion source instead of "
               + "recalling syntax from memory. Options of 'export' depend on its type: ask for "
               + "'export model' or 'export map' (plain 'export' lists the types). An unknown command is "
               + "answered with every command this tool knows. For the Therion Book page, use search_thbook.")]
    public ToolResult<CommandDoc> DescribeCommand(
        [Description("The command, e.g. 'map', 'centreline', 'fix', or 'export model'. Case-insensitive.")]
        string command,
        [Description("Only when the same keyword differs by block, e.g. 'Centreline' or 'ThTopLevel'. "
                   + "Omit to get the first context that defines it.")]
        string? context = null)
    {
        var key = Normalize(command);
        if (key.Length == 0)
            return ToolResult<CommandDoc>.Failure(ToolErrorCodes.InvalidArgument,
                $"Name a command to describe, e.g. 'map' or 'export model'. Known: {KnownList()}.");

        // `export` alone cannot be answered: its options are per-type. Point at the variants rather
        // than picking one and documenting options the caller's type does not accept.
        if (key.Equals("export", StringComparison.OrdinalIgnoreCase))
            return ToolResult<CommandDoc>.Success(ExportOverview());

        if (TypedForms.TryGetValue(key, out var typed))
            return ToolResult<CommandDoc>.Success(Describe(typed, ExportTypeOf(key)));

        if (context is { Length: > 0 })
        {
            if (!Enum.TryParse<SchemaContext>(context, ignoreCase: true, out var ctx))
                return ToolResult<CommandDoc>.Failure(ToolErrorCodes.InvalidArgument,
                    $"Unknown context '{context}'. Use one of: {string.Join(", ", Enum.GetNames<SchemaContext>())}.");

            return SchemaRegistry.Default.TryGet(ctx, key, out var inCtx)
                ? ToolResult<CommandDoc>.Success(Describe(inCtx))
                : ToolResult<CommandDoc>.Failure(ToolErrorCodes.InvalidArgument,
                    $"'{key}' is not a known command in {ctx}. It may be legal in another block — omit "
                    + "context to search them all.");
        }

        foreach (var ctx in Enum.GetValues<SchemaContext>())
            if (SchemaRegistry.Default.TryGet(ctx, key, out var found))
                return ToolResult<CommandDoc>.Success(Describe(found));

        // The grammar model is deliberately incomplete (it grows command by command), so a miss must
        // not read as "no such Therion command". Listing what is known is short enough to inline, and
        // saves the catalog a second tool that would exist only to be discoverable.
        return ToolResult<CommandDoc>.Failure(ToolErrorCodes.InvalidArgument,
            $"No syntax model for '{command}' — this tool does not yet cover every Therion command, so it "
            + $"may still be valid; try search_thbook for the book page. Known: {KnownList()}.");
    }

    /// <summary>Every keyword <see cref="DescribeCommand"/> can answer, sorted.</summary>
    public static IReadOnlyList<string> KnownCommands { get; } =
        ThSchema.Commands.Concat(ThconfigSchema.Commands)
            .Select(c => c.Keyword)
            .Concat(TypedForms.Keys)
            .Concat(["export"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static string KnownList() => string.Join(", ", KnownCommands);

    // ---- rendering -------------------------------------------------------------------------------

    /// <summary>`export` with no type: the types are the answer, plus where to look next.</summary>
    private static CommandDoc ExportOverview()
    {
        var types = CommandVocabulary.ExportTypes.OrderBy(t => t, StringComparer.Ordinal).ToList();
        return new CommandDoc(
            Keyword: "export",
            Syntax: "export <type> [-fmt <format>] [-o <file>] [-cs <coordinate-system>] …",
            Aliases: [],
            Contexts: [nameof(SchemaContext.Thconfig)],
            Section: "export",
            IsBlock: false,
            Terminator: null,
            Positional: [new CommandParamDoc("type", nameof(SchemaValueKind.Enum), true, false, types, null)],
            Options: [],
            Variants: types.Select(t => $"export {t}").ToList(),
            SourceRef: "thexp*.h option tables",
            Notes: "Each type takes different options and a different -fmt table. Ask for the type you "
                 + "want, e.g. describe_command('export model').",
            ThbookCitation: ThbookIndex.Lookup("export")?.Citation);
    }

    private static CommandDoc Describe(CommandSchema schema, string? exportType = null)
    {
        // An option set the command inherits is part of its real syntax, so flatten it in rather than
        // making the caller know that `survey` also takes every data-object option.
        var options = schema.Options
            .Concat(schema.Inherits.SelectMany(set => set.Options))
            .GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => Doc(o, exportType))
            .ToList();

        var positional = schema.Positional.Select(p => Doc(p)).ToList();

        return new CommandDoc(
            Keyword: schema.Keyword,
            Syntax: Usage(schema, positional, options),
            Aliases: schema.Aliases,
            Contexts: schema.Contexts.Select(c => c.ToString()).ToList(),
            Section: schema.Section,
            IsBlock: schema.IsBlock,
            Terminator: schema.Terminator,
            Positional: positional,
            Options: options,
            Variants: [],
            SourceRef: schema.SourceRef,
            Notes: schema.Notes,
            ThbookCitation: ThbookIndex.Lookup(FirstWord(schema.Keyword))?.Citation);
    }

    private static CommandOptionDoc Doc(OptionSpec option, string? exportType) =>
        new(option.Name,
            option.Aliases,
            option.Values.Select(v => Doc(v, ExportFormatsFor(option, exportType))).ToList(),
            option.Deprecated);

    /// <summary>
    /// The `-fmt` table lives in <see cref="CommandVocabulary"/> (the validator checks it as TH0061,
    /// not through the ValueSpec), so it has to be folded in here or help would say "any value" for
    /// the one option a caller is most likely to get wrong.
    /// </summary>
    private static IReadOnlyList<string>? ExportFormatsFor(OptionSpec option, string? exportType)
    {
        if (exportType is null) return null;
        if (!option.Name.Equals("fmt", StringComparison.OrdinalIgnoreCase)) return null;

        var formats = CommandVocabulary.ExportFormats(exportType);
        return formats.IsEmpty ? null : formats.OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    private static CommandParamDoc Doc(ParamSpec p, IReadOnlyList<string>? valuesOverride = null) =>
        new(p.Name,
            p.Value.Kind.ToString(),
            p.Required,
            p.Repeated,
            valuesOverride ?? Values(p.Value),
            p.Value.Range?.ToString());

    private static IReadOnlyList<string>? Values(ValueSpec v) =>
        v.Enum is null ? null : v.Enum.OrderBy(e => e, StringComparer.Ordinal).ToList();

    /// <summary>A usage line — the shape of the command, which is what a writer actually copies.</summary>
    private static string Usage(
        CommandSchema schema, IReadOnlyList<CommandParamDoc> positional, IReadOnlyList<CommandOptionDoc> options)
    {
        var parts = new List<string> { schema.Keyword };

        foreach (var p in positional)
        {
            var token = $"<{p.Name}>{(p.Repeated ? " …" : "")}";
            parts.Add(p.Required ? token : $"[{token}]");
        }

        foreach (var o in options)
            parts.Add(o.Values.Count == 0
                ? $"[-{o.Name}]"
                : $"[-{o.Name} <{o.Values[0].Name}>]");

        var line = string.Join(" ", parts);
        return schema.IsBlock ? $"{line} … {schema.Terminator}" : line;
    }

    /// <summary>The type argument baked into a compound key ("export model" → "model").</summary>
    private static string? ExportTypeOf(string key)
    {
        const string prefix = "export ";
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? key[prefix.Length..].Trim()
            : null;
    }

    private static string FirstWord(string keyword)
    {
        var space = keyword.IndexOf(' ');
        return space < 0 ? keyword : keyword[..space];
    }

    /// <summary>Collapses inner whitespace so "export   model" keys the same as "export model".</summary>
    private static string Normalize(string? command) =>
        string.Join(' ', (command ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
