// Adding members to a map over generated .th files with LF, CRLF and lone-CR line breaks: the
// insertion uses the file's own line break and never splits a CRLF, only the target map gains
// members (exactly the new ones, after its existing ones), and planning the same members again on
// the result adds nothing. Seed fixed; THIDE_PROPERTY_SEED / THIDE_PROPERTY_RUNS override it.

using System.Text;
using Therion.Syntax;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public class MapMemberEditsLineEndingTests
{
    private const string Path = "/virtual/maps.th";

    private static int Seed =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : 161_000;

    private static int Runs =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS"), out var n) && n > 0 ? n : 300;

    private static string Apply(string text, TextEdit edit) =>
        text[..edit.Start] + edit.NewText + text[(edit.Start + edit.Length)..];

    private static IReadOnlyList<string> MembersOf(string text, string mapId) =>
        MapMemberEdits.FindMap(TherionWorkspace.ParseText(Path, text).Value!.Children, mapId)!.Members.Select(m => m.Id).ToList();

    /// <summary>A survey with up to three maps (some empty), one break style, maybe no final break.</summary>
    private static (string Text, string Break, List<string> Maps) Generate(Random rnd)
    {
        string br = rnd.Next(3) switch { 0 => "\n", 1 => "\r\n", _ => "\r" };
        var sb = new StringBuilder();
        var maps = new List<string>();
        sb.Append("survey cave").Append(br);
        int count = rnd.Next(1, 4);
        for (int m = 0; m < count; m++)
        {
            string id = "m" + m;
            maps.Add(id);
            string indent = rnd.Next(2) == 0 ? "  " : "\t";
            sb.Append(indent).Append($"map {id} -projection plan").Append(br);
            int members = rnd.Next(0, 4);
            for (int k = 0; k < members; k++)
                sb.Append(indent).Append("  ").Append($"s{m}_{k}").Append(rnd.Next(4) == 0 ? " # old" : "").Append(br);
            if (rnd.Next(4) == 0) sb.Append(indent).Append("  # endmap in a comment").Append(br);
            sb.Append(indent).Append("endmap").Append(br);
        }
        sb.Append("endsurvey");
        if (rnd.Next(4) != 0) sb.Append(br);
        return (sb.ToString(), br, maps);
    }

    private static List<string> BreakProblems(string text, string br)
    {
        var problems = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            bool crlf = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            string found = crlf ? "\\r\\n" : text[i] == '\r' ? "\\r" : text[i] == '\n' ? "\\n" : "";
            if (found.Length == 0) continue;
            if (found != br.Replace("\r", "\\r").Replace("\n", "\\n"))
                problems.Add($"a {found} break at {i} in a {br.Replace("\r", "\\r").Replace("\n", "\\n")} file");
            if (crlf) i++;
        }
        return problems;
    }

    [Fact]
    public void Insertions_keep_the_files_line_breaks_touch_only_the_target_map_and_are_idempotent()
    {
        for (int seed = Seed; seed < Seed + Runs; seed++)
        {
            var rnd = new Random(seed);
            var (text, br, maps) = Generate(rnd);
            string target = maps[rnd.Next(maps.Count)];
            var before = maps.ToDictionary(m => m, m => MembersOf(text, m));
            var adding = new List<string> { "new_a", "new_b" };
            if (before[target].Count > 0 && rnd.Next(2) == 0) adding.Add(before[target][0]);   // already there: skipped

            var plan = MapMemberEdits.Plan(Path, text, target, adding);
            Assert.True(plan.Problem == MapEditProblem.None, $"seed {seed}: {plan.Problem}");
            var edit = plan.Edit ?? throw new Xunit.Sdk.XunitException($"seed {seed}: no edit planned");
            Assert.Equal(new[] { "new_a", "new_b" }, plan.Added);

            var after = Apply(text, edit);
            var problems = BreakProblems(after, br);
            foreach (var m in maps)
            {
                var expected = m == target ? before[m].Concat(new[] { "new_a", "new_b" }).ToList() : before[m];
                var got = MembersOf(after, m);
                if (!got.SequenceEqual(expected))
                    problems.Add($"map {m}: [{string.Join(", ", got)}], expected [{string.Join(", ", expected)}]");
            }
            var again = MapMemberEdits.Plan(Path, after, target, adding);
            if (again.Edit is not null || again.Added.Count != 0)
                problems.Add($"planning again adds [{string.Join(", ", again.Added)}]");

            if (problems.Count > 0)
                Assert.Fail($"seed {seed} (map {target}):\n  {string.Join("\n  ", problems)}\n" +
                            $"before: \"{Escape(text)}\"\nafter:  \"{Escape(after)}\"");
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Member_goes_on_its_own_line_after_the_last_member(string br)
    {
        string text = string.Join(br, "survey a", "  map m", "    s1", "  endmap", "endsurvey", "");
        var edit = MapMemberEdits.Plan(Path, text, "m", new[] { "s2" }).Edit!;
        Assert.Equal(string.Join(br, "survey a", "  map m", "    s1", "    s2", "  endmap", "endsurvey", ""), Apply(text, edit));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Member_of_an_empty_map_is_indented_under_its_header(string br)
    {
        string text = string.Join(br, "survey a", "  map m", "  endmap", "endsurvey");
        var edit = MapMemberEdits.Plan(Path, text, "m", new[] { "s1" }).Edit!;
        Assert.Equal(string.Join(br, "survey a", "  map m", "    s1", "  endmap", "endsurvey"), Apply(text, edit));
    }

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
}
