namespace ThIDE.UiTests;

/// <summary>Tools ▸ Normalise project, Tools ▸ Renumber stations and Edit ▸ Paste as shot data, in the real app.</summary>
public class HouseKeepingTests
{
    [UiFact]
    public async Task Normalise_previews_then_rewrites_indentation_and_line_endings()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        var demo = Path.Combine(dir, "demo.th");
        File.WriteAllText(demo, File.ReadAllText(demo).Replace("\n    ", "\n").Replace("\n", "\r\n"));
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Normalise project…");
        await app.WaitAsync("#Normalize.Window");
        await SnapAsync(app, "#Normalize.Window", "normalize-dialog.png");
        await app.InvokeAsync("#Normalize.Next");
        await app.WaitAsync("#EditPreview.Window");
        await app.InvokeAsync("#EditPreview.Apply");
        await app.WaitAsync("#EditPreview.Window", gone: true, timeoutMs: 10_000);

        for (int i = 0; i < 40 && File.ReadAllText(demo).Contains('\r'); i++) await Task.Delay(250);
        var text = File.ReadAllText(demo);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("\n    data normal", text);
    }

    [UiFact]
    public async Task Renumber_gives_every_station_a_prefix()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        var demo = Path.Combine(dir, "demo.th");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Renumber stations…");
        await app.WaitAsync("#Renumber.Window");
        await app.SetTextAsync("#Renumber.NewPrefix", "D");
        for (int i = 0; i < 20 && !(await app.TreeAsync("#Renumber.Examples")).Contains("D1"); i++) await Task.Delay(250);
        await SnapAsync(app, "#Renumber.Window", "renumber-dialog.png");
        await app.InvokeAsync("#Renumber.Next");
        await app.WaitAsync("#EditPreview.Window");
        await app.InvokeAsync("#EditPreview.Apply");
        await app.WaitAsync("#EditPreview.Window", gone: true, timeoutMs: 10_000);

        for (int i = 0; i < 40 && !File.ReadAllText(demo).Contains("D0 D1"); i++) await Task.Delay(250);
        Assert.Contains("D0 D1 10.0", File.ReadAllText(demo));
        Assert.Contains("D2 D5 9.0", File.ReadAllText(demo));
    }

    [UiFact]
    public async Task Paste_as_shot_data_inserts_a_data_block()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.th");
        await app.WaitAsync("role:TabItem;name:demo.th;state:selected");
        await app.KeyAsync("type:TextArea", "Ctrl+End");

        await app.MenuAsync("Edit > Paste as shot data…");
        await app.WaitAsync("#PasteShots.Window");
        await app.SetTextAsync("#PasteShots.Source", "from,to,length,compass,clino\n7,8,2.5,10,-3\n");
        for (int i = 0; i < 20 && !(await app.TextOfAsync("#PasteShots.Output")).Contains("7 8 2.5"); i++) await Task.Delay(250);
        await SnapAsync(app, "#PasteShots.Window", "paste-shots-dialog.png");
        await app.InvokeAsync("#PasteShots.Insert");
        await app.WaitAsync("#PasteShots.Window", gone: true);

        var text = await app.TextOfAsync("type:TextEditor");
        Assert.Contains("centreline\n  data normal from to length compass clino\n  7 8 2.5 10 -3\nendcentreline", text.Replace("\r\n", "\n"));
    }

    [UiFact]
    public async Task Pasting_feet_into_a_centreline_restores_the_units_for_the_shots_after_it()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.th");
        await app.WaitAsync("role:TabItem;name:demo.th;state:selected");
        await app.KeyAsync("type:TextArea", ["Ctrl+Home", .. Enumerable.Repeat("Down", 8)]);   // start of "    1 2 8.0 45 -10"

        await app.MenuAsync("Edit > Paste as shot data…");
        await app.WaitAsync("#PasteShots.Window");
        await app.SetTextAsync("#PasteShots.Source", "from\tto\tlength (ft)\tcompass\tclino\n1\t1a\t20\t10\t-3\n");
        for (int i = 0; i < 20 && !(await app.TextOfAsync("#PasteShots.Output")).Contains("1a"); i++) await Task.Delay(250);
        await app.InvokeAsync("#PasteShots.Insert");
        await app.WaitAsync("#PasteShots.Window", gone: true);

        var text = (await app.TextOfAsync("type:TextEditor")).Replace("\r\n", "\n");
        Assert.Contains("    0 1 10.0 0 -5\n    units length feet\n    data normal from to length compass clino\n    1 1a 20 10 -3\n"
                      + "    units length dimensions meters\n    units length meters\n    units compass clino degrees\n    1 2 8.0 45 -10", text);
    }

    private static async Task SnapAsync(ThideApp app, string selector, string name)
    {
        var png = await app.ScreenshotAsync(selector);
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, name), png);
    }
}
