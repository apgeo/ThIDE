using TherionProc.Services;

namespace TherionProc.Tests;

// UX-07: the notification model carried by the toast + bell center.
public class NotificationTests
{
    [Fact]
    public void Action_command_invokes_the_action()
    {
        int called = 0;
        var n = new AppNotification("t", "m", NotificationKind.Success, "Show output", () => called++);

        Assert.True(n.HasAction);
        Assert.NotNull(n.ActionCommand);
        n.ActionCommand!.Execute(null);
        Assert.Equal(1, called);
    }

    [Fact]
    public void Notification_without_action_has_no_command()
    {
        var n = new AppNotification("t", "m", NotificationKind.Info);
        Assert.False(n.HasAction);
        Assert.Null(n.ActionCommand);
    }

    [Fact]
    public void Kind_is_preserved()
    {
        Assert.Equal(NotificationKind.Error,
            new AppNotification("x", "y", NotificationKind.Error).Kind);
    }
}
