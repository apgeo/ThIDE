// T-03.5: the run_command allowlist. The coverage test is the important one — it reflects over every
// ShellCommandIds constant and fails the build if a new command is added without a classification, so
// a command can never become silently runnable (or silently unrunnable) by an agent.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Therion.Mcp;
using Therion.Processing.Abstractions;
using Xunit;

namespace Therion.Mcp.Tests;

public class R3CommandPolicyTests
{
    private static IEnumerable<string> AllShellCommandIds() =>
        typeof(ShellCommandIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Every_shell_command_id_is_classified()
    {
        foreach (var id in AllShellCommandIds())
            Assert.True(R3CommandPolicy.Find(id) is not null, $"ShellCommandIds.{id} has no run_command classification.");
    }

    [Fact]
    public void The_policy_lists_each_command_exactly_once_and_nothing_extra()
    {
        var known = AllShellCommandIds().ToHashSet();
        var classified = R3CommandPolicy.AllCommands.Select(c => c.Id).ToList();

        Assert.Equal(classified.Count, classified.Distinct().Count());       // no duplicates
        Assert.All(classified, id => Assert.Contains(id, known));            // nothing invented
        Assert.Equal(known.Count, classified.Count);                        // exhaustive
    }

    [Fact]
    public void Runnable_commands_exclude_excluded_and_editor_scope()
    {
        Assert.All(R3CommandPolicy.RunnableCommands, c =>
        {
            Assert.Equal(R3CommandScope.Shell, c.Scope);
            Assert.NotEqual(R3CommandGate.Excluded, c.Gate);
        });

        // Spot-check the doc-03 §C.3 buckets.
        Assert.Equal(R3CommandGate.Allowed, R3CommandPolicy.Find(ShellCommandIds.ToggleDiagnostics)!.Gate);
        Assert.Equal(R3CommandGate.Gated, R3CommandPolicy.Find(ShellCommandIds.Save)!.Gate);
        Assert.Equal(R3CommandGate.Excluded, R3CommandPolicy.Find(ShellCommandIds.OpenFile)!.Gate);
        Assert.Equal(R3CommandScope.Editor, R3CommandPolicy.Find(ShellCommandIds.FormatDocument)!.Scope);
    }
}
