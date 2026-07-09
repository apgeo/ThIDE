using System.Reflection;
using Therion.Mcp.Tools;

namespace Therion.Mcp.Tests;

public class DiagnosticsToolsTests
{
    [Fact]
    public async Task Get_diagnostics_needs_a_workspace()
    {
        await using var host = new WorkspaceHost();

        var result = await new DiagnosticsTools(host).GetDiagnostics();

        Assert.Equal(ToolErrorCodes.WorkspaceNotLoaded, result.Error!.Code);
    }

    [Fact]
    public async Task Clean_project_reports_no_errors()
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics();

        Assert.True(result.Ok);
        Assert.Equal(0, result.Data!.Errors);
    }

    [Fact]
    public async Task Broken_project_reports_the_bad_value_and_the_missing_include()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics();

        Assert.True(result.Ok);
        var codes = result.Data!.Diagnostics.Select(d => d.Code).ToList();
        Assert.Contains("TH_SEM_006", codes);   // 'abc' is not a length
        Assert.Contains("TH_SEM_014", codes);   // input nowhere.th
        Assert.Equal(result.Data.Total, result.Data.Diagnostics.Count);
    }

    [Fact]
    public async Task Diagnostics_carry_a_workspace_relative_location()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics();

        var located = result.Data!.Diagnostics.First(d => d.Code == "TH_SEM_006");
        Assert.Equal("caves/upper.th", located.File);
        Assert.True(located.Line > 0);
        Assert.True(located.Column > 0);
    }

    [Fact]
    public async Task Severity_floor_filters_and_errors_survive_it()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var everything = await tools.GetDiagnostics(minSeverity: "hint");
        var errorsOnly = await tools.GetDiagnostics(minSeverity: "error");

        Assert.True(errorsOnly.Data!.Total <= everything.Data!.Total);
        Assert.All(errorsOnly.Data.Diagnostics, d => Assert.Equal("error", d.Severity));
        Assert.Equal(everything.Data.Errors, errorsOnly.Data.Total);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("3")]
    public async Task Unknown_severity_is_an_argument_error(string severity)
    {
        using var fixture = FixtureWorkspace.Create();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics(minSeverity: severity);

        Assert.Equal(ToolErrorCodes.InvalidArgument, result.Error!.Code);
    }

    [Fact]
    public async Task Scoping_to_a_file_drops_other_files_findings()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var scoped = await tools.GetDiagnostics(file: "caves/upper.th");

        Assert.True(scoped.Ok);
        Assert.NotEmpty(scoped.Data!.Diagnostics);
        Assert.All(scoped.Data.Diagnostics, d => Assert.Equal("caves/upper.th", d.File));
    }

    [Fact]
    public async Task Scoping_to_a_file_outside_the_workspace_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics(file: "../../etc/passwd");

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
    }

    [Fact]
    public async Task Scoping_to_a_file_the_project_does_not_include_is_refused()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var result = await tools.GetDiagnostics(file: "caves/abandoned.th");

        Assert.Equal(ToolErrorCodes.FileNotFound, result.Error!.Code);
    }

    [Fact]
    public async Task Diagnostics_page()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var tools = await LoadedToolsAsync(fixture);

        var page = await tools.GetDiagnostics(limit: 1);

        Assert.Single(page.Data!.Diagnostics);
        Assert.True(page.Data.Total >= 2);
        Assert.True(page.Data.Truncated);
    }

    [Fact]
    public async Task Explain_diagnostic_answers_a_code_it_knows()
    {
        await using var host = new WorkspaceHost();

        var result = new DiagnosticsTools(host).ExplainDiagnostic("TH_SEM_015");

        Assert.True(result.Ok);
        Assert.Equal("TH_SEM_015", result.Data!.Code);
        Assert.Contains("disconnected", result.Data.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("equate", result.Data.DocTerm);
    }

    [Fact]
    public async Task Explain_diagnostic_tolerates_surrounding_whitespace()
    {
        await using var host = new WorkspaceHost();

        var result = new DiagnosticsTools(host).ExplainDiagnostic("  TH_SEM_015 ");

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Unknown_code_is_a_clean_miss_naming_the_prefixes()
    {
        await using var host = new WorkspaceHost();

        var result = new DiagnosticsTools(host).ExplainDiagnostic("TH_NOPE_999");

        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UnknownDiagnosticCode, result.Error!.Code);
        Assert.Contains("TH_SEM_", result.Error.Message);
    }

    /// <summary>
    /// Every code the parser and the semantic passes can actually emit must either explain itself or
    /// miss cleanly. A code that throws, or that answers ok:true with an empty summary, is a bug the
    /// model would surface as gibberish.
    /// </summary>
    [Fact]
    public async Task Every_shipped_code_explains_itself_or_misses_cleanly()
    {
        await using var host = new WorkspaceHost();
        var tools = new DiagnosticsTools(host);

        var codes = ShippedCodes().ToList();
        Assert.True(codes.Count >= 60, $"Expected the code catalogs to be populated, found {codes.Count}.");

        foreach (var code in codes)
        {
            var result = tools.ExplainDiagnostic(code);
            if (result.Ok)
                Assert.False(string.IsNullOrWhiteSpace(result.Data!.Summary), $"{code} has an empty summary.");
            else
                Assert.Equal(ToolErrorCodes.UnknownDiagnosticCode, result.Error!.Code);
        }
    }

    /// <summary>The `TH*` string constants declared by the two code catalogs.</summary>
    private static IEnumerable<string> ShippedCodes()
    {
        foreach (var type in new[] { typeof(Therion.Syntax.DiagnosticCodes), typeof(Therion.Semantics.SemanticDiagnosticCodes) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field is { IsLiteral: true, IsInitOnly: false } && field.GetRawConstantValue() is string code)
                    yield return code;
    }

    private static async Task<DiagnosticsTools> LoadedToolsAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return new DiagnosticsTools(host);
    }
}
