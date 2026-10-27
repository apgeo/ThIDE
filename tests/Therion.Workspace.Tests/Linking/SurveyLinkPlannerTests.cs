using System.Collections.Immutable;
using Therion.Semantics;
using Therion.Semantics.Linking;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Linking;

namespace Therion.Workspace.Tests.Linking;

/// <summary>Invented fixture: a two-survey cave and a new trip to link in.</summary>
public class SurveyLinkPlannerTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "link-planner");
    private static string P(string name) => Path.GetFullPath(Path.Combine(Root, name));
    private static ImmutableArray<string> S(params string[] parts) => [.. parts];

    private const string Centreline = "  centreline\n    data normal from to length compass clino\n    1 2 10 0 0\n    2 3 10 90 0\n  endcentreline\n";

    private const string Main =
        "encoding utf-8\n" +
        "survey cave -title \"Cave\"\n" +
        "  input entrance.th\n" +
        "  input deep.th\n" +
        "\n" +
        "  equate 3@entrance 1@deep\n" +
        "endsurvey\n";

    private static Dictionary<string, string> DefaultFiles() => new(StringComparer.OrdinalIgnoreCase)
    {
        [P("main.th")] = Main,
        [P("entrance.th")] = "survey entrance\n" + Centreline + "endsurvey\n",
        [P("deep.th")] = "survey deep\n" + Centreline + "endsurvey\n",
    };

    private const string NewTrip = "survey newtrip\n  input sketches/plan.th2\n" + Centreline + "endsurvey\n";

    private sealed class Fixture
    {
        public required Dictionary<string, string> Files { get; init; }
        public required LinkContext Context { get; init; }
        public required SourceSurvey Source { get; init; }
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Fixture Make(Dictionary<string, string>? files = null, string sourcePath = "incoming/new.th", string sourceText = NewTrip)
    {
        files ??= DefaultFiles();
        var parsed = files.ToDictionary(kv => kv.Key, kv => new ThParser().Parse(kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);
        var ws = WorkspaceSemanticModel.Build(parsed, Array.Empty<XviFile>());
        TherionFile? Ast(string p) => parsed.TryGetValue(p, out var r) ? r.Value : null;
        var scopes = SurveyScopeResolver.Build(ws, Ast);

        var srcAst = new ThParser().Parse(P(sourcePath), sourceText).Value!;
        var source = new SourceSurvey(P(sourcePath), srcAst, new SemanticBinder().Bind(srcAst));

        Fixture? fixture = null;
        var ctx = new LinkContext(ws, scopes, p => files.GetValueOrDefault(Path.GetFullPath(p)), Ast,
            p => files.ContainsKey(Path.GetFullPath(p)) || fixture!.Existing.Contains(Path.GetFullPath(p)));
        fixture = new Fixture { Files = files, Context = ctx, Source = source };
        return fixture;
    }

    private static LinkRequest Request(Fixture f, LinkDestination? dest = null, LinkCopy? copy = null, LinkOptions? options = null,
        string sourceStation = "1", string workspaceFile = "deep.th", string workspaceStation = "2", string workspaceSurvey = "deep") =>
        new(f.Source,
            [
                new LinkMember(LinkSide.Source, f.Source.FilePath, QualifiedName.Of("newtrip", sourceStation)),
                new LinkMember(LinkSide.Workspace, P(workspaceFile), QualifiedName.Of(workspaceSurvey, workspaceStation)),
            ],
            dest ?? new LinkDestination(P("main.th"), S("cave")),
            copy ?? LinkCopy.InPlace,
            options ?? new LinkOptions());

    private static string Applied(Fixture f, LinkPlan plan, string file = "main.th")
    {
        var group = plan.Batch.Edits.Single(e => e.FilePath == P(file));
        return TextSplicer.Splice(f.Files[P(file)], group.Edits)!;
    }

    [Fact]
    public void Default_scope_is_the_parent_survey_unless_nested()
    {
        var station = QualifiedName.Of("cave", "deep", "2");

        Assert.Equal(S("cave"), SurveyLinkPlanner.DefaultScope(station, nest: false));
        Assert.Equal(S("cave", "deep"), SurveyLinkPlanner.DefaultScope(station, nest: true));
        Assert.Equal(S("cave"), SurveyLinkPlanner.DefaultScope(QualifiedName.Of("cave", "5"), nest: false));
    }

    [Fact]
    public void Inserts_input_after_the_last_input_and_equate_after_the_last_equate()
    {
        var f = Make();

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f));

        Assert.True(plan.CanApply, string.Join(", ", plan.Warnings));
        Assert.Equal("input incoming/new.th", plan.InputLine);
        Assert.Equal("equate 1@newtrip 2@deep", plan.EquateLine);
        Assert.True(plan.Batch.RequiresWorkspaceReload);
        Assert.Equal(
            "encoding utf-8\n" +
            "survey cave -title \"Cave\"\n" +
            "  input entrance.th\n" +
            "  input deep.th\n" +
            "  input incoming/new.th\n" +
            "\n" +
            "  equate 3@entrance 1@deep\n" +
            "  equate 1@newtrip 2@deep\n" +
            "endsurvey\n",
            Applied(f, plan));
    }

    [Fact]
    public void Comment_goes_above_each_inserted_group()
    {
        var f = Make();

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, options: new LinkOptions(Comment: "Added on 2026.10.28 from source file new.th")));

        var text = Applied(f, plan);
        Assert.Contains("  # Added on 2026.10.28 from source file new.th\n  input incoming/new.th\n", text);
        Assert.Contains("  # Added on 2026.10.28 from source file new.th\n  equate 1@newtrip 2@deep\n", text);
    }

    [Fact]
    public void Empty_block_gets_both_lines_after_its_header_indented_one_level()
    {
        var files = DefaultFiles();
        files[P("main.th")] = "survey cave\nendsurvey\n";
        files[P("other.th")] = "survey outer\n  survey cave\n  endsurvey\nendsurvey\n";
        files.Remove(P("entrance.th"));
        var f = Make(files);
        // `deep` sits at the root, outside outer.cave: no equate can be written from there.
        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, dest: new LinkDestination(P("other.th"), S("outer", "cave")),
            workspaceFile: "deep.th"));

        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.ScopeNotAncestor);

        var plan2 = SurveyLinkPlanner.Plan(f.Context, Request(f, dest: new LinkDestination(P("other.th"), S("outer", "cave")),
            options: new LinkOptions(InsertEquate: false)));
        Assert.Equal("survey outer\n  survey cave\n    input incoming/new.th\n  endsurvey\nendsurvey\n", Applied(f, plan2, "other.th"));
    }

    [Fact]
    public void Root_destination_goes_after_the_encoding_line()
    {
        var files = DefaultFiles();
        files[P("links.th")] = "encoding utf-8\nequate 1@deep 1@entrance\n";
        var f = Make(files);

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, dest: new LinkDestination(P("links.th"), S()),
            options: new LinkOptions(InsertEquate: false)));

        Assert.Equal("encoding utf-8\ninput incoming/new.th\nequate 1@deep 1@entrance\n", Applied(f, plan, "links.th"));
    }

    [Fact]
    public void Crlf_files_get_crlf_lines()
    {
        var files = DefaultFiles();
        files[P("main.th")] = Main.Replace("\n", "\r\n");
        var f = Make(files);

        var text = Applied(f, SurveyLinkPlanner.Plan(f.Context, Request(f)));

        Assert.Contains("  input deep.th\r\n  input incoming/new.th\r\n", text);
        Assert.Contains("  equate 1@newtrip 2@deep\r\nendsurvey\r\n", text);
        Assert.Equal(text.Split('\n').Length, text.Split("\r\n").Length); // every line break is CRLF
    }

    [Fact]
    public void Unterminated_destination_block_blocks()
    {
        var files = DefaultFiles();
        files[P("main.th")] = "survey cave\n  input entrance.th\n  input deep.th\n";
        var f = Make(files);

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f));

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.UnterminatedBlock);
    }

    [Fact]
    public void Station_outside_the_destination_scope_blocks()
    {
        var f = Make();

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, dest: new LinkDestination(P("entrance.th"), S("entrance"))));

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.ScopeNotAncestor);
    }

    [Fact]
    public void Already_input_file_and_existing_equate_are_reported()
    {
        var files = DefaultFiles();
        files[P("main.th")] = Main.Replace("  input deep.th\n", "  input deep.th\n  input incoming/new.th\n")
                                  .Replace("1@deep\n", "1@deep\n  equate 1@newtrip 2@deep\n");
        files[P("incoming/new.th")] = NewTrip;
        var f = Make(files);

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f));

        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.InputExists && w.Subject == P("main.th"));
        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.EquateExists && w.Subject == P("main.th"));
        Assert.True(plan.CanApply); // the user may insert anyway
    }

    [Fact]
    public void Copies_source_and_sketches_keeping_relative_paths()
    {
        var f = Make(sourcePath: "../outside/new.th");
        var sketch = P("../outside/sketches/plan.th2");

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, copy: new LinkCopy(true, P("trips"), [sketch])));

        Assert.Equal("input trips/new.th", plan.InputLine);
        Assert.Equal(
            [new FileCopy(P("../outside/new.th"), P("trips/new.th")), new FileCopy(sketch, P("trips/sketches/plan.th2"))],
            plan.Batch.Copies);
        Assert.DoesNotContain(plan.Warnings, w => w.Code == LinkWarningCode.SketchNotCopied);
    }

    [Fact]
    public void Leaving_out_an_input_sketch_warns_and_existing_targets_block()
    {
        var f = Make(sourcePath: "../outside/new.th");
        f.Existing.Add(P("trips/new.th"));

        var plan = SurveyLinkPlanner.Plan(f.Context, Request(f, copy: new LinkCopy(true, P("trips"), [])));

        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.SketchNotCopied && w.Subject == P("../outside/sketches/plan.th2"));
        Assert.Contains(plan.Warnings, w => w.Code == LinkWarningCode.TargetExists);
        Assert.False(plan.CanApply);
    }

    [Fact]
    public void Candidates_rank_block_with_equates_then_equate_file_then_plain_block_then_ancestors()
    {
        var files = DefaultFiles();
        files[P("main.th")] = "survey cave\n  input entrance.th\n  input deep.th\n  input links.th\n  equate 3@entrance 1@deep\n  survey annex\n  endsurvey\nendsurvey\n";
        files[P("links.th")] = "equate 2@entrance 2@deep\n";
        var f = Make(files);

        var candidates = SurveyLinkPlanner.Candidates(f.Context, S("cave"));

        Assert.Equal(
            [(P("main.th"), DestinationReason.BlockWithEquates), (P("links.th"), DestinationReason.EquateFile)],
            candidates.Select(c => (c.FilePath, c.Reason)));

        var nested = SurveyLinkPlanner.Candidates(f.Context, S("cave", "annex"));
        Assert.Equal(DestinationReason.Block, nested[0].Reason);
        Assert.Equal(S("cave", "annex"), nested[0].Scope);
        Assert.Equal(S("cave"), nested[1].Scope); // the enclosing scope, ranked lower
    }

    [Fact]
    public void Default_target_directory_follows_the_blocks_inputs()
    {
        var files = DefaultFiles();
        files[P("main.th")] = "survey cave\n  input trips/a.th\n  input trips/b.th\n  input deep.th\nendsurvey\n";
        var f = Make(files);

        Assert.Equal(P("trips"), SurveyLinkPlanner.DefaultTargetDirectory(f.Context, new LinkDestination(P("main.th"), S("cave"))));
    }

    [Fact]
    public void Sketches_marks_the_ones_the_source_inputs()
    {
        var f = Make();

        var sketches = SurveyLinkPlanner.Sketches(f.Source, [P("incoming/sketches/plan.th2"), P("incoming/old.th2")]);

        Assert.Equal([(P("incoming/old.th2"), false), (P("incoming/sketches/plan.th2"), true)], sketches);
    }

    [Fact]
    public void Split_reference_tokens_are_joined()
    {
        Assert.Equal(["1@a", "2@b.c", "3"], SurveyLinkPlanner.JoinSplitReferences(["1", "@a", "2@b.c", "3"]));
    }
}
