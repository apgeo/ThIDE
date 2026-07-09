using System.Collections.Generic;
using System.Linq;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.Tests;

// PageUp/PageDown move the selection by one full page of the overlay's 15-row list, clamping at
// both ends rather than wrapping or unselecting.
public class QuickPickPagingTests
{
    private const int PageSize = 15;

    private static QuickPickViewModel WithItems(int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => new QuickPickItem { Title = $"item{i}" })
            .ToList();
        return new QuickPickViewModel("t", "w", _ => (IReadOnlyList<QuickPickItem>)items);
    }

    [Fact]
    public void First_item_is_selected_when_the_palette_opens()
    {
        var vm = WithItems(40);
        Assert.Equal("item0", vm.Selected!.Title);
    }

    [Fact]
    public void PageDown_advances_by_one_page()
    {
        var vm = WithItems(40);
        vm.MovePageDown();
        Assert.Equal($"item{PageSize}", vm.Selected!.Title);
    }

    [Fact]
    public void PageDown_clamps_to_the_last_item()
    {
        var vm = WithItems(20);
        vm.MovePageDown();
        vm.MovePageDown();
        Assert.Equal("item19", vm.Selected!.Title);
    }

    [Fact]
    public void PageUp_clamps_to_the_first_item()
    {
        var vm = WithItems(40);
        vm.MovePageDown();
        vm.MovePageUp();
        vm.MovePageUp();
        Assert.Equal("item0", vm.Selected!.Title);
    }

    [Fact]
    public void PageDown_round_trips_with_PageUp()
    {
        var vm = WithItems(100);
        vm.MovePageDown();
        vm.MovePageDown();
        Assert.Equal($"item{PageSize * 2}", vm.Selected!.Title);
        vm.MovePageUp();
        Assert.Equal($"item{PageSize}", vm.Selected!.Title);
    }

    [Fact]
    public void Paging_an_empty_result_list_is_a_no_op()
    {
        var vm = WithItems(0);
        vm.MovePageDown();
        vm.MovePageUp();
        Assert.Null(vm.Selected);
    }
}
