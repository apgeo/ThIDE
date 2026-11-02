using System.Diagnostics.CodeAnalysis;
using Therion.Mcp.Mutations;
using Therion.Workspace.Editing;

namespace Therion.Mcp.Tests;

/// <summary>
/// Between reading a file and writing it back, the engine runs the whole-project "before" analysis.
/// Anything that lands on the file in that window — another tool call, the user's Ctrl+S — must not be
/// overwritten by text computed from the earlier read, and never silently.
/// </summary>
public class MutationEngineConcurrentWriteTests
{
    private const string ExternalLine = "# saved by someone else meanwhile";

    [Fact]
    public async Task A_file_changed_after_it_was_read_is_not_overwritten()
    {
        using var fixture = FixtureWorkspace.Create();
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var engine = new MutationEngine(host);
        var target = fixture.PathTo("caves", "upper.th");

        var text = File.ReadAllText(target);
        int start = text.IndexOf("upper", StringComparison.Ordinal);
        var plan = new MutationPlan([new EditFile(target, [new TextEdit(start, "upper".Length, "upper", "lower")])]);

        // The precondition lookup runs after the engine has read the file and before it writes: the one
        // seam inside that window. Use it to stand in for a concurrent writer that appends a line.
        var concurrentWriter = new WriteOnLookup(() => File.AppendAllText(target, "\n" + ExternalLine + "\n"));

        var result = await engine.ApplyAsync(plan, dryRun: false, expectedSha256: concurrentWriter);

        Assert.True(concurrentWriter.Fired, "the test hook never ran; the window it stands in for has moved");
        var after = File.ReadAllText(target);
        Assert.True(after.Contains(ExternalLine, StringComparison.Ordinal),
            $"the concurrent write was lost, and the tool reported ok={result.Ok} ({result.Error?.Code ?? "no error"})");

        await host.DisposeAsync();
    }

    /// <summary>An empty precondition map whose lookup has a side effect, so a write can be placed inside the window.</summary>
    private sealed class WriteOnLookup(Action onLookup) : IReadOnlyDictionary<string, string>
    {
        public bool Fired { get; private set; }

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
        {
            if (!Fired) { Fired = true; onLookup(); }
            value = null;
            return false;
        }

        public string this[string key] => throw new KeyNotFoundException();
        public IEnumerable<string> Keys => [];
        public IEnumerable<string> Values => [];
        public int Count => 0;
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
