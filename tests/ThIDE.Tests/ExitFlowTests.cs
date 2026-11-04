using ThIDE.Services;

namespace ThIDE.Tests;

// The main window's close sequence without a window: a close with unsaved documents is held and asked
// about once, a second request waits for that answer, and only the user's go-ahead (or an OS shutdown,
// which keeps the recovery copies) lets it through.
public class ExitFlowTests
{
    /// <summary>A window stand-in: closing it again runs the flow the way Window.Close re-raises Closing.</summary>
    private sealed class Window
    {
        public bool Unsaved { get; set; } = true;
        public Func<Task<bool>> Answer { get; set; } = () => Task.FromResult(false);
        public int Asked { get; private set; }
        public List<ExitStep> Shutdowns { get; } = new();
        public ExitFlow Flow { get; }

        public Window()
        {
            Flow = new ExitFlow(() => Unsaved, () => { Asked++; return Answer(); }, () => Close(osShutdown: false));
        }

        /// <summary>The Closing handler: returns the step, and records a shutdown when it goes through.</summary>
        public ExitStep Close(bool osShutdown)
        {
            var step = Flow.OnClosing(osShutdown);
            if (step is ExitStep.Exit or ExitStep.ExitKeepingRecovery) Shutdowns.Add(step);
            return step;
        }
    }

    [Fact]
    public void Nothing_unsaved_exits_without_asking_and_drops_the_recovery_copies()
    {
        var w = new Window { Unsaved = false };

        Assert.Equal(ExitStep.Exit, w.Close(osShutdown: false));
        Assert.Equal(ExitStep.Exit, new Window { Unsaved = false }.Close(osShutdown: true));
        Assert.Equal(0, w.Asked);
    }

    [Fact]
    public void An_os_shutdown_with_unsaved_documents_exits_at_once_and_keeps_the_recovery_copies()
    {
        var w = new Window();

        Assert.Equal(ExitStep.ExitKeepingRecovery, w.Close(osShutdown: true));
        Assert.Equal(0, w.Asked);
    }

    [Fact]
    public async Task Unsaved_documents_hold_the_close_and_a_second_request_waits_for_the_open_question()
    {
        var w = new Window();
        var answer = new TaskCompletionSource<bool>();
        w.Answer = () => answer.Task;

        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));
        var asking = w.Flow.AskAsync();
        Assert.Equal(ExitStep.Wait, w.Close(osShutdown: false));   // Alt+F4 again while the dialog is up
        Assert.Equal(1, w.Asked);

        answer.SetResult(false);   // Cancel
        await asking;

        Assert.Empty(w.Shutdowns);
        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));   // the next request asks again
    }

    [Fact]
    public async Task Letting_it_close_requests_the_close_again_which_then_goes_through_even_if_still_unsaved()
    {
        var w = new Window { Answer = () => Task.FromResult(true) };   // Don't save: the documents stay dirty

        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));
        await w.Flow.AskAsync();

        Assert.Equal(new[] { ExitStep.Exit }, w.Shutdowns);
        Assert.Equal(1, w.Asked);
        // Once the user has answered, nothing asks again, and an OS shutdown no longer keeps copies.
        Assert.Equal(ExitStep.Exit, w.Close(osShutdown: true));
    }

    [Fact]
    public async Task A_question_that_fails_is_not_left_open()
    {
        var w = new Window { Answer = () => Task.FromException<bool>(new InvalidOperationException("dialog")) };

        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));
        await Assert.ThrowsAsync<InvalidOperationException>(w.Flow.AskAsync);

        Assert.Empty(w.Shutdowns);
        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));   // not stuck on Wait
    }

    [Fact]
    public async Task Unsaved_state_is_read_at_each_request()
    {
        var w = new Window();
        Assert.Equal(ExitStep.Ask, w.Close(osShutdown: false));
        w.Answer = () => Task.FromResult(false);
        await w.Flow.AskAsync();

        w.Unsaved = false;   // the user saved by hand after cancelling
        Assert.Equal(ExitStep.Exit, w.Close(osShutdown: false));
    }
}
