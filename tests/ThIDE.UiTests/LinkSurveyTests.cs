namespace ThIDE.UiTests;

/// <summary>Tools ▸ Link survey file over the invented demo cave and an invented new trip.</summary>
public class LinkSurveyTests
{
    private const string Trip =
        "encoding utf-8\n# Invented test data.\nsurvey trip\n  centreline\n    data normal from to length compass clino\n" +
        "    1 2 5.0 10 0\n    2 3 4.0 80 -5\n  endcentreline\nendsurvey\n";

    [UiFact]
    public async Task Links_a_new_trip_into_the_project_with_input_and_equate()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        Directory.CreateDirectory(Path.Combine(dir, "incoming"));
        var trip = Path.Combine(dir, "incoming", "trip.th");
        File.WriteAllText(trip, Trip);
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Link survey file…");
        await app.WaitAsync("#LinkSurvey.Window");
        await app.SetTextAsync("#LinkSurvey.SourcePath", trip);
        await app.InvokeAsync("#LinkSurvey.LoadSource");
        await app.WaitAsync("#LinkSurvey.SourceStatus;name~:^3 ");

        await app.SetTextAsync("#Picker.Source.Filter", "trip.1");
        await app.InvokeAsync("#Picker.Source.List >> role:ListItem");
        await app.SetTextAsync("#Picker.Station.Filter", "demo.3");
        await app.InvokeAsync("#Picker.Station.List >> role:ListItem");

        var generated = await app.TextOfAsync("#LinkSurvey.Generated");
        Assert.Contains("input incoming/trip.th", generated);
        Assert.Contains("equate 1@trip 3", generated);

        Directory.CreateDirectory(ThideApp.ArtifactsDir);
        await File.WriteAllBytesAsync(Path.Combine(ThideApp.ArtifactsDir, "link-survey-dialog.png"),
            await app.ScreenshotAsync("#LinkSurvey.Window"));

        await app.InvokeAsync("#LinkSurvey.Insert");
        await app.WaitAsync("#LinkSurvey.Window", gone: true, timeoutMs: 10_000);

        var demo = File.ReadAllText(Path.Combine(dir, "demo.th"));
        Assert.Contains("  input incoming/trip.th\n  equate 1@trip 3\n", demo);
    }
}
