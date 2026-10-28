namespace ThIDE.UiTests;

/// <summary>Tools ▸ Format all files previews the changes and writes them.</summary>
public class FormatAllTests
{
    [UiFact]
    public async Task Format_all_previews_then_rewrites_the_messy_file()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        var demo = Path.Combine(dir, "demo.th");
        File.WriteAllText(demo, File.ReadAllText(demo).Replace("\n    ", "\n"));   // strip the indentation
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Format all files…");
        await app.WaitAsync("#EditPreview.Window");
        Assert.Contains("demo.th", await app.TreeAsync("#EditPreview.Items"));
        await app.InvokeAsync("#EditPreview.Apply");
        await app.WaitAsync("#EditPreview.Window", gone: true, timeoutMs: 10_000);

        for (int i = 0; i < 40 && !File.ReadAllText(demo).Contains("\n    data normal"); i++) await Task.Delay(250);
        Assert.Contains("\n    data normal", File.ReadAllText(demo));
    }
}
