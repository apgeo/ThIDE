// Schema-validation machinery tests (syntax-coverage effort, A1).
// The DEFAULT registry is empty in A1 (no behaviour change) — these tests exercise the
// pipeline with a synthetic registry: context walk, arity, value kinds, enums, ranges,
// case-mismatch, and every configuration toggle (categories, sections, master switch).

using System;
using System.Collections.Immutable;
using System.Linq;
using Therion.Core;
using Therion.Syntax;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class SchemaValidatorTests
{
    // `frobnicate <count:Number[range]> <mode:{alpha|beta}>`, survey context, section "test".
    private static SchemaRegistry Registry(NumericRange? countRange = null) => new(new[]
    {
        new CommandSchema(
            "frobnicate",
            ImmutableArray.Create(SchemaContext.Survey),
            "test",
            ImmutableArray.Create(
                new ParamSpec("count", new ValueSpec(SchemaValueKind.Number, Range: countRange)),
                new ParamSpec("mode", ValueSpec.OfEnum(
                    ImmutableHashSet.Create(StringComparer.Ordinal, "alpha", "beta")))),
            ImmutableArray<OptionSpec>.Empty,
            ImmutableArray<OptionSet>.Empty,
            ImmutableArray<string>.Empty),
    });

    private static ImmutableArray<Diagnostic> Validate(
        string commandLine,
        SchemaRegistry registry,
        SchemaValidationOptions? options = null)
    {
        var src = $"survey s\n  {commandLine}\nendsurvey\n";
        var parse = new ThParser().Parse("/schema/a.th", src);
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        SchemaValidator.Validate(parse.Value!, SchemaContext.ThTopLevel,
            options ?? SchemaValidationOptions.Default, registry, builder);
        return builder.ToImmutable();
    }

    [Fact]
    public void Default_registry_holds_the_th_grammar_and_stays_silent_on_valid_input()
    {
        // C1.2: the registry carries the spec §5 survey/centreline schemas…
        Assert.True(SchemaRegistry.Default.Count > 25);
        Assert.True(SchemaRegistry.Default.TryGet(SchemaContext.Centreline, "data", out var data));
        Assert.Equal(2, data.MinArgs);
        Assert.Null(data.MaxArgs); // trailing repeated reading
        Assert.True(SchemaRegistry.Default.TryGet(SchemaContext.Survey, "centerline", out _)); // alias
        Assert.True(SchemaRegistry.Default.TryGet(SchemaContext.Centreline, "fix", out var fix));
        Assert.Equal(4, fix.MinArgs);
        Assert.Equal(7, fix.MaxArgs);

        // …and an unregistered keyword still validates to nothing.
        var diags = Validate("frobnicate", SchemaRegistry.Default);
        Assert.Empty(diags);
    }

    [Fact]
    public void Valid_usage_produces_no_diagnostics()
        => Assert.Empty(Validate("frobnicate 5 alpha", Registry()));

    [Fact]
    public void Missing_required_argument_is_error()
    {
        var d = Assert.Single(Validate("frobnicate 5", Registry()));
        Assert.Equal(DiagnosticCodes.MissingRequiredArgument, d.Code.Value);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    [Fact]
    public void Extra_argument_is_warning()
    {
        var d = Assert.Single(Validate("frobnicate 5 alpha extra", Registry()));
        Assert.Equal(DiagnosticCodes.TooManyArguments, d.Code.Value);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Nonnumeric_value_is_type_mismatch()
    {
        var d = Assert.Single(Validate("frobnicate x5 alpha", Registry()));
        Assert.Equal(DiagnosticCodes.ValueTypeMismatch, d.Code.Value);
    }

    [Fact]
    public void Unknown_enum_value_is_type_mismatch()
    {
        var d = Assert.Single(Validate("frobnicate 5 gamma", Registry()));
        Assert.Equal(DiagnosticCodes.ValueTypeMismatch, d.Code.Value);
    }

    [Fact]
    public void Wrong_case_enum_value_is_info_case_mismatch()
    {
        var d = Assert.Single(Validate("frobnicate 5 Alpha", Registry()));
        Assert.Equal(DiagnosticCodes.KeywordCaseMismatch, d.Code.Value);
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
    }

    // REVIEW F1: our real tables are OrdinalIgnoreCase — the wrong-case token used to match
    // silently, never reaching the case-mismatch diagnostic. TryGetValue now recovers the
    // stored spelling behind the case-insensitive hit.
    private static SchemaRegistry IgnoreCaseRegistry(bool therionCaseSensitive = true) => new(new[]
    {
        new CommandSchema(
            "frobnicate",
            ImmutableArray.Create(SchemaContext.Survey),
            "test",
            ImmutableArray.Create(
                new ParamSpec("mode", ValueSpec.OfEnum(
                    ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "alpha", "beta"),
                    caseSensitive: therionCaseSensitive))),
            ImmutableArray<OptionSpec>.Empty,
            ImmutableArray<OptionSet>.Empty,
            ImmutableArray<string>.Empty),
    });

    [Fact]
    public void Wrong_case_on_ignorecase_table_is_still_case_mismatch()
    {
        var d = Assert.Single(Validate("frobnicate Alpha", IgnoreCaseRegistry()));
        Assert.Equal(DiagnosticCodes.KeywordCaseMismatch, d.Code.Value);
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Contains("'alpha'", d.Message);   // suggests the stored spelling
    }

    [Fact]
    public void Exact_case_on_ignorecase_table_is_silent()
        => Assert.Empty(Validate("frobnicate alpha", IgnoreCaseRegistry()));

    [Fact]
    public void Thcasematch_tables_accept_any_case_silently()
        => Assert.Empty(Validate("frobnicate Alpha", IgnoreCaseRegistry(therionCaseSensitive: false)));

    [Fact]
    public void Out_of_range_value_is_flagged()
    {
        var d = Assert.Single(Validate("frobnicate 200 alpha", Registry(new NumericRange(0, 100))));
        Assert.Equal(DiagnosticCodes.ValueOutOfRange, d.Code.Value);
    }

    [Fact]
    public void Command_outside_declared_context_is_not_checked()
    {
        // frobnicate is declared for Survey only — at top level the schema doesn't match.
        var parse = new ThParser().Parse("/schema/a.th", "frobnicate\n");
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        SchemaValidator.Validate(parse.Value!, SchemaContext.ThTopLevel,
            SchemaValidationOptions.Default, Registry(), builder);
        Assert.Empty(builder);
    }

    // --- configuration toggles (user requirement: sections/categories on/off) ---------

    [Fact]
    public void Master_switch_disables_everything()
        => Assert.Empty(Validate("frobnicate", Registry(), SchemaValidationOptions.Off));

    [Fact]
    public void Disabling_arity_category_suppresses_arity_checks()
    {
        var opts = new SchemaValidationOptions(
            Categories: ValidationCategories.All & ~ValidationCategories.Arity);
        Assert.Empty(Validate("frobnicate 5", Registry(), opts));
    }

    [Fact]
    public void Disabling_enums_category_suppresses_enum_checks()
    {
        var opts = new SchemaValidationOptions(
            Categories: ValidationCategories.All & ~ValidationCategories.Enums);
        Assert.Empty(Validate("frobnicate 5 gamma", Registry(), opts));
    }

    [Fact]
    public void Disabling_case_category_keeps_wrong_case_silent()
    {
        var opts = new SchemaValidationOptions(
            Categories: ValidationCategories.All & ~ValidationCategories.CaseSensitivity);
        Assert.Empty(Validate("frobnicate 5 Alpha", Registry(), opts));
    }

    [Fact]
    public void Disabling_the_section_suppresses_all_checks_for_its_commands()
    {
        var opts = new SchemaValidationOptions(
            DisabledSections: ImmutableHashSet.Create("test"));
        Assert.Empty(Validate("frobnicate", Registry(), opts));
    }

    [Fact]
    public void Strict_mode_promotes_type_mismatch_to_error()
    {
        var src = "survey s\n  frobnicate x5 alpha\nendsurvey\n";
        var parse = new ThParser().Parse("/schema/a.th", src);
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        SchemaValidator.Validate(parse.Value!, SchemaContext.ThTopLevel,
            SchemaValidationOptions.Default, Registry(), builder, ParserMode.Strict);
        var d = Assert.Single(builder);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    // --- repeated-aware positional mapping (REVIEW F7) ----------------------------------

    private static ParamSpec Param(string name, bool required = true, bool repeated = false) =>
        new(name, ValueSpec.Free, required, repeated);

    [Fact]
    public void MapPositionals_maps_greedy_tail_after_repeated_param()
    {
        // mark <station>… <type> — the TYPE is always the LAST argument.
        var positional = ImmutableArray.Create(
            Param("station", required: false, repeated: true),
            Param("type"));

        var one = SchemaValidator.MapPositionals(1, positional).ToArray();
        Assert.Equal(new[] { (0, "type") }, one.Select(m => (m.ArgIndex, m.Param.Name)));

        var three = SchemaValidator.MapPositionals(3, positional).ToArray();
        Assert.Equal(new[] { (0, "station"), (1, "station"), (2, "type") },
            three.Select(m => (m.ArgIndex, m.Param.Name)).OrderBy(m => m.Item1).ToArray());
    }

    [Fact]
    public void MapPositionals_skips_ambiguous_tail_with_optional_params()
    {
        // units <qty>… [factor] <unit> — the optional factor makes the tail ambiguous;
        // only the head (nothing here) may be mapped, never a wrong pairing.
        var positional = ImmutableArray.Create(
            Param("quantity", repeated: true),
            Param("factor", required: false),
            Param("unit"));
        Assert.Empty(SchemaValidator.MapPositionals(2, positional));
    }

    [Fact]
    public void MapPositionals_without_repeated_param_maps_by_index()
    {
        var positional = ImmutableArray.Create(Param("a"), Param("b"));
        var m = SchemaValidator.MapPositionals(3, positional).ToArray();
        Assert.Equal(new[] { (0, "a"), (1, "b") }, m.Select(x => (x.ArgIndex, x.Param.Name)));
    }

    // --- exclusive range bounds (REVIEW F10) ---------------------------------------------

    [Theory]
    [InlineData(360.0, false)]   // Angle is [0, 360) — Therion rejects 360 exactly
    [InlineData(359.9, true)]
    [InlineData(0.0, true)]
    public void Angle_range_excludes_the_upper_bound(double value, bool contained)
        => Assert.Equal(contained, NumericRange.Angle.Contains(value));

    [Theory]
    [InlineData(0.0, false)]     // fix sd must be strictly positive
    [InlineData(0.001, true)]
    public void Positive_range_excludes_zero(double value, bool contained)
        => Assert.Equal(contained, NumericRange.Positive.Contains(value));

    // --- argument splitting ------------------------------------------------------------

    [Theory]
    [InlineData("1 2 3", 3)]
    [InlineData("\"a b\" c", 2)]
    [InlineData("[0 1 m] x", 2)]
    [InlineData("-12.5 3", 2)]     // negative number is positional, not an option
    [InlineData("-Inf 3", 2)]      // special value is positional, not an option
    [InlineData("1 -title x", 1)]  // heuristic: '-title' starts the option tail
    public void SplitArguments_handles_quotes_brackets_negatives(string raw, int expected)
    {
        var schema = new CommandSchema("x", ImmutableArray.Create(SchemaContext.Survey), "test",
            ImmutableArray<ParamSpec>.Empty, ImmutableArray<OptionSpec>.Empty,
            ImmutableArray<OptionSet>.Empty, ImmutableArray<string>.Empty);
        Assert.Equal(expected, SchemaValidator.SplitArguments(raw, schema).Count);
    }
}
