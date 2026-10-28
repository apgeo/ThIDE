namespace ThIDE.UiTests;

/// <summary>A built-in macro previews into the Automation panel and asks before applying.</summary>
public class MacroTests
{
    [UiFact]
    public async Task Release_check_previews_then_asks()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Macros > Release check");
        await app.WaitAsync("#Confirm.Window", timeoutMs: 30_000);
        Assert.Contains("diag", await app.TextOfAsync("#Confirm.Message"));
        await app.InvokeAsync("#Confirm.Cancel");

        await app.WaitAsync("#Automation.Rows >> name~:get_diagnostics", timeoutMs: 10_000);
    }
}
