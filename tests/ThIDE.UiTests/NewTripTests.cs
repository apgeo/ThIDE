namespace ThIDE.UiTests;

/// <summary>Tools ▸ New trip… over the invented demo cave.</summary>
public class NewTripTests
{
    [UiFact]
    public async Task New_trip_is_written_and_tied_into_the_cave()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > New trip…");
        await app.WaitAsync("#NewTrip.Window");
        await app.SetTextAsync("#NewTrip.Date", "2026.10.21");
        await app.SetTextAsync("#Picker.TripStation.Filter", "demo.4");
        await app.InvokeAsync("#Picker.TripStation.List >> role:ListItem");
        await app.InvokeAsync("#NewTrip.Create");
        await app.WaitAsync("#NewTrip.Window", gone: true, timeoutMs: 10_000);
        await app.WaitAsync("role:TabItem;name:trip_2026_10_21.th");

        Assert.Contains("date 2026.10.21", File.ReadAllText(Path.Combine(dir, "trip_2026_10_21.th")));
        var demo = File.ReadAllText(Path.Combine(dir, "demo.th"));
        Assert.Contains("input trip_2026_10_21.th", demo);
        Assert.Contains("equate 0@trip_2026_10_21 4", demo);
    }
}
