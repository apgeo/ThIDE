namespace ThIDE.UiTests;

/// <summary>End-to-end smoke tests over the in-app UI-automation server. Uses invented fixture data only.</summary>
public class SmokeTests
{
    [UiFact]
    public async Task Starts_with_the_welcome_page_and_menu_bar()
    {
        await using var app = await ThideApp.StartAsync();
        await app.WaitAsync("#Welcome.View;state:showing");
        foreach (var menu in new[] { "File", "View", "Edit", "Search", "Tools", "Build", "Settings", "Help" })
            Assert.True(await app.ExistsAsync($"#Menu.{menu}"), $"menu {menu} missing");
    }

    [UiFact]
    public async Task Opens_a_project_passed_on_the_command_line()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");

        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
        Assert.Contains("source demo.th", await app.TextOfAsync("type:TextEditor"));
        await app.WaitAsync("#WorkspaceExplorer.Tree >> name~:^demo\\.th$");
    }

    [UiFact]
    public async Task Typing_in_the_editor_changes_the_buffer_and_undo_restores_it()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");
        var before = await app.TextOfAsync("type:TextEditor");

        await app.KeyAsync("type:TextArea", "Ctrl+End");
        await app.TypeAsync("# typed by a test");
        Assert.EndsWith("# typed by a test", await app.TextOfAsync("type:TextEditor"));

        await app.KeyAsync("type:TextArea", "Ctrl+Z");
        Assert.Equal(before, await app.TextOfAsync("type:TextEditor"));
    }

    [UiFact]
    public async Task View_menu_brings_a_tool_pane_forward()
    {
        await using var app = await ThideApp.StartAsync();
        await app.MenuAsync("View > Log");
        await app.WaitAsync("role:TabItem;name:Log;state:selected", timeoutMs: 5000);
    }

    [UiFact]
    public async Task Find_in_files_opens_as_a_window_and_closes_on_escape()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.MenuAsync("Search > Find in Files");
        await app.WaitAsync("#Search.Window");
        await app.KeyAsync("#Search.Window", "Escape");
        await app.WaitAsync("#Search.Window", gone: true, timeoutMs: 5000);
    }

    [UiFact]
    public async Task Toolbar_buttons_have_accessible_names()
    {
        await using var app = await ThideApp.StartAsync();
        var tree = await app.TreeAsync("#Toolbar.Panel");
        Assert.DoesNotContain("Avalonia.Controls", tree);
        Assert.Contains("#Toolbar.OpenFile", tree);
    }

    [UiFact]
    public async Task Screenshot_renders_the_main_window()
    {
        await using var app = await ThideApp.StartAsync();
        var png = await app.ScreenshotAsync();
        Assert.True(png.Length > 1000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }
}
