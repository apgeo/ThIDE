namespace ThIDE.UiTests;

/// <summary>A project task from .thide/tasks.json, run from the Tools menu in a folder trusted on the spot.</summary>
public class TaskTests
{
    [UiFact]
    public async Task Running_a_project_task_asks_for_trust_and_shows_its_output()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        Directory.CreateDirectory(Path.Combine(dir, ".thide"));
        File.WriteAllText(Path.Combine(dir, ".thide", "tasks.json"),
            """{ "version": 1, "tasks": [ { "id": "hello", "title": "Say hello", "command": "echo hello from task", "problemMatcher": "file-line" } ] }""");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.MenuAsync("Tools > Tasks > Say hello");
        await app.WaitAsync("role:Button;name:Trust and run", timeoutMs: 10_000);
        await app.InvokeAsync("role:Button;name:Trust and run");

        await app.WaitAsync("#Automation.Rows >> name~:hello from task", timeoutMs: 15_000);
    }
}
