namespace ThIDE.UiTests;

/// <summary>File ▸ New File… creates a file from a template and opens it.</summary>
public class NewFileTests
{
    [UiFact]
    public async Task New_file_from_the_trip_template_is_created_and_opened()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("File > New File…");
        await app.WaitAsync("#NewFile.Window");
        await app.SetTextAsync("#NewFile.Folder", dir);
        await app.SetTextAsync("#NewFile.Name", "trip1.th");
        Assert.Contains("survey trip1", await app.TextOfAsync("#NewFile.Preview"));

        await app.InvokeAsync("#NewFile.Create");
        await app.WaitAsync("#NewFile.Window", gone: true, timeoutMs: 10_000);
        await app.WaitAsync("role:TabItem;name:trip1.th");

        Assert.StartsWith("encoding utf-8\nsurvey trip1", File.ReadAllText(Path.Combine(dir, "trip1.th")));
    }
}
