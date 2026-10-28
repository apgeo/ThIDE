namespace ThIDE.UiTests;

/// <summary>A survey export dropped into a watched incoming folder is offered for import.</summary>
public class IncomingTests
{
    [UiFact]
    public async Task A_file_landing_in_the_incoming_folder_is_offered()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        Directory.CreateDirectory(Path.Combine(dir, ".thide"));
        Directory.CreateDirectory(Path.Combine(dir, "incoming"));
        File.WriteAllText(Path.Combine(dir, ".thide", "project.json"), """{ "version": 1, "incoming": { "folder": "incoming", "enabled": true } }""");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
        await Task.Delay(1500);   // let the watcher start

        File.WriteAllText(Path.Combine(dir, "incoming", "trip.th"), "encoding utf-8\n# Invented.\nsurvey trip\nendsurvey\n");

        await app.WaitAsync("name~:New survey export: trip.th", timeoutMs: 20_000);
    }
}
