using System.Text;
using Therion.Mcp.Mutations;

namespace Therion.Mcp.Tests;

/// <summary>
/// The failure paths matter more than the happy one. Every test here is a way a caver's survey could
/// have been corrupted.
/// </summary>
public class MutationEngineTests
{
    [Fact]
    public async Task Dry_run_is_the_default_and_writes_nothing()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, host) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");
        var original = File.ReadAllText(target);

        var result = await engine.ApplyAsync(RenameSurvey(target, "upper", "lower"), dryRun: true);

        Assert.True(result.Ok);
        Assert.True(result.Data!.DryRun);
        Assert.Equal(original, File.ReadAllText(target));
        await host.DisposeAsync();
    }

    [Fact]
    public async Task Dry_run_previews_the_line_before_and_after()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);

        var result = await engine.ApplyAsync(
            RenameSurvey(fixture.PathTo("caves", "upper.th"), "upper", "lower"), dryRun: true);

        var file = Assert.Single(result.Data!.Files);
        Assert.Equal("caves/upper.th", file.Path);
        Assert.Equal("edit", file.Action);
        Assert.NotNull(file.Sha256);

        var line = Assert.Single(file.Preview);
        Assert.Equal("survey upper", line.Before);
        Assert.Equal("survey lower", line.After);
    }

    [Fact]
    public async Task Apply_writes_the_file_and_re_lints_it()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");

        var result = await engine.ApplyAsync(RenameSurvey(target, "upper", "lower"), dryRun: false);

        Assert.True(result.Ok);
        Assert.False(result.Data!.DryRun);
        Assert.Contains("survey lower", File.ReadAllText(target));
        Assert.Equal(0, result.Data.NewErrors);
    }

    /// <summary>The result has to carry its own evidence, or the caller must ask again to learn it broke something.</summary>
    [Fact]
    public async Task Apply_reports_the_errors_it_introduced()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");
        var text = File.ReadAllText(target);

        // A length reading that is not a number: TH_SEM_006.
        int start = text.IndexOf("10.0", StringComparison.Ordinal);
        var plan = new MutationPlan([new EditFile(target, [new TextEdit(start, 4, "10.0", "abc")])]);

        var result = await engine.ApplyAsync(plan, dryRun: false);

        Assert.True(result.Ok);
        Assert.Equal(1, result.Data!.NewErrors);
        Assert.Contains(result.Data.Diagnostics, d => d.Code == "TH_SEM_006");
    }

    [Fact]
    public async Task Apply_reports_the_errors_it_fixed_as_a_negative_delta()
    {
        using var fixture = FixtureWorkspace.CreateBroken();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");
        var text = File.ReadAllText(target);

        int start = text.IndexOf("abc", StringComparison.Ordinal);
        var plan = new MutationPlan([new EditFile(target, [new TextEdit(start, 3, "abc", "10.0")])]);

        var result = await engine.ApplyAsync(plan, dryRun: false);

        Assert.Equal(-1, result.Data!.NewErrors);
        Assert.DoesNotContain(result.Data.Diagnostics, d => d.Code == "TH_SEM_006");
    }

    /// <summary>Someone edited the file between the plan and the apply. Applying anyway rewrites the wrong bytes.</summary>
    [Fact]
    public async Task A_plan_made_against_stale_text_is_refused_whole()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");
        var plan = RenameSurvey(target, "upper", "lower");

        File.WriteAllText(target, "# somebody got here first\n" + File.ReadAllText(target));
        var afterInterference = File.ReadAllText(target);

        var result = await engine.ApplyAsync(plan, dryRun: false);

        Assert.Equal(ToolErrorCodes.StalePlan, result.Error!.Code);
        Assert.Equal(afterInterference, File.ReadAllText(target));
    }

    [Fact]
    public async Task A_sha256_that_no_longer_matches_is_refused()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");

        var result = await engine.ApplyAsync(
            RenameSurvey(target, "upper", "lower"),
            dryRun: false,
            expectedSha256: new Dictionary<string, string> { ["caves/upper.th"] = new string('0', 64) });

        Assert.Equal(ToolErrorCodes.FileChanged, result.Error!.Code);
        Assert.Contains("survey upper", File.ReadAllText(target));
    }

    [Fact]
    public async Task The_sha256_a_dry_run_reports_is_the_one_apply_accepts()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("caves", "upper.th");

        var planned = await engine.ApplyAsync(RenameSurvey(target, "upper", "lower"), dryRun: true);
        var sha = planned.Data!.Files[0].Sha256!;

        var applied = await engine.ApplyAsync(
            RenameSurvey(target, "upper", "lower"), dryRun: false,
            expectedSha256: new Dictionary<string, string> { ["caves/upper.th"] = sha });

        Assert.True(applied.Ok);
    }

    [Fact]
    public async Task A_write_outside_the_workspace_is_refused()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var outside = Path.Combine(Path.GetTempPath(), "thmcp_escape_" + Guid.NewGuid().ToString("N") + ".th");

        var result = await engine.ApplyAsync(
            new MutationPlan([new CreateFile(outside, "survey x\nendsurvey\n")]), dryRun: false);

        Assert.Equal(ToolErrorCodes.PathOutsideWorkspace, result.Error!.Code);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task Create_never_overwrites()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var existing = fixture.PathTo("caves", "upper.th");
        var original = File.ReadAllText(existing);

        var result = await engine.ApplyAsync(
            new MutationPlan([new CreateFile(existing, "clobbered")]), dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal(original, File.ReadAllText(existing));
    }

    [Fact]
    public async Task Create_writes_a_new_file_and_its_parent_directory()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var target = fixture.PathTo("new", "sub", "survey.th");

        var result = await engine.ApplyAsync(
            new MutationPlan([new CreateFile(target, "survey s\nendsurvey\n")]), dryRun: false);

        Assert.True(result.Ok);
        Assert.Equal("create", result.Data!.Files[0].Action);
        Assert.Equal("survey s\nendsurvey\n", File.ReadAllText(target));
    }

    /// <summary>
    /// A Latin-1 file rewritten as UTF-8 keeps its `encoding iso-8859-1` directive, which then lies
    /// about the bytes. Every accented name in the survey is silently corrupted.
    /// </summary>
    [Fact]
    public async Task An_edit_preserves_the_declared_encoding()
    {
        using var fixture = FixtureWorkspace.Create();
        var target = fixture.PathTo("caves", "grotte.th");
        File.WriteAllText(target, "encoding iso-8859-1\n# Grotte de Bédeilhac\nsurvey grotte\nendsurvey\n",
            Encoding.Latin1);
        File.AppendAllText(fixture.Thconfig, "\nsource caves/grotte.th\n");

        var (engine, _) = await EngineAsync(fixture);
        var text = File.ReadAllText(target, Encoding.Latin1);
        int start = text.IndexOf("grotte", StringComparison.Ordinal);

        var result = await engine.ApplyAsync(
            new MutationPlan([new EditFile(target, [new TextEdit(start, 6, "grotte", "gouffre")])]), dryRun: false);

        Assert.True(result.Ok);
        var bytes = File.ReadAllBytes(target);
        Assert.Contains((byte)0xE9, bytes);                                  // é stayed one Latin-1 byte
        Assert.DoesNotContain("Ã©", Encoding.Latin1.GetString(bytes));       // …and did not become UTF-8
        Assert.Contains("survey gouffre", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public async Task An_edit_preserves_a_utf8_byte_order_mark()
    {
        using var fixture = FixtureWorkspace.Create();
        var target = fixture.PathTo("caves", "upper.th");
        var text = File.ReadAllText(target);
        File.WriteAllText(target, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var (engine, _) = await EngineAsync(fixture);
        int start = File.ReadAllText(target).IndexOf("upper", StringComparison.Ordinal);

        var result = await engine.ApplyAsync(
            new MutationPlan([new EditFile(target, [new TextEdit(start, 5, "upper", "lower")])]), dryRun: false);

        Assert.True(result.Ok);
        var bytes = File.ReadAllBytes(target);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public async Task Editing_a_missing_file_is_reported_not_created()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var missing = fixture.PathTo("caves", "nope.th");

        var result = await engine.ApplyAsync(
            new MutationPlan([new EditFile(missing, [new TextEdit(0, 1, "x", "y")])]), dryRun: false);

        Assert.Equal(ToolErrorCodes.FileNotFound, result.Error!.Code);
        Assert.False(File.Exists(missing));
    }

    /// <summary>One bad file must not leave the other half-written.</summary>
    [Fact]
    public async Task A_plan_that_would_fail_halfway_writes_nothing_at_all()
    {
        using var fixture = FixtureWorkspace.Create();
        var (engine, _) = await EngineAsync(fixture);
        var good = fixture.PathTo("caves", "upper.th");
        var original = File.ReadAllText(good);

        var plan = new MutationPlan([
            new EditFile(good, [new TextEdit(original.IndexOf("upper", StringComparison.Ordinal), 5, "upper", "lower")]),
            new CreateFile(good, "clobbered"),   // fails: already exists
        ]);

        var result = await engine.ApplyAsync(plan, dryRun: false);

        Assert.Equal(ToolErrorCodes.FileExists, result.Error!.Code);
        Assert.Equal(original, File.ReadAllText(good));
    }

    [Theory]
    [InlineData(0, 3, "sur", "SUR", "SURvey upper")]                 // start of file
    [InlineData(7, 5, "upper", "u", "survey u")]                     // mid-line
    public void Splice_rewrites_exactly_the_planned_spans(int start, int length, string expected, string replacement, string result)
    {
        Assert.Equal(result, MutationEngine.Splice("survey upper", [new TextEdit(start, length, expected, replacement)]));
    }

    [Fact]
    public void Splice_refuses_overlapping_edits()
    {
        var edits = new TextEdit[] { new(0, 6, "survey", "cave"), new(3, 3, "vey", "x") };

        Assert.Null(MutationEngine.Splice("survey upper", edits));
    }

    [Fact]
    public void Splice_refuses_a_span_past_the_end_of_the_file()
    {
        Assert.Null(MutationEngine.Splice("short", [new TextEdit(3, 99, "rt", "x")]));
    }

    [Fact]
    public void Splice_refuses_a_span_whose_text_has_drifted()
    {
        Assert.Null(MutationEngine.Splice("survey upper", [new TextEdit(7, 5, "lower", "x")]));
    }

    [Fact]
    public void Splice_applies_several_edits_right_to_left_consistently()
    {
        var edits = new TextEdit[] { new(0, 6, "survey", "cave"), new(7, 5, "upper", "lower") };

        Assert.Equal("cave lower", MutationEngine.Splice("survey upper", edits));
    }

    /// <summary>A rename of the survey name token on line 1 — the simplest real plan there is.</summary>
    private static MutationPlan RenameSurvey(string path, string from, string to)
    {
        var text = File.ReadAllText(path);
        int start = text.IndexOf(from, StringComparison.Ordinal);
        return new MutationPlan([new EditFile(path, [new TextEdit(start, from.Length, from, to)])]);
    }

    private static async Task<(MutationEngine Engine, WorkspaceHost Host)> EngineAsync(FixtureWorkspace fixture)
    {
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        return (new MutationEngine(host), host);
    }
}
