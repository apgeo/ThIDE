using ThIDE.Services;

namespace ThIDE.Tests;

// The watchdog must never fight a real modal: a window is only "stuck" when it stays disabled while
// owning no visible window, for several consecutive samples.
public class ModalDesyncDetectorTests
{
    private static int Run(params (bool Enabled, bool OwnsWindow)[] samples)
    {
        var strikes = 0;
        foreach (var (enabled, owns) in samples)
            strikes = ModalDesyncDetector.NextStrikes(enabled, owns, strikes);
        return strikes;
    }

    [Fact]
    public void An_enabled_window_never_accrues_strikes()
        => Assert.False(ModalDesyncDetector.ShouldRecover(Run((true, false), (true, false), (true, false), (true, false))));

    [Fact]
    public void A_disabled_window_owning_a_dialog_is_a_normal_modal()
        => Assert.False(ModalDesyncDetector.ShouldRecover(Run((false, true), (false, true), (false, true), (false, true))));

    [Fact]
    public void A_disabled_window_owning_nothing_is_recovered_after_the_threshold()
    {
        var strikes = Run((false, false), (false, false), (false, false));
        Assert.Equal(ModalDesyncDetector.StrikesBeforeRecovery, strikes);
        Assert.True(ModalDesyncDetector.ShouldRecover(strikes));
    }

    // ShowDialog disables the owner a moment before the dialog window exists; one sample landing in
    // that gap must not trip the watchdog.
    [Fact]
    public void A_single_sample_inside_the_ShowDialog_gap_does_not_recover()
        => Assert.False(ModalDesyncDetector.ShouldRecover(Run((true, false), (false, false))));

    [Fact]
    public void A_dialog_appearing_mid_count_resets_the_strikes()
    {
        var strikes = Run((false, false), (false, false), (false, true));
        Assert.Equal(0, strikes);
        Assert.False(ModalDesyncDetector.ShouldRecover(strikes));
    }

    [Fact]
    public void Strikes_reset_once_the_window_is_enabled_again()
        => Assert.Equal(0, Run((false, false), (false, false), (true, false)));
}
