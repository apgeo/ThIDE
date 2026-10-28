namespace ThIDE.UiTests;

/// <summary>File ▸ New Project… writes a project from a built-in template and opens its thconfig.</summary>
public class NewProjectTests
{
    [UiFact]
    public async Task Single_cave_project_is_created_and_its_thconfig_opened()
    {
        var target = Path.Combine(Path.GetTempPath(), "thide-ui-proj-" + Guid.NewGuid().ToString("N")[..8], "pit");
        await using var app = await ThideApp.StartAsync();
        await app.WaitAsync("#Welcome.View;state:showing");

        await app.MenuAsync("File > New Project…");
        await app.WaitAsync("#NewProject.Window");
        await app.SetTextAsync("#NewProject.Field.project", "pit");
        await app.SetTextAsync("#NewProject.Folder", target);
        await app.InvokeAsync("#NewProject.Create");
        await app.WaitAsync("#NewProject.Window", gone: true, timeoutMs: 10_000);
        await app.WaitAsync("role:TabItem;name:pit.thconfig", timeoutMs: 20_000);

        Assert.True(File.Exists(Path.Combine(target, "pit.th")));
        Assert.True(Directory.Exists(Path.Combine(target, "output")));
    }
}
