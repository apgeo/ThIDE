// Approval test for the declarative .th command schemas (ThSchema): every command with its contexts,
// terminator, positional parameters (kind, required, repeated, range, enum table) and options,
// rendered to text and compared with ThSchema.approved.txt next to this file. The schema is
// descriptive data that the MCP describe tool and the completion read, and the same facts live in
// the centreline rules, so an unnoticed edit here would drift from them.
//
// A deliberate schema change: run the tests once with THIDE_UPDATE_SNAPSHOTS=1, read the diff of the
// approved file, and commit it with the change. SourceRef/Notes citations are left out of the
// rendering on purpose (a reworded citation is not a schema change).

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Therion.Syntax.Schema;

namespace Therion.Syntax.Tests.Schema;

public class ThSchemaSnapshotTests
{
    private static string ApprovedPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "ThSchema.approved.txt");

    private static string Sorted(IEnumerable<string> names) =>
        string.Join("|", names.OrderBy(n => n, StringComparer.Ordinal));

    private static string Value(ValueSpec v)
    {
        var sb = new StringBuilder(v.Kind.ToString());
        if (v.Charset is { } cs) sb.Append(":charset=").Append(cs);
        if (v.Range is { } r) sb.Append(":range=").Append(r);
        if (v.Enum is { } e) sb.Append(":enum{").Append(Sorted(e)).Append('}');
        if (!v.CaseSensitive) sb.Append(":case-insensitive");
        return sb.ToString();
    }

    private static string Param(ParamSpec p) =>
        $"{p.Name}: {Value(p.Value)}{(p.Required ? "" : " optional")}{(p.Repeated ? " repeated" : "")}";

    private static void Options(StringBuilder sb, ImmutableArray<OptionSpec> options, string indent)
    {
        foreach (var o in options)
        {
            sb.Append(indent).Append("option -").Append(o.Name);
            if (o.Aliases.Length > 0) sb.Append(" aliases=").Append(Sorted(o.Aliases));
            if (o.Repeatable) sb.Append(" repeatable");
            if (o.Deprecated) sb.Append(" deprecated");
            if (o.ValidForTypes is { } t) sb.Append(" types=").Append(Sorted(t));
            sb.Append(" (").Append(string.Join("; ", o.Values.Select(Param))).Append(")\n");
        }
    }

    public static string Render()
    {
        var sb = new StringBuilder();
        sb.Append("# ThSchema snapshot: command [section] contexts, terminator, positional parameters, options\n");
        sb.Append("optionset ").Append(ThSchema.DataObjectOptions.Name).Append('\n');
        Options(sb, ThSchema.DataObjectOptions.Options, "  ");
        sb.Append("projection-types ").Append(Sorted(ThSchema.ProjectionTypes)).Append('\n');
        foreach (var c in ThSchema.Commands)
        {
            sb.Append('\n').Append(c.Keyword).Append(" [").Append(c.Section).Append("] in ")
                .Append(string.Join(",", c.Contexts));
            if (c.Terminator is { } term) sb.Append(" ends with ").Append(term);
            if (c.Aliases.Length > 0) sb.Append(" aliases=").Append(Sorted(c.Aliases));
            sb.Append('\n');
            foreach (var p in c.Positional) sb.Append("  arg ").Append(Param(p)).Append('\n');
            Options(sb, c.Options, "  ");
            foreach (var set in c.Inherits) sb.Append("  inherits ").Append(set.Name).Append('\n');
        }
        return sb.ToString();
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void The_th_command_schemas_match_the_approved_snapshot()
    {
        var path = ApprovedPath();
        var actual = Render();
        if (Environment.GetEnvironmentVariable("THIDE_UPDATE_SNAPSHOTS") == "1")
        {
            File.WriteAllText(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"approved snapshot missing: {path} (run once with THIDE_UPDATE_SNAPSHOTS=1)");
        var approved = Normalize(File.ReadAllText(path));
        if (approved == actual) return;

        // Point at the first differing line instead of dumping two long texts.
        var a = approved.Split('\n');
        var b = actual.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        Assert.Fail($"ThSchema differs from the approved snapshot at line {i + 1}:\n  approved: {(i < a.Length ? a[i] : "<end>")}\n  actual:   {(i < b.Length ? b[i] : "<end>")}\n" +
                    "If the change is intended, rerun with THIDE_UPDATE_SNAPSHOTS=1 and commit the new file.");
    }

    [Fact]
    public void Command_keywords_are_unique_per_context_and_every_block_has_its_end()
    {
        var seen = new HashSet<(string, SchemaContext)>();
        foreach (var c in ThSchema.Commands)
        {
            foreach (var ctx in c.Contexts)
                Assert.True(seen.Add((c.Keyword, ctx)), $"{c.Keyword} is defined twice in {ctx}");
            if (c.Terminator is { } t) Assert.Equal("end" + c.Keyword, t);
        }
    }

    [Fact]
    public void Enum_parameters_carry_a_table_and_only_enum_parameters_do()
    {
        foreach (var c in ThSchema.Commands)
            foreach (var p in c.Positional)
                Assert.Equal(p.Value.Kind == SchemaValueKind.Enum, p.Value.Enum is { Count: > 0 });
    }
}
