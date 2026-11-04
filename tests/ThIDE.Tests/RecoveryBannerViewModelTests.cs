// The crash-recovery offer pinned above the editor area: it names the files, Discard asks first (the
// copies cannot be brought back), and only the first answer — banner, toast or bell — does anything.
// All paths are invented.

using System.Threading.Tasks;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class RecoveryBannerViewModelTests
{
    private static readonly string[] Paths = ["/invented/cave/main.th", "/invented/cave/data/trip1.th"];

    private sealed class Calls
    {
        public int Restored, Discarded;
    }

    private static (RecoveryBannerViewModel Vm, Calls Calls) Offered()
    {
        var calls = new Calls();
        var vm = new RecoveryBannerViewModel();
        vm.Offer(Paths, () => { calls.Restored++; return Task.CompletedTask; }, () => calls.Discarded++);
        return (vm, calls);
    }

    [Fact]
    public void The_offer_names_the_recovered_files()
    {
        var (vm, _) = Offered();

        Assert.True(vm.IsOpen);
        Assert.Equal("main.th, trip1.th", vm.Files);
        Assert.Contains("2", vm.Message);
        Assert.Contains("/invented/cave/data/trip1.th", vm.Paths);
    }

    [Fact]
    public async Task Discard_asks_first_and_a_no_keeps_the_copies_and_the_banner()
    {
        var (vm, calls) = Offered();
        string? asked = null;
        vm.Confirm = q => { asked = q; return Task.FromResult(false); };

        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.NotNull(asked);
        Assert.Contains("main.th, trip1.th", asked);
        Assert.Equal(0, calls.Discarded);
        Assert.True(vm.IsOpen);

        vm.Confirm = _ => Task.FromResult(true);
        await vm.DiscardCommand.ExecuteAsync(null);
        Assert.Equal(1, calls.Discarded);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Only_the_first_answer_counts()
    {
        var (vm, calls) = Offered();
        vm.Confirm = _ => Task.FromResult(true);

        await vm.RestoreCommand.ExecuteAsync(null);
        await vm.DiscardCommand.ExecuteAsync(null);   // the toast's button, clicked afterwards
        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(1, calls.Restored);
        Assert.Equal(0, calls.Discarded);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Restore_chosen_while_the_discard_question_is_open_wins()
    {
        var (vm, calls) = Offered();
        var answer = new TaskCompletionSource<bool>();
        vm.Confirm = _ => answer.Task;

        var discard = vm.DiscardCommand.ExecuteAsync(null);
        await vm.RestoreCommand.ExecuteAsync(null);
        answer.SetResult(true);
        await discard;

        Assert.Equal(1, calls.Restored);
        Assert.Equal(0, calls.Discarded);
    }
}
