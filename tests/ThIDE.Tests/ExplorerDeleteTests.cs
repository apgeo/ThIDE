// The Workspace Explorer's Delete without a window: nothing goes without a yes, and a delete for good
// after the trash refused needs a second, explicit yes — never on a platform that deletes for good anyway.

using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Tests;

public class ExplorerDeleteTests
{
    private sealed class FileOps(bool undoable, bool trashWorks, bool permanentWorks = true) : IFileOperations
    {
        public List<string> Calls { get; } = [];
        public bool DeleteIsUndoable => undoable;
        public string DeleteActionLabel => undoable ? "To the bin" : "For good";
        public bool Delete(string path) { Calls.Add("delete " + Path.GetFileName(path)); return trashWorks; }
        public bool DeletePermanently(string path) { Calls.Add("permanently " + Path.GetFileName(path)); return permanentWorks; }
    }

    private const string Item = "/project/cave.th";

    /// <summary>Answers the questions in turn and records the confirming button of each.</summary>
    private static Func<string, string, Task<bool>> Answers(List<(string Message, string Button)> asked, params bool[] answers) =>
        (message, button) => { asked.Add((message, button)); return Task.FromResult(answers[asked.Count - 1]); };

    [Fact]
    public async Task No_yes_deletes_nothing()
    {
        var ops = new FileOps(undoable: true, trashWorks: true);
        var asked = new List<(string Message, string Button)>();

        Assert.False(await ExplorerDelete.RunAsync(Item, ops, Answers(asked, false)));

        Assert.Empty(ops.Calls);
        Assert.Equal("To the bin", Assert.Single(asked).Button);
        Assert.Contains("cave.th", asked[0].Message);
    }

    [Fact]
    public async Task A_yes_moves_it_to_the_trash_and_asks_nothing_more()
    {
        var ops = new FileOps(undoable: true, trashWorks: true);
        var asked = new List<(string Message, string Button)>();

        Assert.True(await ExplorerDelete.RunAsync(Item, ops, Answers(asked, true)));

        Assert.Equal(["delete cave.th"], ops.Calls);
        Assert.Single(asked);
    }

    [Theory]
    [InlineData(false, new[] { "delete cave.th" }, false)]
    [InlineData(true, new[] { "delete cave.th", "permanently cave.th" }, true)]
    public async Task When_the_trash_refuses_only_a_second_yes_deletes_for_good(bool secondYes, string[] calls, bool gone)
    {
        var ops = new FileOps(undoable: true, trashWorks: false);
        var asked = new List<(string Message, string Button)>();

        Assert.Equal(gone, await ExplorerDelete.RunAsync(Item, ops, Answers(asked, true, secondYes)));

        Assert.Equal(calls, ops.Calls);
        Assert.Equal(2, asked.Count);
        Assert.Equal(Tr.Get("Explorer_DeletePermanently"), asked[1].Button);
        Assert.Contains("cave.th", asked[1].Message);
    }

    [Fact]
    public async Task A_permanent_delete_that_fails_is_not_asked_again()
    {
        var ops = new FileOps(undoable: false, trashWorks: false);
        var asked = new List<(string Message, string Button)>();

        Assert.False(await ExplorerDelete.RunAsync(Item, ops, Answers(asked, true)));

        Assert.Equal(["delete cave.th"], ops.Calls);
        Assert.Equal("For good", Assert.Single(asked).Button);
    }

    [Fact]
    public async Task Without_file_operations_it_asks_with_the_plain_label_and_deletes_nothing()
    {
        var asked = new List<(string Message, string Button)>();

        Assert.False(await ExplorerDelete.RunAsync(Item, null, Answers(asked, true)));

        Assert.Equal(Tr.Get("Ctx_Delete"), Assert.Single(asked).Button);
    }
}
