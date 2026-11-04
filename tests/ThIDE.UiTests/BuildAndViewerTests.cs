namespace ThIDE.UiTests;

/// <summary>Compile with the real Therion and look at the result. Skips itself when Therion isn't installed.</summary>
public class BuildAndViewerTests
{
    private static bool HasTherion() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(d => File.Exists(Path.Combine(d, OperatingSystem.IsWindows() ? "therion.exe" : "therion")));

    [UiFact]
    public async Task Build_runs_therion_and_produces_the_model()
    {
        if (!HasTherion()) return; // nothing to check without the toolchain
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.InvokeAsync("#Toolbar.Compile");
        for (int i = 0; i < 120 && !File.Exists(Path.Combine(dir, "demo.lox")); i++) await Task.Delay(500);

        Assert.True(File.Exists(Path.Combine(dir, "demo.lox")), "Therion did not write demo.lox\n" + app.Output);
        Assert.True(File.Exists(Path.Combine(dir, "demo.3d")));
    }

    [UiFact]
    public async Task Viewer_3d_page_reports_the_loaded_model_back_to_the_app()
    {
        if (!HasTherion()) return;
        var dir = ThideApp.CopyFixture("demo-cave");
        await using (var build = await ThideApp.StartAsync(dir, "demo.thconfig"))
        {
            await build.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
            await build.InvokeAsync("#Toolbar.Compile");
            for (int i = 0; i < 120 && !File.Exists(Path.Combine(dir, "demo.lox")); i++) await Task.Delay(500);
        }

        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:3D Viewer");
        await app.InvokeAsync("role:TabItem;name:3D Viewer");
        // The status line only changes from "Loading…" once CaveView's page has answered over the JS bridge.
        await app.WaitAsync("#Viewer3D.View >> role:Text;name:demo.lox", timeoutMs: 30_000);
    }

    [UiFact]
    public async Task Show_in_3d_flies_to_a_station_and_reports_what_it_found()
    {
        if (!HasTherion()) return;
        var dir = ThideApp.CopyFixture("demo-cave");
        await using (var build = await ThideApp.StartAsync(dir, "demo.thconfig"))
        {
            await build.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
            await build.InvokeAsync("#Toolbar.Compile");
            for (int i = 0; i < 120 && !File.Exists(Path.Combine(dir, "demo.lox")); i++) await Task.Delay(500);
        }
        Assert.True(File.Exists(Path.Combine(dir, "demo.lox")), "Therion did not write demo.lox");
        // A station added after the build: the source has it, the model does not.
        var th = Path.Combine(dir, "demo.th");
        File.WriteAllText(th, File.ReadAllText(th).Replace("2 5 9.0 300 -30", "2 5 9.0 300 -30\n    5 6 4.0 10 0"));

        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig", "demo.th");
        await app.WaitAsync("role:TabItem;name:3D Viewer");
        await app.InvokeAsync("role:TabItem;name:3D Viewer");
        // The status line names the model alone once CaveView's page has reported it loaded.
        await app.WaitAsync("#Viewer3D.Status;name:demo.lox", timeoutMs: 30_000);

        // The station grid of demo.th: its "Show in 3D View" command on the selected row.
        async Task ShowIn3D(string station)
        {
            await app.InvokeAsync("role:TabItem;name:demo.th");
            await app.InvokeAsync("#Document.Tab.Measurements", "select");
            await app.InvokeAsync("#Document.Measurements.Tab.Stations", "select");
            await app.SetTextAsync("#Document.Stations.Filter", station);
            await app.WaitAsync($"#Document.Stations.Grid >> #CellTextBlock;name:{station}");
            await app.InvokeAsync("#Document.Stations.Grid", "focus");
            await app.KeyAsync("#Document.Stations.Grid", "Down");   // selects the one row left
            await app.InvokeAsync("#Document.Stations.Grid", "contextmenu");
            await app.InvokeAsync("#Document.Stations.Grid.ShowIn3D");
        }

        // The status line takes the page's answer: the station was found in the model and marked…
        await ShowIn3D("3");
        await app.WaitAsync("#Viewer3D.Status;name:Showing demo.3 in 3D.", timeoutMs: 30_000);

        // …and a name the model does not have is said to be missing, not silently ignored.
        await ShowIn3D("6");
        await app.WaitAsync("#Viewer3D.Status;name~:demo\\.6.*not in the loaded model", timeoutMs: 30_000);
    }
}
