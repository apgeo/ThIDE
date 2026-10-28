namespace ThIDE.UiTests;

/// <summary>The Mainline Preview dock panel, which now hosts the reusable preview view.</summary>
public class PreviewTests
{
    [UiFact]
    public async Task Mainline_preview_panel_draws_the_project_with_its_toolbar_and_window_controls()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("View > Mainline Preview");
        await app.WaitAsync("#LivePreview.Sketch");

        Assert.True(await app.ExistsAsync("#LivePreview.WindowControls"), "dock window controls missing");
        Assert.True(await app.ExistsAsync("#LivePreview.ColorSurvey"), "toolbar missing");
        var count = 0;
        for (int i = 0; i < 40 && count == 0; i++, await Task.Delay(250))
        {
            var props = (await app.GetAsync("#LivePreview.Sketch", "Segments.Count")).GetProperty("properties");
            int.TryParse(props.GetProperty("Segments.Count").GetString(), out count);
        }
        Assert.True(count > 0, "the sketch drew no legs");

        await app.InvokeAsync("#LivePreview.ColorSurvey");
        var png = await app.ScreenshotAsync("#LivePreview.View");
        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "live-preview-panel.png"), png);
    }
}
